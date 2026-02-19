using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppleShopikCRM.Client.Models // Або ваш неймспейс
{
    public class RestockHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public int StoreId { get; set; }
        public int AddedQuantity { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
}