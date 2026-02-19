namespace AppleShopikCRM.Client.Models
{
    public class CashShift
    {
        public int Id { get; set; }
        public int StoreId { get; set; }
        public DateTime ShiftDate { get; set; }
        public decimal IncomeUAH { get; set; }
        public decimal IncomeUSD { get; set; }
        public decimal IncomeEUR { get; set; }
        public decimal IncomeCard { get; set; }
        public decimal ExpenseUAH { get; set; }
        public decimal ExpenseUSD { get; set; }
        public decimal ExpenseEUR { get; set; }
    }
}
