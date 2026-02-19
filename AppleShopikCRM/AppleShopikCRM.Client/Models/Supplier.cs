using System.ComponentModel.DataAnnotations;

namespace AppleShopikCRM.Client.Models
{
    public class Supplier
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Notes { get; set; }

        public decimal Debt { get; set; } = 0;
    }
    public class SupplierOutbound
    {
        [Key]
        public int Id { get; set; }

        public int SupplierId { get; set; }
        public int StoreId { get; set; }
        public int SaleId { get; set; }
        public DateTime Date { get; set; }

        public string ItemType { get; set; } = ""; // "Device" або "Accessory"
        public int ItemId { get; set; } // ID зі складу
        public string ModelName { get; set; } = "";
        public string Imei { get; set; } = ""; // Якщо є

        public decimal PurchasePriceUSD { get; set; } // Собівартість
        public decimal AgreedPriceUSD { get; set; } // За скільки віддали

        // Фінансовий стан саме цього товару
        public decimal PaidInCashUSD { get; set; } // Скільки він за нього вже дав готівкою
        public decimal OffsetDebtUSD { get; set; } // Скільки пішло у взаємозалік нашого боргу перед ним

        public string Source { get; set; } = "Опт"; // Опт / Офлайн
    }
}