using System.ComponentModel.DataAnnotations;

namespace AppleShopikCRM.Client.Models
{
    public class RepairTicket
    {
        public int Id { get; set; }
        public string TicketNumber { get; set; } = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }

        // Тип ремонту: Внутрішній (свій телефон) або Клієнтський
        public RepairType Type { get; set; }

        // Статус заявки
        public RepairStatus Status { get; set; } = RepairStatus.New;

        // Зв'язки
        public int StoreId { get; set; } // Де прийняли
        public string? TechnicianId { get; set; } // Хто робить (User ID)

        // Якщо це внутрішній ремонт (телефон зі складу)
        public int? DeviceStockId { get; set; }
        public string? DeviceModel { get; set; } // Назва моделі (копіюється або вводиться)
        public string? SerialNumber { get; set; } // IMEI/SN

        // Клієнт (якщо ремонт послуга)
        public string? ClientName { get; set; }
        public string? ClientPhone { get; set; }

        // Опис проблеми та рішення
        public string IssueDescription { get; set; } = "";
        public string? ResolutionReport { get; set; } = ""; // Що замінили (текст або список)

        // Фінанси
        public decimal CostPart { get; set; } // Собівартість запчастини
        public decimal Price { get; set; } // Ціна для клієнта (або скільки додасться до вартості телефону)

        [System.ComponentModel.DataAnnotations.Schema.ForeignKey("DeviceStockId")]
        public DeviceStock? DeviceStock { get; set; }
    }

    public enum RepairType { ClientService, InternalStock }

    public enum RepairStatus
    {
        New,            // Прийнято, лежить на магазині
        InTransit,      // Їде до майстра (якщо майстер в іншому місці)
        InProgress,     // Майстер взяв в роботу
        PendingParts,   // Чекаємо запчастину
        Ready,          // Готово (у майстра)
        ReturnedToStore,// Повернуто на магазин (готово до видачі/продажу)
        Completed       // Видано клієнту / Продано
    }
}