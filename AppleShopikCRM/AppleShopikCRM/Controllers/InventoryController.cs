using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleShopikCRM.Client.Models;

namespace AppleShopikCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Це означає, що адреса починається з api/inventory
    public class InventoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public InventoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. ОТРИМАТИ СПИСОК ПРИСТРОЇВ
        // Повна адреса буде: api/inventory/devices
        [HttpGet("devices")]
        public async Task<ActionResult<IEnumerable<object>>> GetDevices()
        {
            var list = await _context.DeviceStock
                .Join(_context.Products, d => d.ProductId, p => p.Id, (d, p) => new {
                    d.Id,            // <--- Додай ID запису (важливо для переміщень!)
                    d.StoreId,       // <--- ОБОВ'ЯЗКОВО ДОДАЙ ЦЕ! Без цього фільтр не працює.
                    d.ProductId,     // <--- Додай ProductId
                    d.Identifier,
                    d.Color,
                    d.Capacity,
                    d.ConditionStatus,
                    d.BatteryHealth,
                    d.SellingPrice,
                    d.PurchasePrice,
                    ModelName = p.Name
                })
                .ToListAsync();
            return Ok(list);
        }

        // 2. ОТРИМАТИ СПИСОК МОДЕЛЕЙ (для випадаючого списку)
        // Повна адреса буде: api/inventory/products
        [HttpGet("products")]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            return await _context.Products.ToListAsync();
        }

        // 3. ДОДАТИ НОВИЙ ПРИСТРІЙ
        [HttpPost("add-device")]
        public async Task<IActionResult> AddDevice(DeviceStock device)
        {
            device.DateAdded = DateTime.Now;
            device.LastUpdated = DateTime.Now;
            _context.DeviceStock.Add(device);
            await _context.SaveChangesAsync();
            return Ok();
        }
        // 4. СТВОРЕННЯ НОВОГО ТОВАРУ (ДОВІДНИК)
        [HttpPost("add-product")]
        public async Task<IActionResult> AddProduct([FromBody] Product product)
        {
            if (string.IsNullOrWhiteSpace(product.Name))
                return BadRequest("Назва не може бути пуста");

            // Перевірка, чи такий товар вже існує
            var exists = await _context.Products.AnyAsync(p => p.Name == product.Name);
            if (exists) return BadRequest("Такий товар вже існує");

            // Важливо: Тут ми жорстко задаємо категорію "Аксесуар" (ID = 2)
            // Якщо у тебе в базі для аксесуарів ID = 0 або інший, зміни цифру тут!
            product.CategoryId = 2;

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return Ok(product);
        }

        [HttpGet("warranty-rates")]
        public async Task<ActionResult<List<WarrantyRate>>> GetWarrantyRates([FromQuery] string modelName)
        {
            // 1. Завантажуємо всі тарифи (крім дефолтних) у пам'ять
            // Таблиця тарифів маленька, тому це працюватиме миттєво
            var allRates = await _context.WarrantyRates
                .Where(w => w.DeviceModel != "Default")
                .ToListAsync();

            // 2. Розумний пошук:
            // Шукаємо тариф, назва якого МІСТИТЬСЯ всередині назви телефону.
            // Наприклад: Тариф "iPhone 15 Pro" міститься у телефоні "iPhone 15 Pro 128GB"
            var rates = allRates
                .Where(r => modelName.Contains(r.DeviceModel, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 3. Якщо знайшли спеціальні тарифи — віддаємо їх
            if (rates.Any())
            {
                return Ok(rates);
            }

            // 4. Якщо не знайшли — віддаємо Default (загальні для всіх)
            var defaults = await _context.WarrantyRates
                .Where(w => w.DeviceModel == "Default")
                .ToListAsync();

            return Ok(defaults);
        }
    }
}