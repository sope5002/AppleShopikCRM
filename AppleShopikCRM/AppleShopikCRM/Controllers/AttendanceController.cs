using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services; // Ваш сервіс Telegram
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace AppleShopikCRM.Server.Controllers
{
    
    // Спеціальний клас для передачі даних
    public class AttendanceRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegramService;
        private readonly IServiceScopeFactory _scopeFactory;

        public AttendanceController(
            ApplicationDbContext context,
            TelegramService telegramService,
            IServiceScopeFactory scopeFactory) // 🔥 ДОДАЛИ СЮДИ
        {
            _context = context;
            _telegramService = telegramService;
            _scopeFactory = scopeFactory;
        }

        [HttpGet("status/{storeId}")]
        public async Task<IActionResult> GetStatus(int storeId, [FromQuery] string userId)
        {
            var today = TimeHelper.GetUkraineTime().Date;
            var attendance = await _context.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.StoreId == storeId && a.WorkDate == today);

            return Ok(attendance);
        }

        [HttpPost("clock-in/{storeId}")]
        public async Task<IActionResult> ClockIn(int storeId, [FromBody] AttendanceRequest request)
        {
            var today = TimeHelper.GetUkraineTime().Date;
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Магазин не знайдено");

            // ==========================================
            // 🔥 1. БЛОК ПЕРЕВІРКИ ТА ОТРИМАННЯ ІМЕНІ
            // ==========================================
            // Намагаємось взяти дані з запиту, якщо там пусто - беремо з токена сесії
            string userId = !string.IsNullOrWhiteSpace(request?.UserId)
                ? request.UserId
                : User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

            string userName = !string.IsNullOrWhiteSpace(request?.UserName)
                ? request.UserName
                : User.FindFirst(System.Security.Claims.ClaimTypes.UserData)?.Value ?? User.Identity?.Name ?? "";

            // Якщо ми так і не змогли знайти ім'я чи ID - ВІДХИЛЯЄМО ЗАПИТ, щоб не плодити пусті рядки в БД
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(userName))
            {
                return BadRequest("Помилка: не вдалося розпізнати працівника. Спробуйте оновити сторінку або зайти в систему наново.");
            }
            // ==========================================

            // Далі шукаємо запис вже за перевіреним userId
            var attendance = await _context.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.StoreId == storeId && a.WorkDate == today);

            if (attendance == null)
            {
                attendance = new EmployeeAttendance
                {
                    UserId = userId,     // Записуємо 100% не пустий ID
                    UserName = userName, // Записуємо 100% не пусте ім'я
                    StoreId = storeId,
                    WorkDate = today,
                    ClockInTime = TimeHelper.GetUkraineTime()
                };
                _context.EmployeeAttendances.Add(attendance);

                // ==========================================
                // 2. АВТОМАТИЧНЕ ВІДКРИТТЯ КАСИ
                // ==========================================
                var cashShift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == storeId && s.ShiftDate == today);

                if (cashShift == null)
                {
                    // Якщо каси за сьогодні ще взагалі не було — створюємо нову
                    cashShift = new CashShift
                    {
                        StoreId = storeId,
                        ShiftDate = today,
                        IsClosed = 0 // Явно вказуємо, що вона відкрита
                    };
                    _context.CashShifts.Add(cashShift);
                }
                else if (cashShift.IsClosed == 1)
                {
                    // Якщо хтось із напарників закрив касу раніше, 
                    // а цей менеджер тільки-но починає день — перевідкриваємо її!
                    cashShift.IsClosed = 0;
                }
                await _context.SaveChangesAsync();

                // 📩 ВІДПРАВКА В TELEGRAM (Використовуємо перевірене userName)
                string time = TimeHelper.GetUkraineTime().ToString("dd.MM.yyyy HH:mm");
                string msg = $"✅ *Відкрито зміну в магазині {store.Name}*\n👤 {userName} 📆 {time}";

                await _telegramService.SendMessageAsync(msg);
                _ = Task.Run(() => RunInventoryCheckAsync(store.Id, store.Name));
                await _telegramService.SendRateQuestionAsync(store.Id, store.ExchangeRate);
            }

            return Ok(attendance);
        }

        // 1. Створюємо окремий клас для закриття зміни з новим параметром
        public class ClockOutRequest
        {
            public string UserId { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public bool CloseCashShift { get; set; } // 🔥 Додано: чи закривати касу?
        }

        // 2. Оновлений метод ClockOut
        [HttpPost("clock-out/{storeId}")]
        public async Task<IActionResult> ClockOut(int storeId, [FromBody] ClockOutRequest request)
        {
            var now = TimeHelper.GetUkraineTime();
            var today = now.Date;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            // 1. ЗАКРИВАЄМО ОСОБИСТУ РОБОЧУ ЗМІНУ МЕНЕДЖЕРА
            var attendance = await _context.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == request.UserId && a.StoreId == storeId && a.WorkDate == today);

            if (attendance != null && attendance.ClockOutTime == null)
            {
                attendance.ClockOutTime = now;
            }

            // 2. ЗАКРИВАЄМО КАСОВУ ЗМІНУ
            var cashShift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == storeId && s.ShiftDate == today);
            bool cashShiftActuallyClosed = false;

            if (cashShift != null && request.CloseCashShift)
            {
                cashShift.IsClosed = 1;
                cashShiftActuallyClosed = true;
            }

            await _context.SaveChangesAsync();

            // ==========================================
            // 📊 ЗБІР ДАНИХ ДЛЯ ТЕЛЕГРАМУ
            // ==========================================
            var monthSales = await _context.Sales.Include(s => s.SaleItems).Where(s => s.StoreId == storeId && s.SaleDate >= startOfMonth).ToListAsync();
            var todaySales = monthSales.Where(s => s.SaleDate.Date == today).ToList();

            var itemsToday = todaySales.SelectMany(s => s.SaleItems).ToList();
            var itemsMonth = monthSales.SelectMany(s => s.SaleItems).ToList();

            // 🔥 РОЗДІЛЕНО: Групуємо товари та послуги
            var accToday = itemsToday.Where(i => i.ItemType == "Accessory").ToList();
            var accMonth = itemsMonth.Where(i => i.ItemType == "Accessory").ToList();

            var devToday = itemsToday.Where(i => i.ItemType == "Device").ToList();
            var devMonth = itemsMonth.Where(i => i.ItemType == "Device").ToList();

            // Тільки повноцінні ремонти (з центру ремонтів)
            var repairToday = itemsToday.Where(i => i.ItemType == "Service").ToList();
            var repairMonth = itemsMonth.Where(i => i.ItemType == "Service").ToList();

            // Тільки швидкі послуги (з каси: поклеїти скло, створити Apple ID тощо)
            var serviceToday = itemsToday.Where(i => i.ItemType == "PosService").ToList();
            var serviceMonth = itemsMonth.Where(i => i.ItemType == "PosService").ToList();

            var warToday = itemsToday.Where(i => i.WarrantyPrice > 0).ToList();
            var warMonth = itemsMonth.Where(i => i.WarrantyPrice > 0).ToList();

            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            int currentDay = now.Day;

            // 🔥 Витрати у нас в доларах (згідно ваших слів)
            decimal dailyExpenseUSD = store.MonthlyExpensesUAH / daysInMonth;
            decimal accumulatedExpenseUSD = dailyExpenseUSD * currentDay;

            // ==========================================
            // БАЗОВІ ФОРМУЛИ (Для гривневих товарів)
            // ==========================================
            decimal GetRevUAH(List<SaleItem> list) => list.Sum(x => (x.SoldSellingPrice * x.Quantity) - x.WarrantyPrice);
            decimal GetProfUAH(List<SaleItem> list) => list.Sum(x => x.PositionProfit - x.WarrantyProfit);
            decimal GetWarRevUAH(List<SaleItem> list) => list.Sum(x => x.WarrantyPrice);
            decimal GetWarProfUAH(List<SaleItem> list) => list.Sum(x => x.WarrantyProfit);

            // 🔥 БАЗОВІ ФОРМУЛИ (Для доларових товарів - Техніки та загального підсумку)
            decimal GetRevUSD(List<SaleItem> list) => list.Sum(x => x.SellingPriceUSD * x.Quantity);
            decimal GetProfUSD(List<SaleItem> list) => list.Sum(x => x.ProfitUSD);

            // --- РОЗРАХУНОК ЗА СЬОГОДНІ ---
            decimal accTodayRev = GetRevUAH(accToday); decimal accTodayProf = GetProfUAH(accToday);

            // 🔥 iPhone тепер в доларах
            decimal devTodayRevUSD = GetRevUSD(devToday); decimal devTodayProfUSD = GetProfUSD(devToday);

            decimal repairTodayRev = GetRevUAH(repairToday); decimal repairTodayProf = GetProfUAH(repairToday);
            decimal serviceTodayRev = GetRevUAH(serviceToday); decimal serviceTodayProf = GetProfUAH(serviceToday);
            decimal warTodayRev = GetWarRevUAH(warToday); decimal warTodayProf = GetWarProfUAH(warToday);

            // --- РОЗРАХУНОК ЗА МІСЯЦЬ ---
            decimal accMonthRev = GetRevUAH(accMonth); decimal accMonthProf = GetProfUAH(accMonth);

            // 🔥 iPhone тепер в доларах
            decimal devMonthRevUSD = GetRevUSD(devMonth); decimal devMonthProfUSD = GetProfUSD(devMonth);

            decimal repairMonthRev = GetRevUAH(repairMonth); decimal repairMonthProf = GetProfUAH(repairMonth);
            decimal serviceMonthRev = GetRevUAH(serviceMonth); decimal serviceMonthProf = GetProfUAH(serviceMonth);
            decimal warMonthRev = GetWarRevUAH(warMonth); decimal warMonthProf = GetWarProfUAH(warMonth);

            // 🔥 Підрахунок Net Profit (Чистий прибуток мінус витрати)
            // Тепер ми просто беремо загальний дохід ВСІХ товарів у $ (бо ми його зберігаємо в БД для кожної позиції)
            decimal todayTotalProfitUSD = itemsToday.Sum(i => i.ProfitUSD);
            decimal monthTotalProfitUSD = itemsMonth.Sum(i => i.ProfitUSD);

            // Віднімаємо витрати
            decimal netProfitTodayUSD = Math.Round(todayTotalProfitUSD - dailyExpenseUSD, 0);
            decimal netProfitMonthUSD = Math.Round(monthTotalProfitUSD - accumulatedExpenseUSD, 0);

            // Дані про КАСУ
            string cashMsg = "";
            if (cashShift != null)
            {
                string statusText = cashShiftActuallyClosed ? "🔴 КАСА ЗАКРИТА" : "🟢 Каса ще працює";
                cashMsg = $"\n\n💵 <b>РУХ КАСИ ЗА ДЕНЬ: ({statusText})</b>\n" +
                          $"• Прихід: <code>{cashShift.IncomeUAH} ₴</code> | <code>{cashShift.IncomeUSD} $</code> | <code>{cashShift.IncomeEUR} €</code>\n" +
                          $"• Витрати: <code>{cashShift.ExpenseUAH} ₴</code> | <code>{cashShift.ExpenseUSD} $</code> | <code>{cashShift.ExpenseEUR} €</code> \n\n" +
                          $"🏦 <b>ЗАЛИШОК В КАСІ:</b>\n" +
                          $"• Готівка: <code>{store.BalanceUAH} ₴</code> | <code>{store.BalanceUSD} $</code> | <code>{store.BalanceEUR} €</code>\n" +
                          $"• Термінал: <code>{store.BalanceCard} ₴</code>";
            }

            string timeStr = now.ToString("dd.MM.yyyy HH:mm");
            string startDateStr = startOfMonth.ToString("dd.MM.yyyy");

            // 🔥 ФОРМУВАННЯ ПОВІДОМЛЕННЯ
            string msg = $@"✅ <b>Закрито зміну в <code>{store.Name}</code></b>
