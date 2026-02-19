using System;

namespace AppleShopikCRM.Server.Models // (Або Client.Models для клієнтської частини)
{
    public class TelegramBooking
    {
        public int Id { get; set; }
        public long ChatId { get; set; }
        public int? ThreadId { get; set; }     // ID гілки (теми) в групі
        public int MessageId { get; set; }
        public string SenderName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime DateReceived { get; set; }
        public bool IsProcessed { get; set; } = false;
        public bool IsLater { get; set; } = false; // Прапорець "На потім"

        // 🔥 ДАТА ДЛЯ АВТОМАТИЧНОГО "ПРОБУДЖЕННЯ"
        public DateTime? TargetDate { get; set; }

        // 🔥 ПОЛЯ ДЛЯ ПРИВ'ЯЗКИ ДО КАСИ ТА СКЛАДУ
        public int? DeviceStockId { get; set; }       // Який саме телефон відклали
        public decimal? AgreedPrice { get; set; }     // Зафіксована ціна продажу
        public decimal? DepositAmount { get; set; }   // Сума завдатку
        public string? CustomerPhone { get; set; }    // Телефон клієнта
        public string? CustomerName { get; set; }     // Ім'я клієнта
    }
}