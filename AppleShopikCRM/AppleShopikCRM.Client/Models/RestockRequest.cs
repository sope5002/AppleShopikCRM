namespace AppleShopikCRM.Client.Models
{
    public class RestockRequest
    {
        public int ProductId { get; set; }
        public int QuantityToAdd { get; set; }
        public decimal NewPurchasePrice { get; set; }
        public decimal NewSellingPrice { get; set; }
        public bool ApplyPricesToAllStores { get; set; }
    }
}
