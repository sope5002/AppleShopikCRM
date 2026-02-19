using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleShopikCRM.Client.Models;

namespace AppleShopikCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CashController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegramService;
        private readonly IHubContext<ChatHub> _hubContext; // 🔥 НОВЕ

        public CashController(ApplicationDbContext context) => _context = context;

        // 1. ОТРИМАТИ СТАН КАСИ (Поточна зміна + Загальний баланс)
        [HttpGet("status/{storeId}")]
        public async Task<IActionResult> GetCashStatus(int storeId)
        {
            var today = DateTime.Today;

            // Загальний баланс
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            // Поточна зміна
            var shift = await _context.CashShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId && s.ShiftDate == today);

            // Останні 20 транзакцій
            var history = await _context.CashTransactions
                .Where(t => t.StoreId == storeId)
                .OrderByDescending(t => t.Date)
                .Take(20)
                .ToListAsync();

            return Ok(new
        {
                Store = store,
                Shift = shift ?? new CashShift(),
                History = history
            });
        }

        // 2. ЗРОБИТИ ВИТРАТУ / ІНКАСАЦІЮ
        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] CashTransaction req)
        {
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                var today = DateTime.Today;
                var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == req.StoreId && s.ShiftDate == today);

                // Якщо зміни немає, створюємо (щоб прив'язати витрату)
                if (shift == null)
                {
                    shift = new CashShift { StoreId = req.StoreId, ShiftDate = today };
                    _context.CashShifts.Add(shift);
                    await _context.SaveChangesAsync();
                }

                // Оновлюємо зміну (витрати за день)
                if (req.Currency == "UAH") shift.ExpenseUAH += req.Amount;
                else if (req.Currency == "USD") shift.ExpenseUSD += req.Amount;
                else if (req.Currency == "EUR") shift.ExpenseEUR += req.Amount;

                // Оновлюємо загальний баланс магазину (мінусуємо)
                var store = await _context.Stores.FindAsync(req.StoreId);
                if (req.Currency == "UAH") store.BalanceUAH -= req.Amount;
                else if (req.Currency == "USD") store.BalanceUSD -= req.Amount;
                else if (req.Currency == "EUR") store.BalanceEUR -= req.Amount;

                // Записуємо транзакцію
                req.ShiftId = shift.Id;
                req.Type = "Expense";
                req.Category = "Withdrawal"; // Або "Incasso"
                req.Date = DateTime.Now;

                _context.CashTransactions.Add(req);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                string currentUserName = User.FindFirst(ClaimTypes.UserData)?.Value ?? User.Identity?.Name ?? "Менеджер";
                string msg = $"❌ <b>Переміщення відхилено!</b>\n\n" +
                             $"🏪 <b>Хто відхилив:</b> {targetStore?.Name}\n" +
                             $"👤 <b>Співробітник:</b> {currentUserName}\n" +
                             $"💰 <b>Сума:</b> {transfer.Amount.ToString("N0")} {transfer.Currency}\n" +
                             $"<i>(Гроші повернуто в касу {sourceStore?.Name})</i>";

                _ = _telegramService.SendMessageAsync(msg);
                await _hubContext.Clients.Group($"Store_{transfer.TargetStoreId}").SendAsync("RefreshCashNotifications");
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
    }

    public class WithdrawConfirmDto
    {
        public string RequestId { get; set; } = "";
        public string Pin { get; set; } = "";
    }
}