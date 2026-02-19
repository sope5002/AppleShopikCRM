using System;

namespace AppleShopikCRM.Client.Models
{
    public class PendingTransfer
    {
        public int Id { get; set; }
        public int SourceStoreId { get; set; }
        public int TargetStoreId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string Comment { get; set; } = "";
        public string Status { get; set; } = "Pending"; // Pending, Completed, Rejected
        public DateTime DateCreated { get; set; }
        public string CreatedBy { get; set; } = "";
    }
}