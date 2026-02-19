using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models
{
    public class CashTransaction
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public int ShiftId { get; set; }
        public int? SaleId { get; set; }

        [Column(TypeName = "nvarchar(20)")] public string Type { get; set; } = "Income"; // Income, Expense
        [Column(TypeName = "nvarchar(20)")] public string Category { get; set; } = "Sale";

        public decimal Amount { get; set; }
        [Column(TypeName = "nvarchar(10)")] public string Currency { get; set; } = "UAH"; // UAH, USD, EUR, Card

        public string? Comment { get; set; }
        public DateTime Date { get; set; }
    }
}
