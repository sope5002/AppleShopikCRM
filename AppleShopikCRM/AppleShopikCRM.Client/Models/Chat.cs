using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AppleShopikCRM.Server.Models // або ваш поточний namespace для БД
{
    // Платформи, з яких можуть писати клієнти
    public enum ChatPlatform
    {
        Instagram,
        Telegram,
        Viber
    }

    // Напрямок повідомлення
    public enum MessageDirection
    {
        Incoming, // Від клієнта до нас
        Outgoing  // Від менеджера клієнту
    }

    // ГОЛОВНА ТАБЛИЦЯ ЧАТІВ
    public class Chat
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public ChatPlatform Platform { get; set; }

        // Унікальний ID клієнта в межах платформи (напр. ID в Instagram)
        [Required]
        public string ExternalChatId { get; set; } = "";

        public string CustomerName { get; set; } = "Новий клієнт";
        public string? CustomerUsername { get; set; } // @nickname
        public string? AvatarUrl { get; set; } // Аватарка з інсти/тг

        // Для швидкого відображення в списку чатів
        public string? LastMessageText { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public int UnreadCount { get; set; } = 0;

        // Зв'язок з повідомленнями
        public List<ChatMessage> Messages { get; set; } = new();
    }

    // ТАБЛИЦЯ ПОВІДОМЛЕНЬ
    public class ChatMessage
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // До якого чату належить
        public Guid ChatId { get; set; }
        [JsonIgnore]
        public Chat? Chat { get; set; }

        // ID повідомлення на серверах Фейсбуку чи ТГ (щоб не було дублікатів)
        public string ExternalMessageId { get; set; } = "";

        public MessageDirection Direction { get; set; }

        public string? Text { get; set; }

        // Якщо надіслали фото/голосове - тут буде посилання
        public string? MediaUrl { get; set; }
        public string? MediaType { get; set; } // "image", "audio", "video"

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Чи прочитано повідомлення
        public bool IsRead { get; set; } = false;
    }
}