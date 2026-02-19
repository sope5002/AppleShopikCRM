using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AppleShopikCRM.Client.Models
{
    public class Sale
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public DateTime SaleDate { get; set; } = DateTime.Now;
        public decimal TotalAmount { get; set; }      // Сума без знижки
        public int? RepairTicketId { get; set; }
        public decimal TradeInDiscount { get; set; }  // Знижка за старий тел.
        public decimal FinalAmount { get; set; }      // Сплачено клієнтом
        public decimal TotalProfit { get; set; }      // Загальний чистий прибуток
        public string? Comment { get; set; }
        public string UserId { get; set; } // Хто продав/оформив

        // ТТН Нової Пошти (щоб зручно було відслідковувати)
        public string? TrackingNumber { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }
        public List<SaleItem> SaleItems { get; set; } = new();
        public List<SalePayment> Payments { get; set; } = new();
        public ICollection<CustomerWarranty> CustomerWarranties { get; set; } = new List<CustomerWarranty>();

    }

}
