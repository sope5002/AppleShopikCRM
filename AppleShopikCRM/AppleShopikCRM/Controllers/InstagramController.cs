using AppleShopikCRM.Hubs;
using AppleShopikCRM.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AppleShopikCRM.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InstagramController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        private readonly string _verifyToken = "AppleShopikCRM_SecretToken_2026";

        // 🔥 ВСТАВТЕ СВІЙ ДОВГИЙ ТОКЕН СЮДИ
        private readonly string _instagramToken = "EAASkZBCEdP54BQ3FgBZCrGSPg3qzTENre05T78ZCZCkeO1T68j8U3bwAigU8N7ZBATAhB4Lk9WAK1ppfavQpafIiGCebRfTLtDEElZC0IDCeJgB2BUW4R3AD3ld551JPcgCA5qmIn4HUg0fuJlEp7NxoCoCRVD8vGmA6ZBZB5xn6jD6HUlJvgmUgLwiBeb3Dtp1JhifXs1VHgAZDZD";

        public InstagramController(ApplicationDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet("webhook")]
        public IActionResult VerifyWebhook(
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.verify_token")] string token,
            [FromQuery(Name = "hub.challenge")] string challenge)
        {
            if (mode == "subscribe" && token == _verifyToken)
            {
                return Ok(challenge);
            }
            return Forbid();
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveMessage()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                using var jsonDoc = JsonDocument.Parse(body);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("object", out var objProperty) &&
                   (objProperty.GetString() == "page" || objProperty.GetString() == "instagram"))
                {
                    if (root.TryGetProperty("entry", out var entries))
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                            if (entry.TryGetProperty("messaging", out var messagingEvents))
                            {
                                foreach (var messagingEvent in messagingEvents.EnumerateArray())
                                {
                                    await ProcessMessageEvent(messagingEvent);
                                }
                            }
                        }
                    }
                }
                return Ok("EVENT_RECEIVED");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INSTAGRAM WEBHOOK ERROR] {ex.Message}");
                return Ok("EVENT_RECEIVED");
            }
        }

        private async Task ProcessMessageEvent(JsonElement messagingEvent)
        {
            string senderId = messagingEvent.GetProperty("sender").GetProperty("id").GetString() ?? "";

            if (messagingEvent.TryGetProperty("message", out var messageData))
            {
                if (messageData.TryGetProperty("is_echo", out var isEcho) && isEcho.GetBoolean())
                    return;

                string messageId = messageData.GetProperty("mid").GetString() ?? Guid.NewGuid().ToString();

                // 🔥 ДОДАНО ПАРСИНГ МЕДІА
                string text = "";
                string? mediaUrl = null;
                string? mediaType = null;

                if (messageData.TryGetProperty("text", out var textProp))
                    text = textProp.GetString() ?? "";

                // Перевіряємо, чи є вкладення (attachments)
                if (messageData.TryGetProperty("attachments", out var attachments) && attachments.GetArrayLength() > 0)
                {
                    var firstAttachment = attachments[0];
                    if (firstAttachment.TryGetProperty("type", out var typeProp))
                        mediaType = typeProp.GetString();

                    if (firstAttachment.TryGetProperty("payload", out var payload) && payload.TryGetProperty("url", out var urlProp))
                    {
                        mediaUrl = urlProp.GetString();
                        if (string.IsNullOrEmpty(text)) text = $"[Медіа: {mediaType}]";
                    }
                }

                var chat = await _context.Chats.FirstOrDefaultAsync(c => c.ExternalChatId == senderId && c.Platform == ChatPlatform.Instagram);

                if (chat == null)
                {
                    // ... (тут залишається ваш код запиту імені та аватарки, не видаляйте його) ...
                    string realName = "Гість з Instagram";
                    string? fetchedUsername = null;
                    string? avatarUrl = null;

                    try
                    {
                        using var http = new HttpClient();
                        var url = $"https://graph.facebook.com/v19.0/{senderId}?fields=name,username,profile_pic&access_token={_instagramToken}";
                        var response = await http.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(json);

                            string? fetchedName = null;

                            if (doc.RootElement.TryGetProperty("name", out var nameProp))
                                fetchedName = nameProp.GetString();

                            if (doc.RootElement.TryGetProperty("username", out var userProp))
                                fetchedUsername = userProp.GetString();

                            if (doc.RootElement.TryGetProperty("profile_pic", out var picProp))
                                avatarUrl = picProp.GetString();

                            if (!string.IsNullOrWhiteSpace(fetchedName))
                                realName = fetchedName;
                            else if (!string.IsNullOrWhiteSpace(fetchedUsername))
                                realName = "@" + fetchedUsername;
                        }
                    }
                    catch { }

                    chat = new Chat
                    {
                        Id = Guid.NewGuid(),
                        Platform = ChatPlatform.Instagram,
                        ExternalChatId = senderId,
                        CustomerName = realName,
                        CustomerUsername = fetchedUsername,
                        AvatarUrl = avatarUrl,
                        LastActivity = DateTime.UtcNow
                    };
                    _context.Chats.Add(chat);
                }

                bool msgExists = await _context.ChatMessages.AnyAsync(m => m.ExternalMessageId == messageId);
                if (!msgExists)
                {
                    var newMessage = new ChatMessage
                    {
                        Id = Guid.NewGuid(),
                        ChatId = chat.Id,
                        ExternalMessageId = messageId,
                        Direction = MessageDirection.Incoming,
                        Text = text,
                        MediaUrl = mediaUrl, // 🔥 Зберігаємо URL медіа
                        MediaType = mediaType, // 🔥 Зберігаємо тип (image, video)
                        Timestamp = DateTime.UtcNow,
                        IsRead = false
                    };

                    _context.ChatMessages.Add(newMessage);

                    chat.LastMessageText = text;
                    chat.LastActivity = DateTime.UtcNow;
                    chat.UnreadCount += 1;

                    await _context.SaveChangesAsync();

                    await _hubContext.Clients.All.SendAsync("ReceiveNewMessage", chat.Id, newMessage);
                    await _hubContext.Clients.All.SendAsync("UpdateChatList", chat);
                }
            }
        }
    }
}