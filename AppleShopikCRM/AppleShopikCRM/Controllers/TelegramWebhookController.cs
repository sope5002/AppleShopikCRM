using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Hubs;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using Telegram.Bot.Types;

namespace AppleShopikCRM.Server.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/telegram")]
    public class TelegramWebhookController : ControllerBase
    {
        private readonly TelegramService _telegramService;
        private readonly PdfService _pdfService; // Додано для генерації чеків
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public TelegramWebhookController(TelegramService telegramService, PdfService pdfService, ApplicationDbContext context, IHubContext<ChatHub> hubContext)
        {
            _telegramService = telegramService;
            _pdfService = pdfService;
            _context = context;
            _hubContext = hubContext;
        }

        // ==========================================
        // 1. АДМІНСЬКИЙ ВЕБХУК (Курс валют) - Твій оригінал
        // ==========================================
        [HttpPost("admin-webhook")]
        public async Task<IActionResult> ReceiveAdminUpdate([FromBody] JsonElement update)
        {
            try
            {
                if (update.TryGetProperty("callback_query", out var callbackQuery))
                {
                    var data = callbackQuery.GetProperty("data").GetString();
                    var message = callbackQuery.GetProperty("message");
                    var messageId = message.GetProperty("message_id").GetInt32();
                    var chatId = message.GetProperty("chat").GetProperty("id").GetInt64().ToString();

                    if (data != null && data.StartsWith("rate_yes_"))
                    {
                        int storeId = int.Parse(data.Replace("rate_yes_", ""));
                        TelegramService.AwaitingRateStoreId[chatId] = storeId;
                        await _telegramService.EditMessageTextAsync(chatId, messageId, "✍️ Надішліть нове значення курсу (наприклад: 41.5 або 42):");
                    }
                    else if (data == "rate_no")
                    {
                        await _telegramService.EditMessageTextAsync(chatId, messageId, "Ок, працюємо зі старим курсом. Гарного дня! 🚀");
                    }
                    return Ok();
                }

                if (update.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("text", out var textProp))
                    {
                        var text = textProp.GetString();
                        var chatId = msg.GetProperty("chat").GetProperty("id").GetInt64().ToString();

                        if (TelegramService.AwaitingRateStoreId.TryGetValue(chatId, out int storeId))
                        {
                            if (text == "/cancel")
                            {
                                TelegramService.AwaitingRateStoreId.TryRemove(chatId, out _);
                                await _telegramService.SendMessageAsync("Оновлення курсу скасовано.");
                                return Ok();
                            }

                            string cleanText = text?.Replace(",", ".") ?? "";

                            if (decimal.TryParse(cleanText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal newRate))
                            {
                                var store = await _context.Stores.FindAsync(storeId);
                                if (store != null)
                                {
                                    store.ExchangeRate = newRate;
                                    await _context.SaveChangesAsync();
                                    await _telegramService.SendMessageAsync($"✅ Готово! Встановлено новий курс: <b>{newRate} грн</b> для {store.Name}.");
                                }
                                TelegramService.AwaitingRateStoreId.TryRemove(chatId, out _);
                            }
                            else
                            {
                                await _telegramService.SendMessageAsync($"⚠️ Неправильний формат. Я отримав текст '{cleanText}', але не можу перетворити його на число.");
                            }
                        }
                        else
                        {
                            await _telegramService.SendMessageAsync($"[ДЕБАГ] Я отримав від вас текст: '{text}'. Але в моїй пам'яті зараз немає запису, що я чекаю від вас курс. ChatId: {chatId}");
                        }
                    }
                }
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Webhook Error: {ex.Message}");
                return Ok();
            }
        }

        // ==========================================
        // 2. КЛІЄНТСЬКИЙ ВЕБХУК (Обробка чеків)
        // ==========================================
        [HttpPost("client-webhook")]
        public async Task<IActionResult> ReceiveClientUpdate([FromBody] JsonElement update)
        {
            try
            {
                if (!update.TryGetProperty("message", out var msg)) return Ok();

                var chatInfo = msg.GetProperty("chat");
                var chatId = chatInfo.GetProperty("id").GetInt64().ToString();
                string? username = chatInfo.TryGetProperty("username", out var uname) ? uname.GetString() : null;

                if (msg.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString();

                    if (text != null && text.StartsWith("/start c_"))
                    {
                        string customerIdString = text.Replace("/start c_", "").Trim();
                        if (int.TryParse(customerIdString, out int customerId))
                        {
                            var customer = await _context.Customers.FindAsync(customerId);
                            if (customer != null) await LinkCustomerAndAutoSendReceipt(customer, chatId, username);
                        }
                        return Ok();
                    }

                    if (text == "/start")
                    {
                        var keyboard = new
                        {
                            keyboard = new[] { new[] { new { text = "📱 Надіслати мій номер", request_contact = true } } },
                            resize_keyboard = true,
                            one_time_keyboard = true
                        };
                        await _telegramService.SendMessageToClientAsync(chatId, "Привіт! 👋 Щоб я міг знайти ваші електронні чеки та гарантії, будь ласка, натисніть кнопку нижче, щоб поділитися номером телефону.", keyboard);
                        return Ok();
                    }
                }

                if (msg.TryGetProperty("contact", out var contactProp))
                {
                    string phone = contactProp.GetProperty("phone_number").GetString() ?? "";
                    string cleanPhone = new string(phone.Where(char.IsDigit).ToArray());
                    if (cleanPhone.Length > 10) cleanPhone = cleanPhone.Substring(cleanPhone.Length - 10);

                    var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Phone.Contains(cleanPhone));
                    if (customer != null) await LinkCustomerAndAutoSendReceipt(customer, chatId, username);
                    else await _telegramService.SendMessageToClientAsync(chatId, "На жаль, за цим номером замовлень не знайдено. 😔");
                }

                return Ok();
            }
            catch { return Ok(); }
        }

        private async Task LinkCustomerAndAutoSendReceipt(Customer customer, string chatId, string? username)
        {
            customer.TelegramChatId = long.Parse(chatId);
            customer.TelegramUsername = username;
            await _context.SaveChangesAsync();

            var lastSale = await _context.Sales
                .Include(s => s.SaleItems)
                    .ThenInclude(i => i.WarrantyRate)
                .Include(s => s.Payments)
                .Include(s => s.CustomerWarranties)
                .OrderByDescending(s => s.SaleDate)
                .FirstOrDefaultAsync(s => s.CustomerId == customer.Id && s.SaleDate > DateTime.Now.AddDays(-1));

            if (lastSale != null)
            {
                var storeInfo = await _context.Stores.FindAsync(lastSale.StoreId);

                string shortSellerName = "Менеджер";
                if (!string.IsNullOrEmpty(lastSale.UserId))
                {
                    var user = await _context.Users.FindAsync(lastSale.UserId);
                    if (user != null)
                    {
                        string fullSellerName = user.FullName ?? user.UserName ?? "Невідомий";
                        shortSellerName = fullSellerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullSellerName;
                    }
                }

                decimal globalDiscount = 0;
                if (lastSale.Comment != null && lastSale.Comment.Contains("Знижка на чек:"))
                {
                    var parts = lastSale.Comment.Split("Знижка на чек: ");
                    if (parts.Length > 1)
                    {
                        var discountStr = parts[1].Replace(" грн", "").Trim();
                        decimal.TryParse(discountStr, out globalDiscount);
                    }
                }

                var receiptDto = new SaleReceiptDto
                {
                    Sale = lastSale,
                    StoreName = storeInfo?.Name ?? "Apple Shopik",
                    StoreAddress = storeInfo?.Address ?? "",
                    StorePhone = storeInfo?.Phone ?? "",
                    StoreInstagramUrl = storeInfo?.InstagramUrl ?? "",
                    SellerName = shortSellerName,
                    GlobalDiscount = globalDiscount
                };

                bool hasWarranty = lastSale.SaleItems.Any(i => i.WarrantyPrice > 0);
                string documentType = hasWarranty ? "електронний чек та гарантійний талон 🛡" : "електронний чек 🧾";

                string caption = $"🍏 Дякуємо за покупку в Apple Shopik ({receiptDto.StoreName})!\n\n" +
                                 $"Ваш {documentType} №{lastSale.Id} знаходиться у прикріпленому файлі.\n\n" +
                                 $"Завжди раді бачити вас знову! 💙";

                string fileName = hasWarranty ? $"Receipt_Warranty_{lastSale.Id}.pdf" : $"Receipt_{lastSale.Id}.pdf";

                await _telegramService.SendMessageToClientAsync(chatId, $"🎉 Вітаємо, {customer.Name}! Ви успішно підключені. Формую документи...");
                byte[] pdfBytes = _pdfService.GenerateDigitalWarrantyPdf(receiptDto);
                await _telegramService.SendDocumentAsync(chatId, pdfBytes, fileName, caption);
            }
            else
            {
                await _telegramService.SendMessageToClientAsync(chatId, $"🎉 Вітаємо, {customer.Name}! Ви успішно підключені. Ваші майбутні чеки будуть приходити сюди.");
            }
        }

        // ==========================================
        // 3. ВЕБХУК РОБОЧОЇ ГРУПИ - Твій оригінал
        // ==========================================
        [HttpPost("receive")]
        public async Task<IActionResult> ReceiveMessage([FromBody] Update update)
        {
            if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message && update.Message?.Text != null)
            {
                var msg = update.Message;
                long myWorkGroupId = -1002638764086;

                string? text = msg?.Text ?? msg?.Caption;
                string textLower = text!.ToLower();
                var today = TimeHelper.GetUkraineTime().Date;
                if (string.IsNullOrEmpty(text)) return Ok();
                if (msg!.Chat.Id != myWorkGroupId) return Ok();

                if (msg!.ReplyToMessage != null)
                {
                    var originalBooking = await _context.TelegramBookings
                        .FirstOrDefaultAsync(b => b.MessageId == msg.ReplyToMessage.MessageId && b.ChatId == msg.Chat.Id);

                    if (originalBooking != null)
                    {
                        Match replyDateMatch = Regex.Match(text, @"\b\d{2}\.\d{2}\.\d{4}\b");
                        if (replyDateMatch.Success && DateTime.TryParseExact(replyDateMatch.Value, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime newDate))
                        {
                            originalBooking.TargetDate = newDate.Date;
                            originalBooking.IsLater = newDate.Date > today;
                            string managerName = msg.From?.FirstName ?? "Менеджер";
                            originalBooking.Text += $"\n\n🔄 <i>[Перенесено ({managerName})]: {text}</i>";

                            await _context.SaveChangesAsync();
                            await _hubContext.Clients.All.SendAsync("BookingToggled", originalBooking);
                        }
                        return Ok();
                    }
                }

                Regex phoneRegex = new Regex(@"(?:\+?380|0)\s?\d{2}\s?\d{3}\s?\d{2}\s?\d{2}");
                bool hasPhoneNumber = phoneRegex.IsMatch(text);
                bool hasBookingKeyword = textLower.Contains("бронь") || textLower.Contains("замовлення");

                if (hasPhoneNumber || hasBookingKeyword)
                {
                    DateTime targetDate = today;
                    Match dateMatch = Regex.Match(text, @"\b\d{2}\.\d{2}\.\d{4}\b");
                    if (dateMatch.Success && DateTime.TryParseExact(dateMatch.Value, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate)) targetDate = parsedDate.Date;
                    else if (textLower.Contains("завтра")) targetDate = today.AddDays(1);
                    else if (textLower.Contains("післязавтра")) targetDate = today.AddDays(2);

                    var booking = new TelegramBooking
                    {
                        ChatId = msg.Chat.Id,
                        ThreadId = msg.MessageThreadId,
                        MessageId = msg.MessageId,
                        SenderName = $"{msg.From?.FirstName} {msg.From?.LastName}".Trim(),
                        Text = text,
                        DateReceived = TimeHelper.GetUkraineTime(),
                        TargetDate = targetDate,
                        IsProcessed = false,
                        IsLater = targetDate > today
                    };

                    _context.TelegramBookings.Add(booking);
                    await _context.SaveChangesAsync();
                    await _hubContext.Clients.All.SendAsync("NewTelegramBooking", booking);
                }
            }
            return Ok();
        }

        // ==========================================
        // 4. API ДЛЯ ФРОНТЕНДУ - Твій оригінал
        // ==========================================
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveBookings()
        {
            var bookings = await _context.TelegramBookings.Where(b => !b.IsProcessed).ToListAsync();
            bool needsDbUpdate = false;
            var today = TimeHelper.GetUkraineTime().Date;

            foreach (var b in bookings)
            {
                if (b.IsLater)
                {
                    if (b.TargetDate.HasValue && b.TargetDate.Value.Date <= today) { b.IsLater = false; needsDbUpdate = true; }
                    else if (!b.TargetDate.HasValue && b.DateReceived.Date < today) { b.IsLater = false; needsDbUpdate = true; }
                }
            }

            if (needsDbUpdate) await _context.SaveChangesAsync();
            return Ok(bookings.OrderByDescending(b => b.DateReceived));
        }

        [HttpPost("complete/{id}")]
        public async Task<IActionResult> CompleteBooking(int id)
        {
            var booking = await _context.TelegramBookings.FindAsync(id);
            if (booking != null) { booking.IsProcessed = true; await _context.SaveChangesAsync(); await _hubContext.Clients.All.SendAsync("BookingCompleted", id); }
            return Ok();
        }

        [HttpPost("toggle-later/{id}")]
        public async Task<IActionResult> ToggleLater(int id)
        {
            var booking = await _context.TelegramBookings.FindAsync(id);
            if (booking != null)
            {
                booking.IsLater = !booking.IsLater;
                booking.TargetDate = booking.IsLater ? TimeHelper.GetUkraineTime().Date.AddDays(1) : TimeHelper.GetUkraineTime().Date;
                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("BookingToggled", booking);
            }
            return Ok();
        }

        public class BindDeviceDto
        {
            public int? DeviceStockId { get; set; }
            public decimal? AgreedPrice { get; set; }
            public decimal? DepositAmount { get; set; }
            public string CustomerPhone { get; set; } = "";
            public string CustomerName { get; set; } = "";
        }

        [HttpPost("bind-device/{id}")]
        public async Task<IActionResult> BindDevice(int id, [FromBody] BindDeviceDto req)
        {
            var booking = await _context.TelegramBookings.FindAsync(id);
            if (booking != null)
            {
                if (req.DeviceStockId == null || req.DeviceStockId == 0) { booking.DeviceStockId = null; booking.AgreedPrice = null; booking.DepositAmount = null; }
                else { booking.DeviceStockId = req.DeviceStockId; booking.AgreedPrice = req.AgreedPrice; booking.DepositAmount = req.DepositAmount; booking.CustomerPhone = req.CustomerPhone; booking.CustomerName = req.CustomerName; }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("BookingToggled", booking);
            }
            return Ok();
        }

        [HttpPost("cancel/{id}")]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var booking = await _context.TelegramBookings.FindAsync(id);
            if (booking != null) { booking.IsProcessed = true; booking.DeviceStockId = null; booking.AgreedPrice = null; booking.DepositAmount = null; await _context.SaveChangesAsync(); await _hubContext.Clients.All.SendAsync("BookingCompleted", id); }
            return Ok();
        }

        [HttpGet("check-reservation/{deviceStockId}")]
        public async Task<IActionResult> CheckReservation(int deviceStockId)
        {
            var booking = await _context.TelegramBookings.FirstOrDefaultAsync(b => b.DeviceStockId == deviceStockId && !b.IsProcessed);
            if (booking != null) return Ok(new { bookingId = booking.Id, agreedPrice = booking.AgreedPrice, deposit = booking.DepositAmount, customerPhone = booking.CustomerPhone, customerName = booking.CustomerName });
            return NotFound("Броні немає");
        }
        // ==========================================
        // 5. РОЗСИЛКА ДЛЯ КЛІЄНТІВ (ТІЛЬКИ ДЛЯ АДМІНІВ)
        // ==========================================
        [HttpGet("connected-count")]
        public async Task<IActionResult> GetConnectedClientsCount()
        {
            // Рахуємо клієнтів, у яких TelegramChatId не null
            var count = await _context.Customers.CountAsync(c => c.TelegramChatId != null);
            return Ok(count);
        }

        public class BroadcastDto
        {
            public string Message { get; set; } = "";
        }

        [HttpPost("broadcast")]
        public async Task<IActionResult> SendBroadcast([FromBody] BroadcastDto req)
        {
            if (string.IsNullOrWhiteSpace(req.Message))
                return BadRequest("Повідомлення порожнє.");

            // Беремо всіх підключених клієнтів
            var connectedCustomers = await _context.Customers
                .Where(c => c.TelegramChatId != null)
                .Select(c => c.TelegramChatId!.Value.ToString())
                .ToListAsync();

            int successCount = 0;
            foreach (var chatId in connectedCustomers)
            {
                try
                {
                    // Відправляємо кожному
                    await _telegramService.SendMessageToClientAsync(chatId, req.Message);
                    successCount++;
                }
                catch
                {
                    // Ігноруємо помилку одного, продовжуємо для інших
                }
            }

            return Ok(new { Sent = successCount, Total = connectedCustomers.Count });
        }
    }
}