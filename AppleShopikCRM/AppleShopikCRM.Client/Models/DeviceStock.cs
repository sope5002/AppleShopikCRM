using System.ComponentModel.DataAnnotations;

namespace AppleShopikCRM.Client.Models
{
    public class DeviceStock
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public int StoreId { get; set; }

        [MaxLength(100)] // Обмеження довжини для БД
        public string? ModelName { get; set; }
        public string Identifier { get; set; } = string.Empty; // IMEI або Serial
        public string? Color { get; set; }
        public string? Capacity { get; set; }
        public int BatteryHealth { get; set; }
        public string ConditionStatus { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SellingPrice { get; set; }
        // 0 = В наявності, 1 = Продано, 2 = В ремонті
        public int IsSold { get; set; }
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        [System.ComponentModel.DataAnnotations.Schema.ForeignKey("ProductId")]
        public Product? Product { get; set; } // Сам об'єкт товару
    }
}