👤 {request.UserName}
📆 {timeStr}

📅 <b>За сьогодні:</b>
• Аксесуари: <code>{accTodayRev} грн</code> (поз.: {accToday.Sum(x => x.Quantity)})
• Дохід аксесуарів: <code>{accTodayProf} грн</code>
• iPhone: <code>{devTodayRevUSD} $</code> (поз.: {devToday.Sum(x => x.Quantity)})
• Дохід iPhone: <code>{devTodayProfUSD} $</code>
• Ремонти: <code>{repairTodayRev} грн</code> (поз.: {repairToday.Sum(x => x.Quantity)})
• Дохід ремонтів: <code>{repairTodayProf} грн</code>
• Швидкі послуги: <code>{serviceTodayRev} грн</code> (поз.: {serviceToday.Sum(x => x.Quantity)})
• Дохід послуг: <code>{serviceTodayProf} грн</code>
• Гарантії (Виручка): <code>{warTodayRev} грн</code> (поз.: {warToday.Count})
• Дохід гарантій: <code>{warTodayProf} грн</code>

🗓️ <b>З {startDateStr} до сьогодні:</b>
• Аксесуари: <code>{accMonthRev} грн</code> (поз.: {accMonth.Sum(x => x.Quantity)})
• Дохід аксесуарів: <code>{accMonthProf} грн</code>
• iPhone: <code>{devMonthRevUSD} $</code> (поз.: {devMonth.Sum(x => x.Quantity)})
• Дохід iPhone: <code>{devMonthProfUSD} $</code>
• Ремонти: <code>{repairMonthRev} грн</code> (поз.: {repairMonth.Sum(x => x.Quantity)})
• Дохід ремонтів: <code>{repairMonthProf} грн</code>
• Швидкі послуги: <code>{serviceMonthRev} грн</code> (поз.: {serviceMonth.Sum(x => x.Quantity)})
• Дохід послуг: <code>{serviceMonthProf} грн</code>
• Гарантії (Виручка): <code>{warMonthRev} грн</code> (поз.: {warMonth.Count})
• Дохід гарантій: <code>{warMonthProf} грн</code>

