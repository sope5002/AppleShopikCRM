using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// Переконайтеся, що ваші namespace правильні

namespace AppleShopikCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SalesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfService _pdfService;
        private readonly StorageService _storageService;

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
        }
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
        {
            if (request.Items == null || !request.Items.Any()) return BadRequest("Кошик порожній");

            using var transaction = _context.Database.BeginTransaction();
            try
            {
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

                    // Якщо це телефон - додаємо пам'ять і колір
                    if (item.Category == "Device")
                    {
                        // Результат буде: "iPhone 13 128GB Midnight"
                        // Використовуємо string.Join щоб уникнути зайвих пробілів, якщо чогось немає
                        var parts = new List<string> { item.ModelName, item.Capacity, item.Color };
                        fullModelName = string.Join(" ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
                    }

                    // --- А. ЛОГІКА ДЛЯ ПРИСТРОЇВ (Device) ---
                    if (item.Category == "Device" && item.DeviceStockId.HasValue)
                    {
                        var device = await _context.DeviceStock.FindAsync(item.DeviceStockId.Value);

                        if (device == null || device.StoreId != request.StoreId)
                            throw new Exception($"Пристрій {item.ModelName} вже продано або переміщено");

                        purchasePrice = device.PurchasePrice;
                        itemId = device.Id;
                        itemType = "Device";

                        device.IsSold = 1; 
                        _context.Entry(device).State = EntityState.Modified;
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
                    }

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
                        case "Термінал":
                        case "Картковий переказ":
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
        [HttpGet("history")]
        public async Task<ActionResult<List<SaleReportDto>>> GetHistory([FromQuery] int storeId)
        {
            var sales = await _context.Sales
                .Include(s => s.SaleItems) // Тепер тут є ModelName!
                .Where(s => s.StoreId == storeId)
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
                .Include(s => s.SaleItems)
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
        [HttpPost("generate-receipt/{id}")]
        public async Task<ActionResult> GenerateReceipt(int id)
        {
            // 1. Отримуємо дані чеку (використовуємо логіку з GetSale)
            var sale = await _context.Sales
                .Include(s => s.SaleItems)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null) return NotFound();

            var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == sale.StoreId);
            var user = await _userManager.FindByIdAsync(sale.UserId);
            string sellerName = user?.FullName ?? user?.UserName ?? "Невідомий";
            string storeName = store?.Name ?? "UnknownShop";

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