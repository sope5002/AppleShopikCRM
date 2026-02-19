namespace AppleShopikCRM.Client.Models
{
    public class DeviceView
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public int ProductId { get; set; }
        public string Identifier { get; set; } = "";
        public string? ModelName { get; set; }
        public int IsSold { get; set; }
        public string? Color { get; set; }
        public string? Capacity { get; set; }
        public string? ConditionStatus { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal RepairCost { get; set; }
        public int BatteryHealth { get; set; }
        public bool IsConsignment { get; set; } = false;
        public string? SupplierName { get; set; }
        public bool IsSupplierPaid { get; set; } = false;
        public int? SupplierId { get; set; }
        public decimal SupplierPaidAmount { get; set; }
        public DateTime DateAdded { get; set; }
    }

    public class AccessoryDto
    {
        public int ProductId { get; set; }
        public int StoreId { get; set; }
        public string ModelName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public string? ImageUrl { get; set; }   // ← ФОТО
        public string? CompatibleModels { get; set; }
    }
    public class SupplierDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Phone { get; set; }

        public string? Notes { get; set; }
    }
    public class PostalShipmentDto
    {
        public int Id { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal FinalAmount { get; set; }
        public string? CustomerName { get; set; }
        public string? TrackingNumber { get; set; }
        public string? Status { get; set; }
        public List<string> Items { get; set; } = new(); // Список назв товарів
    }
    public class TimesheetFormDto
    {
        public string FullName { get; set; } = "";
        public string Month { get; set; } = DateTime.Today.AddMonths(-1).ToString("MMMM yyyy");

        public int WorkingDays { get; set; }
        public int Latenesses { get; set; }
        public decimal IphonesSold { get; set; }
        public decimal OtherAppleSold { get; set; }
        public int WarrantiesSold { get; set; }
        public decimal AccessoriesRevenueUAH { get; set; }
        public int ServicesSold { get; set; }

        public decimal BaseSalaryAmount { get; set; }
        public decimal IphonesBonusAmount { get; set; }
        public decimal OtherAppleBonusAmount { get; set; }
        public decimal WarrantiesBonusAmount { get; set; }
        public decimal AccessoriesBonusAmount { get; set; }
        public decimal ServicesBonusAmount { get; set; }
        public decimal BonusAmount { get; set; }
        public decimal PenaltyAmount { get; set; }
        public decimal AdvancePayment { get; set; }

        public decimal TotalAccrued => BaseSalaryAmount + IphonesBonusAmount + OtherAppleBonusAmount + WarrantiesBonusAmount + AccessoriesBonusAmount + ServicesBonusAmount + BonusAmount - PenaltyAmount;
        public decimal FinalPayout => TotalAccrued - AdvancePayment;
    }
}
