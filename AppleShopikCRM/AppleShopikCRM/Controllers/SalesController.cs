using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Hubs;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
// Переконайтеся, що ваші namespace правильні

namespace AppleShopikCRM.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SalesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfService _pdfService;
        private readonly StorageService _storageService;
        private readonly TelegramService _telegramService;
        private readonly IHubContext<ChatHub> _hubContext;

        public SalesController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
    PdfService pdfService,          // <--- Нове
    StorageService storageService)  // <--- Нове
        {
            _context = context;
            _userManager = userManager;
            _pdfService = pdfService;
            _storageService = storageService;
            _telegramService = telegramService;
            _hubContext = hubContext;
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
        {
            if (request.Items == null || !request.Items.Any()) return BadRequest("Кошик порожній");

            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value
                  ?? User.FindFirst("Id")?.Value;

            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var storeInfo = await _context.Stores.FindAsync(request.StoreId);
                decimal currentExchangeRate = (storeInfo != null && storeInfo.ExchangeRate > 0) ? storeInfo.ExchangeRate : 41.5m;

                // ==========================================
                // 1. СТВОРЮЄМО ЗАГАЛЬНИЙ ЧЕК (SALE)
                // ==========================================
                var sale = new Sale
                {
                    StoreId = request.StoreId,
                    SaleDate = DateTime.Now,
                    TradeInDiscount = 0,
                    Comment = "Продаж через касу",
                    TotalAmount = 0,
                    FinalAmount = 0,
                    TotalProfit = 0
                };

                _context.Sales.Add(sale);
                await _context.SaveChangesAsync(); // Щоб отримати sale.Id

                decimal totalBankCommission = request.Payments?
                    .Where(p => p.Method == "Оплата частинами")
                    .Sum(p => p.CommissionAmount) ?? 0;

                decimal terminalAmount = request.Payments?
                    .Where(p => p.Method == "Термінал")
                    .Sum(p => p.Amount) ?? 0;
                decimal totalTerminalCommission = terminalAmount * 0.013m;

                // Загальна сума товарів (без Trade-in і GlobalDiscount) для розрахунку "ваги" кожного товару
                decimal grossItemsTotal = request.Items.Sum(i => (i.Price * i.Quantity) + i.WarrantyPrice);
                if (grossItemsTotal == 0) grossItemsTotal = 1; // Запобіжник від ділення на нуль


                // ==========================================
                // 2. ОБРОБКА ТОВАРІВ (ITEMS)
                // ==========================================
                foreach (var item in request.Items)
                {
                    decimal purchasePrice = 0; // Собівартість
                    decimal sellingPriceBase = item.Price; // Ціна продажу самого товару (без гарантії)
                    decimal warrantyPrice = item.WarrantyPrice; // Ціна гарантії
                    string savedModelName = item.ModelName; // Беремо назву прямо з кошика!
                    string? savedIdentifier = item.Identifier; // IMEI (для телефонів)

                    int itemId = 0;
                    string itemType = "";
                    // === ФОРМУВАННЯ ПОВНОЇ НАЗВИ ДЛЯ ЧЕКА ===
                    string fullModelName = item.ModelName;
                    int currentItemId = 0;
                    string currentItemType = item.Category;

                    // Якщо це телефон - додаємо пам'ять і колір
                    if (item.Category == "Device")
                    {
                        // Результат буде: "iPhone 13 128GB Midnight"
                        // Використовуємо string.Join щоб уникнути зайвих пробілів, якщо чогось немає
                        var parts = new List<string> { item.ModelName, item.Capacity, item.Color };
                        fullModelName = string.Join(" ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

                    decimal currentWarrantyProfit = originalWarrantyPrice;
                    decimal currentDeviceProfit = 0;

                    // --- А. ЛОГІКА ДЛЯ ПРИСТРОЇВ (Device) ---
                    if (item.Category == "Device" && item.DeviceStockId.HasValue)
                    {
                        var device = await _context.DeviceStock.FindAsync(item.DeviceStockId.Value);
                        if (device == null || device.StoreId != request.StoreId || device.IsSold == 1)
                            throw new Exception($"Пристрій {item.ModelName} вже продано");

                        if (device == null || device.StoreId != request.StoreId)
                            throw new Exception($"Пристрій {item.ModelName} вже продано або переміщено");

                        purchasePrice = device.PurchasePrice;
                        itemId = device.Id;
                        itemType = "Device";

                        device.IsSold = 1;
                        _context.Entry(device).State = EntityState.Modified;

                        currentDeviceProfit = (sellingPriceBase - purchasePriceUAH) * item.Quantity;

                        if (totalBankCommission > 0)
                        {
                            if (currentWarrantyProfit >= totalBankCommission)
                            {
                                currentWarrantyProfit -= totalBankCommission;
                                totalBankCommission = 0;
                            }
                    // --- Б. ЛОГІКА ДЛЯ АКСЕСУАРІВ (Accessory) ---
                            else
                            {
                        var accStock = await _context.AccessoryStock
                            .FirstOrDefaultAsync(a => a.ProductId == item.ProductId && a.StoreId == request.StoreId);

                        if (accStock == null || accStock.Quantity < item.Quantity)
                            throw new Exception($"Недостатньо {item.ModelName} на складі");

                        purchasePrice = accStock.PurchasePrice;
                        itemId = item.ProductId;
                        itemType = "Accessory";

                        // Списуємо кількість
                        accStock.Quantity -= item.Quantity;
                        currentDeviceProfit = (sellingPriceBase - purchasePriceUAH) * item.Quantity;
                    }
                    // --- В. ЛОГІКА ДЛЯ ПОСЛУГ ---
                    else if (currentItemType == "PosService")
                    {
                        purchasePriceUAH = 0;
                        purchasePriceUSD = 0;
                        currentItemId = item.ProductId;
                        fullModelName = $"[Послуга] {item.ModelName}";
                        currentDeviceProfit = (sellingPriceBase - purchasePriceUAH) * item.Quantity;
                    }
                    else if (currentItemType == "Service" && item.RepairTicketId.HasValue)
                    {
                        // 1. Завантажуємо тікет РАЗОМ із його списком виконаних робіт (Items)
                        var ticket = await _context.RepairTickets
                            .Include(t => t.Items)
                            .FirstOrDefaultAsync(t => t.Id == item.RepairTicketId.Value);

                    // --- РОЗРАХУНОК ПРИБУТКУ ---
                    decimal positionProfit = ((sellingPriceBase - purchasePrice) * item.Quantity) + warrantyPrice;

                    // Створюємо запис позиції в чеку
                    var saleItem = new SaleItem
                    {
                        SaleId = sale.Id,
                        ItemType = itemType,
                        ItemId = itemId,
                        ProductName = fullModelName,
                        Identifier = item.Identifier,
                        Quantity = item.Quantity,
                        SoldPurchasePrice = purchasePrice,
                        SoldSellingPrice = sellingPriceBase + warrantyPrice,
                        WarrantyPrice = warrantyPrice,
                        WarrantyId = item.WarrantyId,
                        PositionProfit = positionProfit
                    };

                    _context.SaleItems.Add(saleItem);

                    // Оновлюємо загальні цифри чека
                    sale.TotalAmount += (sellingPriceBase * item.Quantity) + warrantyPrice;
                    sale.TotalProfit += positionProfit;
                }

                // Початкова фінальна сума (до застосування знижок)
                sale.FinalAmount = sale.TotalAmount;

                // ==========================================
                // 2.5. ОБРОБКА TRADE-IN (НОВЕ!)
                // ==========================================
                if (request.TradeIns != null && request.TradeIns.Price > 0)
                    {
                    // А. Записуємо знижку в чек
                    sale.TradeInDiscount = request.TradeIns.Price;
                    sale.FinalAmount -= sale.TradeInDiscount;

                    bool imeiExists = await _context.DeviceStock.AnyAsync(d => d.Identifier == request.TradeIns.Imei);
                    if (imeiExists)
                        {
                        throw new Exception($"Помилка: Телефон з IMEI {request.TradeIns.Imei} вже є в базі!");
                        }

                    // Б. Створюємо запис в історії (TradeIns)
                        var tradeInRecord = new TradeIns
                        {
                            SaleId = sale.Id,
                        InboundModel = request.TradeIns.Model,
                        InboundIdentifier = request.TradeIns.Imei,
                        ConditionDescription = request.TradeIns.Condition,
                        AppraisedValue = request.TradeIns.Price,
                        DateCreated = DateTime.Now
                        };
                        _context.TradeIns.Add(tradeInRecord);
                    var tradeInModelName = request.TradeIns.Model.Trim(); // Видаляємо зайві пробіли

                    var product = await _context.Products
                        .FirstOrDefaultAsync(p => p.Name.ToLower() == tradeInModelName.ToLower());

                        var tradeInModelName = ti.Model.Trim();
                        var product = await _context.Products.FirstOrDefaultAsync(p => p.Name.ToLower() == tradeInModelName.ToLower());
                        int finalProductId;

                        if (product != null)
                        {
                        // Варіант А: Товар знайдено, беремо його ID
                            finalProductId = product.Id;
                        }
                        else
                        {
                        // Варіант Б: Такої моделі ще немає в базі Products.
                        // СТВОРЮЄМО ЇЇ АВТОМАТИЧНО, щоб не губити дані.
                        var newProduct = new Product
                        {
                            Name = tradeInModelName,
                            CategoryId = 1 // Тут можете вказати ID категорії "Телефони" або "Вживані", якщо у вас є категорії
                        };

                            _context.Products.Add(newProduct);
                        await _context.SaveChangesAsync(); // Зберігаємо, щоб отримати ID
                            finalProductId = newProduct.Id;
                        }
                    // В. АВТОМАТИЧНО ДОДАЄМО НА СКЛАД (DeviceStock)
                    var newDeviceStocks = new DeviceStock
                    {
                        ProductId = finalProductId, // Завжди ID 1, як ви й просили

                        StoreId = request.StoreId,

                        Identifier = request.TradeIns.Imei,

                        // Якщо у вашій базі є колонка ModelName в DeviceStock - розкоментуйте:
                        // ModelName = request.TradeIn.Model, 

                        // Нові поля з форми
                        Color = request.TradeIns.Color,
                        Capacity = request.TradeIns.Capacity,
                        BatteryHealth = request.TradeIns.BatteryHealth,

                        ConditionStatus = request.TradeIns.Condition,
                        PurchasePrice = request.TradeIns.Price,
                        SellingPrice = request.TradeIns.Price,
                            IsSold = 0,

                        DateAdded = DateTime.Now,
                        LastUpdated = DateTime.Now
                        };
                    _context.DeviceStock.Add(newDeviceStocks);
                }

                // ==========================================
                // 2.6. ЗАГАЛЬНА ЗНИЖКА НА ЧЕК
                // ==========================================
                if (request.GlobalDiscount > 0)
                {
                    sale.FinalAmount -= request.GlobalDiscount;
                    sale.TotalProfit -= request.GlobalDiscount;
                    sale.Comment += $" | Знижка на чек: {request.GlobalDiscount} грн";
                }

                if (sale.FinalAmount < 0) sale.FinalAmount = 0;

                // ==========================================
                // 3. ОБРОБКА ОПЛАТИ І КАСИ (PAYMENTS)
                // ==========================================

                var today = DateTime.Today;

                // Знаходимо або створюємо зміну
                var shift = await _context.CashShifts
                    .FirstOrDefaultAsync(s => s.StoreId == request.StoreId && s.ShiftDate == today);

                if (shift == null)
                {
                    shift = new CashShift { StoreId = request.StoreId, ShiftDate = today };
                    _context.CashShifts.Add(shift);
                    await _context.SaveChangesAsync();
                }

                var store = await _context.Stores.FindAsync(request.StoreId);
                decimal totalPaidInUah = 0; // Для перевірки на решту

                // Проходимо по всіх платежах клієнта
                foreach (var pay in request.Payments)
                {
                    if (pay.ExchangeRate <= 0) pay.ExchangeRate = 1;

                    // Рахуємо скільки це в гривнях для покриття чека
                    decimal amountInUah = pay.Amount * pay.ExchangeRate;
                    totalPaidInUah += amountInUah;

                    // Зараховуємо кошти у відповідний "гаманець"
                    switch (pay.Method)
                    {
                        case "USD":
                            shift.IncomeUSD += pay.Amount;
                            if (store != null) store.BalanceUSD += pay.Amount;
                            break;
                        case "EUR":
                            shift.IncomeEUR += pay.Amount;
                            if (store != null) store.BalanceEUR += pay.Amount;
                            break;
                        case "Оплата частинами":
                        case "Термінал":
                        case "Картковий переказ":
                        case "Картка":
                        case "Завдаток":
                            shift.IncomeCard += pay.Amount;
                            if (store != null) store.BalanceCard += pay.Amount;
                            break;
                        default: // Готівка UAH
                            shift.IncomeUAH += pay.Amount;
                            if (store != null) store.BalanceUAH += pay.Amount;
                            break;
                    }

                    // Історія транзакцій (Прихід)
                    var trans = new CashTransaction
                    {
                        StoreId = request.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Type = "Income",
                        Category = "Sale",
                        Amount = pay.Amount,
                        Currency = pay.Method,
                        Comment = (pay.Method == "USD" || pay.Method == "EUR")
                                  ? $"Продаж (Курс {pay.ExchangeRate})"
                                  : "Продаж товару",
                        Date = DateTime.Now
                    };
                    _context.CashTransactions.Add(trans);
                }

                // ==========================================
                // 4. ВИДАЧА РЕШТИ (CHANGE)
                // ==========================================

                // Якщо клієнт дав більше грошей (в перерахунку на грн), ніж сума чека
                // sale.FinalAmount вже зменшена на суму Trade-In!
                if (totalPaidInUah > sale.FinalAmount)
                {
                    decimal changeToReturn = totalPaidInUah - sale.FinalAmount;

                    // Списуємо решту з гривневої каси
                    shift.ExpenseUAH += changeToReturn;
                    if (store != null) store.BalanceUAH -= changeToReturn;

                    // Історія транзакцій (Витрата/Решта)
                    var changeTrans = new CashTransaction
                    {
                        StoreId = request.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Type = "Expense",
                        Category = "Other",
                        Amount = changeToReturn,
                        Currency = "UAH", // Решта завжди в гривні
                        Comment = "Видача решти клієнту",
                        Date = DateTime.Now
                    };
                    _context.CashTransactions.Add(changeTrans);
                }

                // ==========================================
                // 5. ФІНАЛІЗАЦІЯ
                // ==========================================

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await _hubContext.Clients.All.SendAsync("RefreshRepairNotifications");
                try
                {
                    var completeSale = await _context.Sales
                        .Include(s => s.SaleItems)
                            .ThenInclude(i => i.WarrantyRate) // Підтягуємо інфу про гарантію
                        .Include(s => s.Payments)
                        .FirstOrDefaultAsync(s => s.Id == sale.Id);

                    string fullSellerName = GetCurrentUserName();
                    string shortSellerName = fullSellerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullSellerName;
                    string storeName = storeInfo?.Name ?? "UnknownShop";

                    // ==========================================
                    // 🔥 ВІДПРАВКА В TELEGRAM (ОНОВЛЕНО)
                    // ==========================================
                    try
                    {
                        var soldDevices = completeSale?.SaleItems.Where(i => i.ItemType == "Device").ToList();

                        if (soldDevices != null && soldDevices.Any())
                        {
                            string tgMsg = $"📱 <b>Продаж техніки</b>\n" +
                                           $"🏪 Магазин: {storeName}\n" +
                                           $"👤 Продавець: {fullSellerName}\n" +
                                           $"💵 Курс: {currentExchangeRate}\n" +
                                           $"--------------------------\n";

                            foreach (var dev in soldDevices)
                            {
                                tgMsg += $"🔹 <b>{dev.ProductName}</b>\n" +
                                         $"💰 Ціна: <b>${dev.SellingPriceUSD}</b> ({dev.SoldSellingPrice.ToString("N0")} ₴) | Дохід: <b>${dev.ProfitUSD}</b>\n" +
                                         $"🆔 SN: <code>{dev.Identifier}</code>\n";

                                // Якщо до товару докупили гарантію, виводимо її окремим красивим рядком
                                if (dev.WarrantyPrice > 0)
                                {
                                    decimal warrantyProfitUSD = Math.Round(dev.WarrantyProfit / currentExchangeRate, 2);
                                    string warrantyName = dev.WarrantyRate != null ? $"{dev.WarrantyRate.TermMonths} міс. {dev.WarrantyRate.Type}" : "Розширена";

                                    tgMsg += $"🛡 <b>+ Гарантія:</b> {warrantyName}\n" +
                                             $"💵 Ціна: {dev.WarrantyPrice.ToString("N0")} ₴ | Дохід: <b>${warrantyProfitUSD}</b>\n";
                                }
                                tgMsg += "\n";
                            }

                            await _telegramService.SendMessageAsync(tgMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TG ERROR]: {ex.Message}");
                    }

                    // ==========================================
                    // ГЕНЕРАЦІЯ ТА ЗБЕРЕЖЕННЯ PDF
                    // ==========================================
                    var receiptDto = new SaleReceiptDto
                    {
                        Sale = completeSale ?? sale,
                        StoreName = storeName,
                        StoreAddress = storeInfo?.Address ?? "",
                        StorePhone = storeInfo?.Phone ?? "",
                        StoreInstagramUrl = storeInfo?.InstagramUrl ?? "",
                        SellerName = shortSellerName,
                        GlobalDiscount = request.GlobalDiscount
                    };

                    byte[] pdfBytes = _pdfService.GenerateReceiptPdf(receiptDto);
                    await _storageService.SaveReceiptAsync(pdfBytes, sale.Id, receiptDto.StoreName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[API POST-SALE ERROR] ❌ Помилка: {ex.Message}");
                }

                return Ok(sale.Id);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // Розгортаємо "матрьошку" помилок
                var errorMessage = ex.Message;
                var inner = ex.InnerException;
                while (inner != null)
                {
                    errorMessage += $" | INNER: {inner.Message}";
                    inner = inner.InnerException;
                }

                // Дивіться сюди в консолі Visual Studio!
                Console.WriteLine($"📛 CRITICAL DB ERROR: {errorMessage}");

                return BadRequest($"Помилка бази даних: {errorMessage}");
            }
        }

        // ==========================================
        // 🔥 МЕТОД ДЛЯ АВТО-ДРУКУ ЧЕКА
        // ==========================================
        [AllowAnonymous]
        [HttpGet("download-pdf/{id}")]
        public async Task<IActionResult> DownloadReceiptPdf(int id)
        {
            var sale = await _context.Sales
                .AsNoTracking()
                .Include(s => s.SaleItems)
                    .ThenInclude(i => i.WarrantyRate)
                .Include(s => s.Payments)
                .Include(s => s.CustomerWarranties)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null) return NotFound("Чек не знайдено");

            var storeInfo = await _context.Stores.FirstOrDefaultAsync(s => s.Id == sale.StoreId);
            var user = await _userManager.FindByIdAsync(sale.UserId!);
            string shortSellerName = user?.FullName?.Split(' ').FirstOrDefault() ?? user?.UserName ?? "Менеджер";

            decimal globalDiscount = 0;
            if (sale.Comment != null && sale.Comment.Contains("Знижка на чек:"))
            {
                var parts = sale.Comment.Split("Знижка на чек: ");
                if (parts.Length > 1) decimal.TryParse(parts[1].Replace(" грн", "").Trim(), out globalDiscount);
            }

            var dto = new SaleReceiptDto
            {
                Sale = sale,
                StoreName = storeInfo?.Name ?? "Apple Shopik",
                StoreAddress = storeInfo?.Address ?? "",
                StorePhone = storeInfo?.Phone ?? "",
                SellerName = shortSellerName,
                StoreInstagramUrl = storeInfo?.InstagramUrl ?? "",
                GlobalDiscount = globalDiscount
            };

            byte[] pdfBytes = _pdfService.GenerateReceiptPdf(dto);
            return File(pdfBytes, "application/pdf", $"Receipt_{id}.pdf");
        }

        [HttpGet("history")]
        public async Task<ActionResult<List<SaleReportDto>>> GetHistory([FromQuery] int storeId)
        {
            var sales = await _context.Sales
                .Include(s => s.SaleItems) // Тепер тут є ModelName!
                .Where(s => s.StoreId == storeId)
                .AsQueryable();

            if (start.HasValue)
            {
                query = query.Where(s => s.SaleDate >= start.Value.Date);
            }

            if (end.HasValue)
            {
                query = query.Where(s => s.SaleDate < end.Value.Date.AddDays(1));
            }

            if (!start.HasValue && !end.HasValue)
            {
                query = query.Take(50);
            }

            var sales = await query
                .OrderByDescending(s => s.SaleDate)
                .Take(50)
                .ToListAsync();

            var result = new List<SaleReportDto>();

            foreach (var s in sales)
            {
                // Шукаємо Trade-In...
                var tradeIn = await _context.TradeIns.FirstOrDefaultAsync(t => t.SaleId == s.Id);

                var dto = new SaleReportDto
                {
                    Id = s.Id,
                    Date = s.SaleDate,
                    Comment = s.Comment,
                    TotalAmount = s.TotalAmount,
                    TradeInDiscount = s.TradeInDiscount,
                    FinalAmount = s.FinalAmount,
                    TotalProfit = s.TotalProfit,

                    // ТЕПЕР ТУТ ВСЕ ПРОСТО І КРАСИВО:
                    Items = s.SaleItems.Select(i => new SaleItemDto
                    {
                        // Беремо збережену назву з бази!
                        ModelName = string.IsNullOrEmpty(i.Identifier)
                                    ? i.ProductName
                                    : $"{i.ProductName} (IMEI: {i.Identifier})",

                        Type = i.ItemType,
                        Quantity = i.Quantity,
                        SellingPrice = i.SoldSellingPrice,
                        WarrantyPrice = i.WarrantyPrice,
                        Profit = i.PositionProfit
                    }).ToList()
                };

                // ... (додавання TradeInDto без змін) ...
                if (tradeIn != null) { /*...*/ }

                result.Add(dto);
            }

            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SaleReceiptDto>> GetSale(int id)
        {
            // 1. Шукаємо чек з товарами та оплатами
            var sale = await _context.Sales
                .AsNoTracking()
                .Include(s => s.SaleItems)
                .ThenInclude(i => i.WarrantyRate)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null) return NotFound("Чек не знайдено");

            // 2. Шукаємо магазин
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == sale.StoreId);

            // 3. Шукаємо ім'я продавця
            var user = await _userManager.FindByIdAsync(sale.UserId);

            string sellerName = user?.FullName ?? user?.UserName ?? "Невідомий";

            // 4. Формуємо відповідь
            var dto = new SaleReceiptDto
            {
                Sale = sale,
                StoreName = store?.Name ?? "Apple Shopik",
                StoreAddress = store?.Address ?? "Адреса не вказана", // Переконайтеся, що в моделі Store є поле Address
                StorePhone = store?.Phone ?? "",                      // Переконайтеся, що в моделі Store є поле Phone
                StoreInstagramUrl = store?.InstagramUrl ?? "",        // Переконайтеся, що в моделі Store є поле InstagramUrl
                SellerName = sellerName
            };

            return dto;
        }
        [HttpPost("{id}/return")]
        public async Task<IActionResult> ReturnSale(int id)
        {
            // 1. Знаходимо чек з усіма його товарами
            var sale = await _context.Sales
                .Include(s => s.SaleItems)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null) return NotFound("Чек не знайдено");

            if (sale.Status == "Returned")
                return BadRequest("Цей продаж вже було скасовано раніше!");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 2. Змінюємо статус чека та обнуляємо фінансові показники
                sale.Status = "Returned";

                // 🔥 Обнуляємо чек, бо гроші ми не отримали і прибутку немає
                sale.TotalProfit = 0;
                sale.FinalAmount = 0;
                sale.TotalAmount = 0;

                // 3. ПОВЕРТАЄМО ТОВАРИ НА СКЛАД ТА ОБНУЛЯЄМО ЇХ ДОХІД
                foreach (var item in sale.SaleItems)
                {
                    // 🔥 Обнуляємо дохід по кожній окремій позиції, щоб не збити звіти!
                    item.PositionProfit = 0;
                    item.Quantity = 0;
                    item.ProfitUSD = 0;
                    item.WarrantyProfit = 0;

                    if (item.ItemType == "Device" && item.ItemId > 0)
                    {
                        // Повертаємо телефон: ставимо IsSold = 0 (доступний до продажу)
                        var device = await _context.DeviceStock.FindAsync(item.ItemId);
                        if (device != null)
                        {
                            device.IsSold = 0;
                        }
                    }
                    else if (item.ItemType == "Accessory")
                    {
                        // Повертаємо аксесуари: плюсуємо кількість назад на склад
                        var accessory = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == item.ItemId);
                        if (accessory != null)
                        {
                            accessory.Quantity += item.Quantity;
                        }
                    }
                    // Послуги (PosService/Service) не повертаємо на склад, бо це нематеріальна річ, 
                    // але дохід по них ми вже обнулили вище, тож у звіти вони не підуть.
                }

                // 4. СКАСОВУЄМО ГАРАНТІЇ
                var warranties = await _context.CustomerWarranties.Where(w => w.SaleId == sale.Id).ToListAsync();
                foreach (var w in warranties)
                {
                    // Видаляємо гарантію з бази клієнтів
                    _context.CustomerWarranties.Remove(w);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok("Продаж успішно скасовано. Товари повернуто на склад, а доходи обнулено.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Помилка повернення: {ex.Message}");
            }
        }
        [HttpGet("shipments")]
        public async Task<ActionResult<List<PostalShipmentDto>>> GetPostalShipments([FromQuery] string tab = "active", [FromQuery] int storeId = 0)
        {
            // 1. Отримуємо дані користувача з токена
            bool isAdmin = User.IsInRole("Admin");
            var userStoreClaim = User.FindFirst("StoreId")?.Value;
            int userStoreId = 0;
            if (userStoreClaim != null) int.TryParse(userStoreClaim, out userStoreId);

            // 2. Базовий запит (тільки посилки)
            var query = _context.Sales
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.SaleItems)
                    .ThenInclude(i => i.WarrantyRate)
                .Where(s => s.Status == "Shipped" || s.Status == "Returned" || (s.TrackingNumber != null && s.TrackingNumber != ""));

            // 🔥 3. ФІЛЬТРАЦІЯ ПО МАГАЗИНУ
            if (!isAdmin)
            {
                // Якщо не адмін - жорстко обмежуємо його власним магазином
                query = query.Where(s => s.StoreId == userStoreId);
            }
            else if (storeId > 0)
            {
                // Якщо адмін вибрав конкретний магазин з випадаючого списку
                query = query.Where(s => s.StoreId == storeId);
            }
            // Якщо адмін і storeId == 0 -> показуємо абсолютно всі посилки

            // 4. Фільтрація по вкладках (В дорозі / Забрали / Відмови)
            if (tab == "active")
                query = query.Where(s => s.Status == "Shipped");
            else if (tab == "completed")
                query = query.Where(s => s.Status == "Completed");
            else if (tab == "returned")
                query = query.Where(s => s.Status == "Returned");

            var shipments = await query
                .OrderByDescending(s => s.SaleDate)
                .Select(s => new PostalShipmentDto
                {
                    Id = s.Id,
                    SaleDate = s.SaleDate,
                    FinalAmount = s.FinalAmount,
                    CustomerName = s.Customer != null ? s.Customer.Name : "Невідомий клієнт",
                    TrackingNumber = s.TrackingNumber,
                    Status = s.Status,
                    Items = s.SaleItems.Select(i =>
                        $"{i.Quantity}x {i.ProductName}" +
                        (i.WarrantyPrice > 0
                            ? $" (🛡 Гарантія: {(i.WarrantyRate != null ? $"{i.WarrantyRate.TermMonths} міс. {i.WarrantyRate.Type}" : "Стандартна")})"
                            : "")
                    ).ToList()
                })
                .ToListAsync();

            return Ok(shipments);
        }

        [HttpPost("{id}/confirm-delivery")]
        public async Task<IActionResult> ConfirmDelivery(int id)
        {
            var sale = await _context.Sales.FindAsync(id);
            if (sale == null) return NotFound("Чек не знайдено");

            sale.Status = "Completed"; // Клієнт забрав посилку

            await _context.SaveChangesAsync();
            return Ok("Доставку успішно підтверджено.");
        }
        public class UpdateTrackingRequest
        {
            public string TrackingNumber { get; set; } = string.Empty;
        }

        [HttpPost("{id}/update-tracking")]
        public async Task<IActionResult> UpdateTracking(int id, [FromBody] UpdateTrackingRequest request)
        {
            var sale = await _context.Sales.FindAsync(id);
            if (sale == null) return NotFound("Чек не знайдено");

            // Оновлюємо ТТН
            sale.TrackingNumber = request.TrackingNumber;

            await _context.SaveChangesAsync();
            return Ok("ТТН успішно оновлено.");
        }

        [HttpPost("generate-receipt/{id}")]
        public async Task<ActionResult> GenerateReceipt(int id)
        {
            // 1. Отримуємо дані чеку (використовуємо логіку з GetSale)
                var sale = await _context.Sales
                    .Include(s => s.SaleItems)
                    .ThenInclude(i => i.WarrantyRate)
                    .Include(s => s.Payments)
                    .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null) return NotFound();

                var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == sale.StoreId);
            var user = await _userManager.FindByIdAsync(sale.UserId);
            string sellerName = user?.FullName ?? user?.UserName ?? "Невідомий";
                string storeName = store?.Name ?? "UnknownShop";

                decimal globalDiscount = 0;
                if (sale.Comment != null && sale.Comment.Contains("Знижка на чек:"))
                {
                    var parts = sale.Comment.Split("Знижка на чек: ");
                    if (parts.Length > 1)
                    {
                        var discountStr = parts[1].Replace(" грн", "").Trim();
                        decimal.TryParse(discountStr, out globalDiscount);
                    }
                }

                var dto = new SaleReceiptDto
                {
                    Sale = sale,
                    StoreName = storeName,
                    StoreAddress = store?.Address ?? "",
                    StorePhone = store?.Phone ?? "",
                SellerName = sellerName
                };

            // 2. Генеруємо PDF (байти)
                byte[] pdfBytes = _pdfService.GenerateReceiptPdf(dto);

            // 3. Зберігаємо (Локально + FTP)
            // Тут ми передаємо ID замовлення і назву магазину для імені файлу
                await _storageService.SaveReceiptAsync(pdfBytes, sale.Id, storeName);

            // ТУТ МОЖНА ДОДАТИ ЛОГІКУ TELEGRAM
            // await _telegramService.SendReceipt(pdfBytes, ...);

            return Ok("Чек згенеровано та збережено");
        }
    }
}