💰 <b>Загальний підсумок (всі товари мінус витрати):</b>
• За сьогодні: <code>{netProfitTodayUSD} $</code>
• З початку місяця: <code>{netProfitMonthUSD} $</code>{cashMsg}";

            await _telegramService.SendMessageAsync(msg);

            return Ok(attendance);
        }

        private async Task RunInventoryCheckAsync(int storeId, string storeName)
        {
            using var scope = _scopeFactory.CreateScope();
            var bgContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgTelegramService = scope.ServiceProvider.GetRequiredService<TelegramService>();

            try
            {
                // =======================================================
                // 🔥 1. ОТРИМУЄМО ПОСИЛАННЯ З БАЗИ ДАНИХ
                // =======================================================
                var store = await bgContext.Stores.FindAsync(storeId);

                if (store == null || string.IsNullOrWhiteSpace(store.InventoryCsvUrl))
                {
                    await bgTelegramService.SendMessageAsync($"⚠️ <b>Звірка скасована:</b> Для магазину <b>{storeName}</b> не налаштовано посилання на Google Таблицю в базі даних.");
                    return;
                }

                string csvUrl = store.InventoryCsvUrl;

                // =======================================================
                // 2. ЗАВАНТАЖУЄМО ТАБЛИЦЮ ЯК ТЕКСТ
                // =======================================================
                using var client = new HttpClient();
                string csvData = await client.GetStringAsync(csvUrl);

                var lines = csvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var sheetDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool startCollecting = false;

                foreach (var line in lines)
                {
                    var cols = System.Text.RegularExpressions.Regex.Split(line, "[,;](?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)")
                                     .Select(c => c.Trim(' ', '"'))
                                     .ToArray();

                    if (cols.Length == 0) continue;

                    string firstCol = cols[0];

                    if (firstCol.Equals("наявність", StringComparison.OrdinalIgnoreCase))
                    {
                        startCollecting = true;
                        continue;
                    }

                    if (startCollecting && cols.Length >= 6)
                    {
                        string modelName = firstCol;
                        string imei = cols[5].Trim().Trim('\r', '\n', '\t', '"', ' ');
                        if (!string.IsNullOrWhiteSpace(imei) && imei.Length > 5)
                        {
                            sheetDevices[imei] = string.IsNullOrWhiteSpace(modelName) ? "Невідома модель з таблиці" : modelName;
                        }
                    }
                }

                // =======================================================
                // 3. ОТРИМУЄМО ДАНІ З БАЗИ CRM (Включаючи ті, що на ремонті)
                // =======================================================
                // Тепер беремо IsSold = 0 (На складі) та IsSold = 2 (На ремонті)
                var dbData = await bgContext.DeviceStock
                    .Where(d => d.StoreId == storeId && (d.IsSold == 0 || d.IsSold == 2))
                    .Select(d => new { d.Identifier, d.ModelName, d.IsSold })
                    .ToListAsync();

                // Словник: Ключ - IMEI, Значення - Tuple<НазваМоделі, Статус(0 або 2)>
                var dbDevices = new Dictionary<string, (string ModelName, int IsSold)>(StringComparer.OrdinalIgnoreCase);

                foreach (var d in dbData)
                {
                    string rawimei = d.Identifier?.Trim() ?? "";

                    string imei = rawimei.Trim().Trim('\r', '\n', '\t', '"', ' ');

                    if (!string.IsNullOrWhiteSpace(imei))
                    {
                        string modelName = string.IsNullOrWhiteSpace(d.ModelName) ? "Невідома модель з CRM" : d.ModelName;
                        dbDevices[imei] = (modelName, d.IsSold);
                    }
                }

                // =======================================================
                // 4. ПОРІВНЮЄМО СПИСКИ
                // =======================================================
                var missingInSheetImeis = dbDevices.Keys.Except(sheetDevices.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                var extraInSheetImeis = sheetDevices.Keys.Except(dbDevices.Keys, StringComparer.OrdinalIgnoreCase).ToList();

                // =======================================================
                // 5. ФОРМУЄМО ТА ВІДПРАВЛЯЄМО ЗВІТ
                // =======================================================
                List<string> messageParts = new List<string>();
                StringBuilder currentPart = new StringBuilder();

                currentPart.AppendLine($"📱 <b>Звірка наявності при відкритті ({storeName})</b>\n");

                Action<string> appendText = (text) =>
                {
                    if (currentPart.Length + text.Length > 3800)
                    {
                        messageParts.Add(currentPart.ToString());
                        currentPart.Clear();
                        currentPart.AppendLine($"📱 <b>Звірка ({storeName}) - Продовження...</b>\n");
                    }
                    currentPart.Append(text);
                };

                if (!missingInSheetImeis.Any() && !extraInSheetImeis.Any())
                {
                    appendText("✅ <b>ІДЕАЛЬНО!</b> База CRM повністю співпадає з таблицею Google.");
                }
                else
                {
                    if (missingInSheetImeis.Any())
                    {
                        appendText($"❌ <b>Є в CRM, але НЕМАЄ в Google Sheets ({missingInSheetImeis.Count} шт):</b>\n");
                        foreach (var imei in missingInSheetImeis)
                        {
                            var deviceInfo = dbDevices[imei];
                            string repairMark = deviceInfo.IsSold == 2 ? " 🛠️ <b>[На ремонті]</b>" : "";

                            appendText($"• <code>{deviceInfo.ModelName}/{imei}</code>{repairMark}\n");
                        }
                        appendText("\n");
                    }

                    if (extraInSheetImeis.Any())
                    {
                        appendText($"⚠️ <b>Є в Google Sheets, але НЕМАЄ в CRM ({extraInSheetImeis.Count} шт):</b>\n");
                        foreach (var imei in extraInSheetImeis)
                        {
                            appendText($"• <code>{sheetDevices[imei]}/{imei}</code>\n");
                        }
                        appendText("\n");
                    }
                    appendText("<i>Будь ласка, перевірте ці пристрої та наведіть порядок!</i>");
                }

                messageParts.Add(currentPart.ToString());

                foreach (var part in messageParts)
                {
                    await bgTelegramService.SendMessageAsync(part);
                    await Task.Delay(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка звірки: {ex.Message}");
                await bgTelegramService.SendMessageAsync($"⚠️ <b>Помилка звірки наявності для {storeName}:</b>\n{ex.Message}");
            }
        }
    }
}