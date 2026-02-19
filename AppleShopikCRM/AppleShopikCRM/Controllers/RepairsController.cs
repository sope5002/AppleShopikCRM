using AppleShopikCRM; // Перевірте ваш namespace
using AppleShopikCRM.Client.Models; // Перевірте namespace моделей
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppleShopikCRM.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RepairsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PdfService _pdfService;
        private readonly StorageService _storageService;
        private readonly UserManager<ApplicationUser> _userManager;

        public RepairsController(
            ApplicationDbContext context,
            PdfService pdfService,          // <--- Нове
            StorageService storageService,  // <--- Нове
            UserManager<ApplicationUser> userManager) // <--- Нове
        {
            _context = context;
            _pdfService = pdfService;
            _storageService = storageService;
            _userManager = userManager;
        }

        // 1. ОТРИМАТИ ВСІ ЗАЯВКИ
        [HttpGet]
        public async Task<ActionResult<List<RepairTicket>>> GetTickets()
        {
            return await _context.RepairTickets
                .Include(t => t.DeviceStock) // Підтягуємо інфо про телефон
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        // 2. СТВОРИТИ НОВУ
        [HttpPost]
        public async Task<ActionResult> CreateTicket(RepairTicket ticket)
        {
            ticket.CreatedAt = DateTime.Now;
            ticket.Status = RepairStatus.New;

            // Якщо це внутрішній ремонт - блокуємо телефон
            if (ticket.Type == RepairType.InternalStock && ticket.DeviceStockId.HasValue)
            {
                var device = await _context.DeviceStock
                    .Include(d => d.Product)
                    .FirstOrDefaultAsync(d => d.Id == ticket.DeviceStockId);

                if (device != null)
                {
                    device.IsSold = 2; // 2 = В сервісі
                    ticket.DeviceModel = device.ModelName ?? device.Product?.Name ?? "Unknown";
                    ticket.SerialNumber = device.Identifier;
                }
            }

            _context.RepairTickets.Add(ticket);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // 3. 👇 ОСЬ ЦЬОГО МЕТОДУ НЕ ВИСТАЧАЛО ДЛЯ ПЕРЕМІЩЕННЯ КАРТОК
        [HttpPut("status")]
        public async Task<ActionResult> UpdateStatus([FromBody] UpdateStatusDto request)
        {
            var dbTicket = await _context.RepairTickets.FindAsync(request.Id);
            if (dbTicket == null) return NotFound("Заявку не знайдено");

            // 1. Оновлюємо основні поля
            dbTicket.Status = request.Status;
            dbTicket.ResolutionReport = request.ResolutionReport;
            dbTicket.CostPart = request.CostPart;
            dbTicket.Price = request.Price;

            // 2. 🔥 ПРАВИЛЬНЕ ПРИЗНАЧЕННЯ МАЙСТРА (З серверного контексту)
            // Якщо статус став "В роботі" - записуємо поточного користувача як майстра
            if (request.Status == RepairStatus.InProgress)
            {
                // Дістаємо ID користувача, який викликав цей метод (з його Токена)
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                             ?? User.FindFirst("sub")?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    dbTicket.TechnicianId = userId; // Записуємо реальний ID
                }
                else
                {
                    // Якщо ID не знайдено - залишаємо null (але не пишемо "Unknown"!)
                    // Це врятує від помилки 500
                    Console.WriteLine("⚠️ Увага: Не вдалося визначити ID майстра з токена.");
                }
            }

            // 3. Зберігаємо
            try
            {
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DB UPDATE ERROR: {ex.Message}");
                // Повертаємо помилку, щоб клієнт бачив, що сталося
                return StatusCode(500, $"Помилка БД: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // 4. 👇 ОСЬ ЦЬОГО НЕ ВИСТАЧАЛО ДЛЯ РЕДАГУВАННЯ ХАРАКТЕРИСТИК
        [HttpPut("update-device-specs")]
        public async Task<ActionResult> UpdateDeviceSpecs([FromBody] DeviceStock specs)
        {
            var device = await _context.DeviceStock.FindAsync(specs.Id);
            if (device == null) return NotFound();

            device.BatteryHealth = specs.BatteryHealth;
            device.ConditionStatus = specs.ConditionStatus;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("complete-payment")]
        public async Task<ActionResult> CompletePayment([FromBody] RepairPaymentRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Знаходимо ремонт
                var ticket = await _context.RepairTickets
                    .Include(t => t.DeviceStock)
                    .FirstOrDefaultAsync(t => t.Id == request.TicketId);

                if (ticket == null) return NotFound("Ремонт не знайдено");
                if (ticket.Status == RepairStatus.Completed) return BadRequest("Вже оплачено!");

                // 2. Отримуємо ID касира
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";

                // 3. СТВОРЮЄМО ПРОДАЖ (SALE)
                var sale = new Sale
                {
                    StoreId = ticket.StoreId,
                    SaleDate = DateTime.Now,
                    UserId = userId,
                    RepairTicketId = ticket.Id, // 🔗 ЗВ'ЯЗОК З РЕМОНТОМ

                    // Фінанси
                    TotalAmount = ticket.Price,
                    TradeInDiscount = 0,
                    FinalAmount = ticket.Price,
                    TotalProfit = ticket.Price - ticket.CostPart, // Чистий прибуток

                    Comment = $"Ремонт #{ticket.TicketNumber}: {ticket.DeviceModel}",

                    SaleItems = new List<SaleItem>(),
                    Payments = new List<SalePayment>()
                };

                // 4. СТВОРЮЄМО ПОЗИЦІЮ ЧЕКУ (SALE ITEM)
                var saleItem = new SaleItem
                {
                    ItemType = "Service", // 👈 Важливе поле для розрізнення типів
                    ItemId = ticket.Id,  // ID тікета як ідентифікатор товару

                    // Опис
                    ProductName = ticket.DeviceStock != null
                        ? $"{ticket.DeviceStock.ModelName} (Ремонт)"
                        : $"{ticket.DeviceModel} (Ремонт)",
                    Identifier = ticket.TicketNumber, // Номер тікета як серійник

                    Quantity = 1,

                    // Фінансова математика
                    SoldPurchasePrice = ticket.CostPart, // Собівартість
                    SoldSellingPrice = ticket.Price,     // Ціна продажу
                    PositionProfit = ticket.Price - ticket.CostPart, // Прибуток

                    WarrantyPrice = 0, // На ремонти гарантію окремо не продаємо (зазвичай)
                };
                sale.SaleItems.Add(saleItem);

                // 5. ОБРОБКА ПЛАТЕЖІВ + КАСОВА ЗМІНА
                var today = DateTime.Today;
                var shift = await _context.CashShifts
                    .FirstOrDefaultAsync(s => s.StoreId == ticket.StoreId && s.ShiftDate == today);

                if (shift == null)
                {
                    shift = new CashShift { StoreId = ticket.StoreId, ShiftDate = today };
                    _context.CashShifts.Add(shift);
                    await _context.SaveChangesAsync();
                }

                foreach (var pay in request.Payments)
                {
                    // Додаємо оплату в чек
                    sale.Payments.Add(new SalePayment
                    {
                        Method = pay.Method,
                        Amount = pay.Amount,
                        ExchangeRate = pay.ExchangeRate
                    });

                    // 💰 ОНОВЛЮЄМО КАСУ (CashShift)
                    // (Логіка ідентична до звичайного продажу)
                    decimal amountInUah = pay.Amount * (pay.ExchangeRate > 0 ? pay.ExchangeRate : 1);

                    switch (pay.Method)
                    {
                        case "USD": shift.IncomeUSD += pay.Amount; break;
                        case "EUR": shift.IncomeEUR += pay.Amount; break;
                        case "Картка":
                        case "Термінал": shift.IncomeCard += pay.Amount; break;
                        default: shift.IncomeUAH += pay.Amount; break; // Готівка
                    }

                    // Додаємо транзакцію в історію каси
                    _context.CashTransactions.Add(new CashTransaction
                    {
                        StoreId = ticket.StoreId,
                        ShiftId = shift.Id,
                        //SaleId = sale.Id,
                        Type = "Income",
                        Category = "'Service",
                        Amount = pay.Amount,
                        Currency = pay.Method,
                        Date = DateTime.Now,
                        Comment = $"Ремонт ({ticket.DeviceModel})"
                    });
                }

                // 6. ЗАКРИВАЄМО РЕМОНТ
                ticket.Status = RepairStatus.Completed;
                ticket.CompletedAt = DateTime.Now;

                // Додаємо чек в базу
                _context.Sales.Add(sale);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Повертаємо ID продажу для друку чеку
                return Ok(new { SaleId = sale.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Помилка проведення оплати: {ex.Message}");
            }
        }


        // 6. ПОВЕРНЕННЯ НА СКЛАД (ВНУТРІШНІЙ РЕМОНТ)
        [HttpPost("confirm-return/{id}")]
        public async Task<ActionResult> ConfirmInternalReturn(int id)
        {
            var ticket = await _context.RepairTickets.FindAsync(id);
            if (ticket == null) return NotFound();

            ticket.Status = RepairStatus.Completed;
            ticket.CompletedAt = DateTime.Now;

            // Розблоковуємо телефон
            if (ticket.DeviceStockId.HasValue)
            {
                var device = await _context.DeviceStock.FindAsync(ticket.DeviceStockId);
                if (device != null)
                {
                    device.IsSold = 0; // Повертаємо у продаж
                    device.PurchasePrice += ticket.CostPart; // Збільшуємо собівартість на ціну запчастин
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
        [HttpGet("services")]
        public async Task<ActionResult<List<RepairService>>> GetServices([FromQuery] string? modelName)
        {
            var query = _context.RepairServices.AsQueryable();

            if (string.IsNullOrEmpty(modelName))
            {
                // Якщо нічого не передали - тільки загальні послуги
                query = query.Where(s => s.CompatibleModel == null);
            }
            else
            {
                // 🔥 РОЗУМНИЙ ПОШУК 🔥
                // Логіка: "Якщо в назві, яку ввів менеджер (modelName), є шматок назви з бази"
                // Наприклад: manager="iPhone 11 Pro Max 256", db="iPhone 11 Pro Max" -> MATCH

                // 1. Спочатку завантажимо всі послуги, де модель не NULL або NULL
                // (EF Core іноді важко переварити Contains навпаки, тому робимо це в пам'яті або спрощено)

                var allServices = await _context.RepairServices.ToListAsync();

                var filteredServices = allServices.Where(s =>
                    s.CompatibleModel == null || // Спільні послуги
                    modelName.Contains(s.CompatibleModel, StringComparison.OrdinalIgnoreCase) // Чи міститься "iPhone 11" всередині "iPhone 11 Black"
                ).ToList();

                // 🛡️ Ховаємо собівартість для менеджерів
                if (!User.IsInRole("Admin"))
                {
                    foreach (var s in filteredServices) s.CostPrice = 0;
                }

                return filteredServices;
            }

            return new List<RepairService>(); // Fallback
        }

        // 👇 2. НОВИЙ МЕТОД: ФІНАНСОВИЙ ЗВІТ (Тільки для Адміна)
        [HttpGet("stats")]
        [Authorize(Roles = "Admin")] // Тільки Адмін може бачити гроші
        public async Task<ActionResult<FinancialStatsDto>> GetStats([FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            // Якщо дати не передали - беремо поточний місяць
            var startDate = start ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = end ?? DateTime.Now;

            var completedRepairs = await _context.RepairTickets
                .Where(t => t.Status == RepairStatus.Completed && t.CompletedAt >= startDate && t.CompletedAt <= endDate)
                .ToListAsync();

            var stats = new FinancialStatsDto
            {
                TotalRepairsCount = completedRepairs.Count,
                TotalRevenue = completedRepairs.Sum(t => t.Price), // Гроші в касі
                TotalCost = completedRepairs.Sum(t => t.CostPart), // Витрати на запчастини
                                                                   // Чистий прибуток = Каса - Витрати
                NetProfit = completedRepairs.Sum(t => t.Price - t.CostPart)
            };

            return stats;
        }
    }

    // DTO для статистики
    public class FinancialStatsDto
    {
        public int TotalRepairsCount { get; set; }
        public decimal TotalRevenue { get; set; } // Оборот
        public decimal TotalCost { get; set; }    // Собівартість
        public decimal NetProfit { get; set; }    // Прибуток
    }
    public class UpdateStatusDto
    {
        public int Id { get; set; }
        public RepairStatus Status { get; set; }
        public string? ResolutionReport { get; set; }
        public decimal CostPart { get; set; }
        public decimal Price { get; set; }
        public string? TechnicianId { get; set; }
    }
}

    
