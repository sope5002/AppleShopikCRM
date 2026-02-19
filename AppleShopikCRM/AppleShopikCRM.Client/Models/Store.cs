using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models
{
    public class Store
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? InstagramUrl { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceUAH { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceUSD { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceEUR { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceCard { get; set; } // Безготівковий рахунок
    }
}
