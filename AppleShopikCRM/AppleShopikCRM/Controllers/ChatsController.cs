using AppleShopikCRM.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests.Abstractions;

namespace AppleShopikCRM.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env; // 🔥 Потрібен для збереження файлів

        private readonly string _instagramToken = "EAASkZBCEdP54BQ3FgBZCrGSPg3qzTENre05T78ZCZCkeO1T68j8U3bwAigU8N7ZBATAhB4Lk9WAK1ppfavQpafIiGCebRfTLtDEElZC0IDCeJgB2BUW4R3AD3ld551JPcgCA5qmIn4HUg0fuJlEp7NxoCoCRVD8vGmA6ZBZB5xn6jD6HUlJvgmUgLwiBeb3Dtp1JhifXs1VHgAZDZD";

        public ChatsController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> GetChats()
        {
            var chats = await _context.Chats
                .OrderByDescending(c => c.LastActivity)
                .Select(c => new { c.Id, c.Platform, c.CustomerName, c.AvatarUrl, c.LastMessageText, c.LastActivity, c.UnreadCount })
                .ToListAsync();
            return Ok(chats);
        }

        [HttpGet("{chatId}/messages")]
        public async Task<IActionResult> GetMessages(Guid chatId)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new { m.Id, m.ChatId, m.Direction, m.Text, m.MediaUrl, m.MediaType, m.Timestamp, m.IsRead })
                .ToListAsync();
            return Ok(messages);
        }

        // ==========================================
        // 🔥 ЗАВАНТАЖЕННЯ ФАЙЛУ НА СЕРВЕР
        // ==========================================
        [HttpPost("upload")]
        public async Task<IActionResult> UploadMedia(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Немає файлу");

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "chats");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            // Генеруємо унікальне ім'я файлу
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Повертаємо відносний шлях до файлу
            return Ok(new { Url = $"/uploads/chats/{fileName}" });
        }

        // ==========================================
        // 🔥 ВІДПРАВКА В ІНСТАГРАМ (З ПІДТРИМКОЮ МЕДІА)
        // ==========================================
        public class SendMessageRequest
        {
            public string Text { get; set; } = "";
            public string? MediaUrl { get; set; } // Сюди прийде лінк від ngrok
            public string? MediaType { get; set; } // "image" або "video"
        }

        [HttpPost("{chatId}/send")]
        public async Task<IActionResult> SendMessage(Guid chatId, [FromBody] SendMessageRequest req)
        {
            var chat = await _context.Chats.FindAsync(chatId);
            if (chat == null) return NotFound("Чат не знайдено");

            if (chat.Platform == ChatPlatform.Instagram)
            {
                using var http = new HttpClient();
                var url = $"https://graph.facebook.com/v19.0/me/messages?access_token={_instagramToken}";

                object payload;

                // Якщо є картинка
                if (!string.IsNullOrEmpty(req.MediaUrl))
                {
                    payload = new
                    {
                        recipient = new { id = chat.ExternalChatId },
                        message = new
                        {
                            attachment = new
                            {
                                type = req.MediaType ?? "image",
                                payload = new { url = req.MediaUrl, is_reusable = true }
                            }
                        }
                    };
                }
                else // Якщо тільки текст
                {
                    if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Порожнє повідомлення");
                    payload = new
                    {
                        recipient = new { id = chat.ExternalChatId },
                        message = new { text = req.Text }
                    };
                }

                var response = await http.PostAsJsonAsync(url, payload);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[FB ERROR]: {error}");
                    return StatusCode(500, "Помилка відправки в Instagram");
                }
            }

            // Зберігаємо в БД
            var newMsg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                ChatId = chat.Id,
                ExternalMessageId = $"out_{Guid.NewGuid()}",
                Direction = MessageDirection.Outgoing,
                Text = req.Text,
                MediaUrl = req.MediaUrl,
                MediaType = req.MediaType,
                Timestamp = DateTime.UtcNow,
                IsRead = true
            };

            _context.ChatMessages.Add(newMsg);
            chat.LastMessageText = string.IsNullOrEmpty(req.MediaUrl) ? req.Text : "[Відправлено медіа]";
            chat.LastActivity = DateTime.UtcNow;
            chat.UnreadCount = 0;
            chat.LastMessageText = req.Text;
            chat.LastActivity = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(newMsg);
        }
        [HttpPost("{id}/mark-read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var chat = await _context.Chats.FindAsync(id);
            if (chat != null)
            {
                chat.UnreadCount = 0;
                await _context.SaveChangesAsync();
                return Ok();
            }
            return NotFound();
        }
    }
}