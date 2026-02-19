namespace AppleShopikCRM.Client.Models
{
    public class CustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? Email { get; set; }
        public long? TelegramChatId { get; set; } // Тут буде зберігатися той самий ID для відправки
        public string? TelegramUsername { get; set; } // Нікнейм клієнта (для вашої зручності)
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastVisit { get; set; }
        public int TotalPurchases { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal TotalProfit { get; set; }
        public List<CustomerPurchaseDto> Purchases { get; set; } = new();
        public List<CustomerWarrantyDto> Warranties { get; set; } = new();
    }
    public class CustomerPurchaseDto
    {
        public DateTime Date { get; set; }
        public string ItemName { get; set; } = "";
        public string? Details { get; set; } // Для IMEI або кольору
        public int Quantity { get; set; }
        public decimal Price { get; set; } // Сума за позицію
        public decimal Profit { get; set; } // Чистий прибуток (продаж - закупка)
    }

    public class CustomerWarrantyDto
    {
        public int Id { get; set; }
        public string DeviceModel { get; set; } = "";
        public string? Imei { get; set; }
        public string WarrantyType { get; set; } = "";
        public int TermMonths { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public int DaysLeft { get; set; }
        public int SaleId { get; set; }
    }

    /// <summary>
    /// Запит для перевірки гарантії за номером телефону
    /// </summary>
    public class WarrantyCheckResult
    {
        public bool Found { get; set; }
        public CustomerDto? Customer { get; set; }
        public List<CustomerWarrantyDto> ActiveWarranties { get; set; } = new();
        public List<CustomerWarrantyDto> ExpiredWarranties { get; set; } = new();
    }
}
