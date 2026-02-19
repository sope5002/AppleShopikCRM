using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace AppleShopikCRM.Server.Services
{
    public class TelegramService
    {
        private readonly HttpClient _http;
        private readonly string _adminBotToken;
        private readonly string _clientBotToken;
        private readonly string[] _adminChatIds;

        public static readonly ConcurrentDictionary<string, int> AwaitingRateStoreId = new();

        public TelegramService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _adminBotToken = config["TelegramSettings:AdminBotToken"] ?? "";
            _clientBotToken = config["TelegramSettings:ClientBotToken"] ?? "";

            string chatIdsStr = config["TelegramSettings:AdminChatIds"] ?? "";
            _adminChatIds = chatIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(id => id.Trim())
                                      .ToArray();
        }

        // 🔥 Твій оригінальний метод (повернув GET-запит, як у тебе все працювало)
        public async Task SendMessageAsync(string message)
        {
            if (string.IsNullOrEmpty(_adminBotToken) || _adminChatIds.Length == 0) return;

            foreach (var chatId in _adminChatIds)
            {
                var url = $"https://api.telegram.org/bot{_adminBotToken}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}&parse_mode=HTML";

                try
                {
                    await _http.GetAsync(url);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TG ERROR] Помилка відправки для {chatId}: {ex.Message}");
                }
            }
        }

        // 🔥 Твій оригінальний метод
        public async Task SendRateQuestionAsync(int storeId, decimal currentRate)
        {
            if (string.IsNullOrEmpty(_adminBotToken) || _adminChatIds.Length == 0) return;

            var url = $"https://api.telegram.org/bot{_adminBotToken}/sendMessage";

            foreach (var chatId in _adminChatIds)
            {
                var payload = new
                {
                    chat_id = chatId,
                    text = $"💱 Поточний курс: <b>{currentRate} грн</b>.\nБажаєте оновити курс $?",
                    parse_mode = "HTML",
                    reply_markup = new
                    {
                        inline_keyboard = new[]
                        {
                            new[]
                            {
                                new { text = "✅ Так", callback_data = $"rate_yes_{storeId}" },
                                new { text = "❌ Ні", callback_data = "rate_no" }
                            }
                        }
                    }
                };

                await SendRequest(url, payload);
            }
        }

        // 🔥 Твій оригінальний метод
        public async Task EditMessageTextAsync(string chatId, int messageId, string newText)
        {
            var url = $"https://api.telegram.org/bot{_adminBotToken}/editMessageText";
            var payload = new
            {
                chat_id = chatId,
                message_id = messageId,
                text = newText,
                parse_mode = "HTML"
            };

            await SendRequest(url, payload);
        }

        // 🔥 Твій оригінальний метод
        private async Task SendRequest(string url, object payload)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(url, payload);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[TG ERROR]: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TG EXCEPTION]: {ex.Message}");
            }
        }

        // ==========================================
        // ДОДАНО ТІЛЬКИ ЦЕ ДЛЯ КЛІЄНТСЬКОГО БОТА (ЧЕКИ)
        // ==========================================
        public async Task SendMessageToClientAsync(string chatId, string message, object? replyMarkup = null)
        {
            if (string.IsNullOrEmpty(_clientBotToken)) return;

            var url = $"https://api.telegram.org/bot{_clientBotToken}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = message,
                parse_mode = "HTML",
                reply_markup = replyMarkup
            };

            await SendRequest(url, payload);
        }

        public async Task SendDocumentAsync(string chatId, byte[] fileBytes, string fileName, string caption)
        {
            if (string.IsNullOrEmpty(_clientBotToken)) return;

            var url = $"https://api.telegram.org/bot{_clientBotToken}/sendDocument";

            try
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(chatId), "chat_id");
                form.Add(new StringContent(caption), "caption");
                form.Add(new StringContent("HTML"), "parse_mode");

                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                form.Add(fileContent, "document", fileName);

                var response = await _http.PostAsync(url, form);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[TG ERROR]: Не вдалося відправити PDF: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TG EXCEPTION]: {ex.Message}");
            }
        }
    }
}