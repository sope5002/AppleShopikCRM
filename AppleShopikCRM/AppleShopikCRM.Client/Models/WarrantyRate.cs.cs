namespace AppleShopikCRM.Client.Models
{
    public class WarrantyRate
    {
        public int Id { get; set; }
        public string DeviceModel { get; set; } = "";
        public string Type { get; set; } = "";
        public int TermMonths { get; set; }
        public decimal Price { get; set; }
    }
}
