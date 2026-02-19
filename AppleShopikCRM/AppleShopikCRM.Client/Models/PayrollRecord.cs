using System;
using System.ComponentModel.DataAnnotations;

namespace AppleShopikCRM.Client.Models
{
    public class PayrollRecord
    {
        public int Id { get; set; }
        public string? UserId { get; set; }
        public string EmployeeName { get; set; } = "";
        public int StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public int Month { get; set; }
        public int Year { get; set; }

        // Статистика на момент збереження
        public int WorkingDays { get; set; }
        public int Latenesses { get; set; } // Кількість запізнень
        public int DevicesSold { get; set; }
        public int WarrantiesSold { get; set; }
        public decimal AccessoriesRevenueUAH { get; set; }
        public decimal ServicesRevenueUAH { get; set; }

        // Фінанси
        public decimal BaseSalaryCalculated { get; set; } // Скільки нарахувала формула
        public decimal AdvancePayment { get; set; } // Виданий аванс (вводиться вручну)
        public decimal FinalPayout { get; set; } // Залишок до виплати (Base - Advance)

        public DateTime UpdatedAt { get; set; }
    }
    public class PayrollDto
    {
        public int? RecordId { get; set; } // Якщо null - значить це ще не збережено в БД
        public string UserId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public int StoreId { get; set; }
        public string StoreName { get; set; } = "";

        public int WorkingDays { get; set; }
        public int Latenesses { get; set; }

        public int DevicesSold { get; set; }
        public int WarrantiesSold { get; set; }
        public decimal AccessoriesRevenueUAH { get; set; }
        public decimal ServicesRevenueUAH { get; set; }

        public decimal BaseSalaryCalculated { get; set; }
        public decimal AdvancePayment { get; set; }
        public decimal FinalPayout => BaseSalaryCalculated - AdvancePayment;

        public bool IsSaved => RecordId.HasValue;
    }
    public class EmployeeSalaryRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = ""; // Прив'язка до конкретного працівника

        // 💰 Ставка та штрафи
        public decimal MonthlyBaseSalary { get; set; } // Місячна ставка (грн)
        public decimal PenaltyPerLateness { get; set; } // Штраф за 1 запізнення (грн)

        // 🎁 Фіксовані бонуси (грн за 1 шт)
        public decimal BonusPerDevice { get; set; } // Бонус за продаж 1 техніки
        public decimal BonusPerWarranty { get; set; } // Бонус за продаж 1 гарантії

        // 📊 Відсотки (%)
        public decimal AccessoryBonusPercent { get; set; } // % від суми проданих аксесуарів
        public decimal ServiceBonusPercent { get; set; } // % від суми швидких послуг
    }
}

