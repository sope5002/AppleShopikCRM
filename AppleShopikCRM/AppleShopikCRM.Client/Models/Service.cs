namespace AppleShopikCRM.Client.Models
{
    public class Service
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal PurchasePrice { get; set; } // Собівартість запчастини
        public decimal SellingPrice { get; set; }  // Ціна для клієнта
        public DateTime DateCreated { get; set; } = DateTime.Now;
    }
}
