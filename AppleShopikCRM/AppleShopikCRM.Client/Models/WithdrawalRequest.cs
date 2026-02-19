namespace AppleShopikCRM.Client.Models
{
    public class WithdrawalRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
        public int StoreId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string Comment { get; set; } = "";
        public string Pin { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public string EmployeeName { get; set; } = "";
        public string Status { get; set; } = "Pending"; // "Pending", "Completed", "Cancelled"
    }
}
