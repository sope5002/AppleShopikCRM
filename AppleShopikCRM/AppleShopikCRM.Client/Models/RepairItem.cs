using System.Text.Json.Serialization;

namespace AppleShopikCRM.Client.Models
{
    public class RepairItem
    {
        public int Id { get; set; }

        // Зв'язок з головним квитком ремонту
        public int RepairTicketId { get; set; }

        // Назва роботи або запчастини (напр. "Оригінальний дисплей", "Чистка динаміків")
        public string Name { get; set; } = "";

        // Собівартість конкретно цієї запчастини
        public decimal Cost { get; set; }

        // Ціна цієї послуги/запчастини для клієнта
        public decimal Price { get; set; }

        // Навігаційна властивість (JsonIgnore потрібен, щоб API не зависав через зациклення)
        [JsonIgnore]
        public RepairTicket? RepairTicket { get; set; }
    }
}