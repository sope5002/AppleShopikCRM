using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleShopikCRM.Client.Models;

namespace AppleShopikCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccessoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AccessoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Отримати всі аксесуари з назвами моделей
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAccessories()
        {
            var list = await _context.AccessoryStock
                .Join(_context.Products, a => a.ProductId, p => p.Id, (a, p) => new {
                    a.Id,
                    a.ProductId,
                    a.StoreId,      // <--- ОБОВ'ЯЗКОВО ДОДАЙ ЦЕ!
                    ModelName = p.Name,
                    a.Quantity,
                    a.PurchasePrice,
                    a.SellingPrice
                })
                .ToListAsync();
            return Ok(list);
        }

        // 2. Поповнення складу (Додаємо кількість до існуючої)
        [HttpPost("restock")]
        public async Task<IActionResult> Restock([FromBody] RestockRequest request)
        {
            var accessory = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == request.ProductId);

            if (accessory == null)
            {
                // Якщо товару не було — створюємо. 
                // Якщо ціни не прийшли (менеджер додав новинку), вони будуть 0 (адмін потім поправить)
                accessory = new AccessoryStock
                {
                    ProductId = request.ProductId,
                    StoreId = 1,
                    Quantity = request.QuantityToAdd,
                    PurchasePrice = request.NewPurchasePrice,
                    SellingPrice = request.NewSellingPrice,
                    DateAdded = DateTime.Now
                };
                _context.AccessoryStock.Add(accessory);
            }
            else
            {
                // Якщо товар Є: додаємо кількість
                accessory.Quantity += request.QuantityToAdd;

                // ВАЖЛИВО: Оновлюємо ціни ТІЛЬКИ якщо вони були введені (тобто > 0)
                // Це захистить ціну від затирання нулями, коли працює Менеджер
                if (request.NewPurchasePrice > 0)
                    accessory.PurchasePrice = request.NewPurchasePrice;

                if (request.NewSellingPrice > 0)
                    accessory.SellingPrice = request.NewSellingPrice;

                accessory.LastUpdated = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
    }   
}
