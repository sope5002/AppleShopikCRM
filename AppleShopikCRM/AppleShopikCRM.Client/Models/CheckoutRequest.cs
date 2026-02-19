namespace AppleShopikCRM.Client.Models
{
    /// <summary>
    /// Розширений CheckoutRequest з підтримкою клієнта.
    /// ЗАМІНЮЄ старий CheckoutRequest у CartItemDto.cs
    /// </summary>
    public class CheckoutRequest
    {
        public int StoreId { get; set; }
        public List<CartItemDto> Items { get; set; } = new();
        public List<SalePayment> Payments { get; set; } = new();
        public List<TradeInDto> TradeIns { get; set; } = new();
        public decimal GlobalDiscount { get; set; }

        public string Source { get; set; } = "Offline";
        public bool IsPostalShipment { get; set; }
        public string? TrackingNumber { get; set; }

        // --- КЛІЄНТ ---
        public int? CustomerId { get; set; }       // Якщо обраний з бази
        public string? CustomerName { get; set; }  // Якщо новий клієнт
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }

    }
}
