using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AppleShopikCRM.Client.Models
{
    public class SaleItem
    {
        public int Id { get; set; }
        public int SaleId { get; set; }
        [ForeignKey("SaleId")]
        [JsonIgnore] // Щоб не було "зациклення" даних при серіалізації
        [Column(TypeName = "nvarchar(20)")]
        public string ItemType { get; set; } = "Service";
        public int ItemId { get; set; }
        public string ProductName { get; set; } = "";
        public string? Identifier { get; set; }      // Зберігаємо IMEI тут
                                                     // ----------------------------
        public int Quantity { get; set; } = 1;
        public decimal SoldPurchasePrice { get; set; } // Ціна закупки на момент продажу
        public decimal SoldSellingPrice { get; set; }  // Ціна продажу на момент продажу
        public decimal WarrantyPrice { get; set; }     // Дохід з гарантії
        public decimal PositionProfit { get; set; }    // Прибуток з цієї позиції
        public int? WarrantyId { get; set; }

        public decimal PurchasePriceUSD { get; set; } // Фіксуємо закупку в $
        public decimal SellingPriceUSD { get; set; }  // Ціна продажу, переведена в $ (Price / ExchangeRate)
        public decimal ProfitUSD { get; set; }        // Чистий прибуток в $ (SellingPriceUSD - PurchasePriceUSD)
        public Sale? Sale { get; set; }

        [ForeignKey("WarrantyId")]
        public virtual WarrantyRate? WarrantyRate { get; set; }

    }
}
