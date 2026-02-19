using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models
{
    /// <summary>
    /// База клієнтів магазину
    /// </summary>
    public class Customer
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string Phone { get; set; } = "";

        [MaxLength(200)]
        public long? TelegramChatId { get; set; } // Тут буде зберігатися той самий ID для відправки
        public string? TelegramUsername { get; set; } // Нікнейм клієнта (для вашої зручності)
        public string? Email { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastVisit { get; set; } = DateTime.Now;

        [MaxLength(500)]
        public string? Notes { get; set; }

        // Навігаційні властивості
        public List<Sale> Sales { get; set; } = new();
        public List<CustomerWarranty> Warranties { get; set; } = new();
    }

    /// <summary>
    /// Гарантія, оформлена на клієнта
    /// </summary>
    public class CustomerWarranty
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        public int SaleId { get; set; }
        [ForeignKey(nameof(SaleId))]
        [InverseProperty("CustomerWarranties")]
        public Sale? Sale { get; set; }

        // Що саме під гарантією
        [MaxLength(200)]
        public string DeviceModel { get; set; } = "";

        [MaxLength(50)]
        public string? Imei { get; set; }

        // Тариф гарантії
        public int WarrantyRateId { get; set; }
        public WarrantyRate? WarrantyRate { get; set; }

        [MaxLength(100)]
        public string WarrantyType { get; set; } = ""; // Standard, Premium тощо
        public int TermMonths { get; set; }

        public DateTime StartDate { get; set; } = DateTime.Now;
        public DateTime EndDate { get; set; }

        public bool IsActive => DateTime.Now <= EndDate;
        public int DaysLeft => Math.Max(0, (EndDate - DateTime.Now).Days);
    }
}
