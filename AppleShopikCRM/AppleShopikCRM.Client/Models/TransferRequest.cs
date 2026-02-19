using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models
{
    public class TransferRequest
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int? DeviceStockId { get; set; } // Null, якщо це аксесуар
        public int FromStoreId { get; set; }
        public int ToStoreId { get; set; }
        public int Quantity { get; set; }

        [Column(TypeName = "nvarchar(20)")]
        public string Status { get; set; } = "Pending"; // Pending, Completed

        public DateTime DateInitiated { get; set; }
        public DateTime? DateCompleted { get; set; }

        // Додаткові поля для зручного відображення (не в базі)
        [NotMapped] public string ModelName { get; set; } = "";
        [NotMapped] public string FromStoreName { get; set; } = "";
        [NotMapped] public string ToStoreName { get; set; } = "";
        [NotMapped] public string Identifier { get; set; } = ""; // IMEI
    }
}

