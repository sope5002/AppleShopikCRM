using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models
{
    public class TradeIns
    {
        public int Id { get; set; }
        public int SaleId { get; set; }
        public string InboundModel { get; set; } = string.Empty;
        public string InboundIdentifier { get; set; } = string.Empty;
        public string? ConditionDescription { get; set; }
        public decimal AppraisedValue { get; set; } // Сума оцінки
        public DateTime DateCreated { get; set; } = DateTime.Now;
    }
}
