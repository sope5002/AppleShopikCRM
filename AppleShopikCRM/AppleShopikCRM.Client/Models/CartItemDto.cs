namespace AppleShopikCRM.Client.Models
{
    public class CartItemDto
    {
        public int ProductId { get; set; }       // ID з довідника Products (для аксесуарів)
        public int? DeviceStockId { get; set; }  // ID з DeviceStocks (для телефонів)
        public string ModelName { get; set; } = "";
        public string Identifier { get; set; } = ""; // IMEI
        public string Category { get; set; } = "";   // "Device" або "Accessory"
        public int Quantity { get; set; } = 1;
        public int MaxQuantity { get; set; }
        public decimal Price { get; set; }       // Ціна продажу
        public decimal Cost { get; set; }        // Собівартість (для довідки в коді)

        public string Color { get; set; } = "";
        public string Capacity { get; set; } = "";

        public int? WarrantyId { get; set; }     // ID тарифу (з WarrantyRates)
        public decimal WarrantyPrice { get; set; } = 0;
        public string WarrantyInfo { get; set; } = ""; // Напр: "12 міс. Standard"
    }

    public class SalePayment
    {
        [System.ComponentModel.DataAnnotations.Key]
        public int Id { get; set; }
        public int SaleId { get; set; } // До якого чеку належить
        [System.Text.Json.Serialization.JsonIgnore] // Щоб не ганяти зайві дані по мережі
        public Sale? Sale { get; set; }
        public string Method { get; set; } = "Готівка";
        public decimal Amount { get; set; }      // Сума у валюті (наприклад, 100$)
        public decimal ExchangeRate { get; set; } = 1; // Курс (для гривні = 1)
        public string? Bank { get; set; }
        public int? Months { get; set; }
        public decimal CommissionAmount { get; set; }
    }
    public class TradeInDto
    {
        public string Model { get; set; } = "";
        public string Imei { get; set; } = "";
        public string Condition { get; set; } = "";
        public decimal Price { get; set; }
        public string Color { get; set; } = "";
        public string Capacity { get; set; } = "";
        public int BatteryHealth { get; set; } = 100;
    }
    public class SaleReportDto
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Comment { get; set; } = "";
        public decimal TotalAmount { get; set; }  // Скільки взяли грошей (брутто)
        public decimal TradeInDiscount { get; set; } // Знижка
        public decimal FinalAmount { get; set; }  // Скільки реально заплатив клієнт
        public decimal TotalProfit { get; set; }  // Чистий прибуток

        public List<SaleItemDto> Items { get; set; } = new();
        public List<TradeInDto> TradeIns { get; set; } = new();
    }

    public class SaleItemDto
    {
        public string ModelName { get; set; } = "";
        public string Type { get; set; } = ""; // Device, Accessory
        public int Quantity { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal WarrantyPrice { get; set; }
        public decimal WarrantyProfit { get; set; }
        public decimal Profit { get; set; }
    }

    public class CheckoutRequest
    {
        public int StoreId { get; set; }
        public List<CartItemDto> Items { get; set; } = new();
        public List<SalePayment> Payments { get; set; } = new();
        public TradeInDto? TradeIns { get; set; }

    }
    public class RepairPaymentRequest
    {
        public int TicketId { get; set; }
        public List<SalePayment> Payments { get; set; } = new();
    }
    public class SaleReceiptDto
    {
        public Sale Sale { get; set; } = new();

        // Дані магазину
        public string StoreName { get; set; } = "";
        public string StoreAddress { get; set; } = "";
        public string StorePhone { get; set; } = "";
        public string StoreInstagramUrl { get; set; } = "";

        // Дані продавця
        public string SellerName { get; set; } = "";
        public decimal GlobalDiscount { get; set; }
    }
}
