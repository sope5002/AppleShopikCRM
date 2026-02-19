using Microsoft.AspNetCore.Identity;

namespace AppleShopikCRM.Server.Models // Перевір namespace
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = "";


        // Прив'язка до магазину (може бути null для Адміна)
        public int? StoreId { get; set; }
    }
    public class RegisterDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public int? StoreId { get; set; } // Може бути null для адміна
        public string Role { get; set; } // Наприклад: "Admin", "Manager", "Seller"
    }
}
