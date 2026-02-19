using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models
{
    public class TimesheetArchive
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Month { get; set; } = string.Empty; // Наприклад "Березень 2026"

        // --- ФАКТИЧНІ ПОКАЗНИКИ ---
        public int WorkingDays { get; set; }
        public int Latenesses { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal IphonesSold { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal OtherAppleSold { get; set; }
        public int WarrantiesSold { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal AccessoriesRevenueUAH { get; set; }
        public int ServicesSold { get; set; }

        // --- НАРАХУВАННЯ ---
        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseSalaryAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal IphonesBonusAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal OtherAppleBonusAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal WarrantiesBonusAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal AccessoriesBonusAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal ServicesBonusAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal BonusAmount { get; set; } // Особлива премія
        [Column(TypeName = "decimal(18,2)")]
        public decimal PenaltyAmount { get; set; } // Штрафи
        [Column(TypeName = "decimal(18,2)")]
        public decimal AdvancePayment { get; set; } // Аванс

        // --- ПІДСУМКИ ---
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAccrued { get; set; } // Всього нараховано
        [Column(TypeName = "decimal(18,2)")]
        public decimal FinalPayout { get; set; }  // До виплати

        public DateTime CreatedAt { get; set; }  = DateTime.Now;
    }
}