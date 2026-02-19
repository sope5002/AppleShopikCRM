using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleShopikCRM.Client.Models;

namespace AppleShopikCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TransfersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TransfersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. ОТРИМАТИ ВСІ ЗАПИТИ (Активні)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TransferRequest>>> GetTransfers()
        {
            // Join-имо таблиці, щоб отримати назви магазинів і товарів
            var query = from t in _context.TransferRequests
                        join p in _context.Products on t.ProductId equals p.Id
                        join s1 in _context.Stores on t.FromStoreId equals s1.Id
                        join s2 in _context.Stores on t.ToStoreId equals s2.Id
                        // Лівий Join для IMEI (бо для аксесуарів його немає)
                        join d in _context.DeviceStock on t.DeviceStockId equals d.Id into devices
                        from dev in devices.DefaultIfEmpty()
                        orderby t.DateInitiated descending
                        select new TransferRequest
                        {
                            Id = t.Id,
                            ProductId = t.ProductId,
                            DeviceStockId = t.DeviceStockId,
                            FromStoreId = t.FromStoreId,
                            ToStoreId = t.ToStoreId,
                            Quantity = t.Quantity,
                            Status = t.Status,
                            DateInitiated = t.DateInitiated,
                            ModelName = p.Name,
                            FromStoreName = s1.Name,
                            ToStoreName = s2.Name,
                            Identifier = dev != null ? dev.Identifier : ""
                        };

            return await query.ToListAsync();
        }

        // 2. СТВОРИТИ ПЕРЕМІЩЕННЯ (ВІДПРАВИТИ)
        [HttpPost("initiate")]
        public async Task<IActionResult> InitiateTransfer(TransferRequest req)
        {
            // Якщо це аксесуар — відразу списуємо з балансу відправника
            if (req.DeviceStockId == null)
            {
                var stock = await _context.AccessoryStock
                    .FirstOrDefaultAsync(a => a.ProductId == req.ProductId && a.StoreId == req.FromStoreId);

                if (stock == null || stock.Quantity < req.Quantity)
                    return BadRequest("Недостатньо товару на складі відправника");

                stock.Quantity -= req.Quantity;
            }
            // Якщо це телефон — поки просто створюємо запит (StoreId змінимо при підтвердженні)

            req.DateInitiated = DateTime.Now;
            req.Status = "Pending";

            _context.TransferRequests.Add(req);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // 3. ПІДТВЕРДИТИ ОТРИМАННЯ (ПРИЙНЯТИ)
        [HttpPost("confirm/{id}")]
        public async Task<IActionResult> ConfirmTransfer(int id)
        {
            var transfer = await _context.TransferRequests.FindAsync(id);
            if (transfer == null || transfer.Status != "Pending")
                return BadRequest("Запит не знайдено або вже оброблено");

            // А. Якщо це ТЕЛЕФОН (Тут ціна і так переміщається разом із записом)
            if (transfer.DeviceStockId != null)
            {
                var device = await _context.DeviceStock.FindAsync(transfer.DeviceStockId);
                if (device != null)
                {
                    device.StoreId = transfer.ToStoreId; // Змінюємо прописку телефону
                    device.LastUpdated = DateTime.Now;
                }
            }
            // Б. Якщо це АКСЕСУАР (Тут треба скопіювати ціну)
            else
            {
                // 1. Дізнаємося ціну на магазині, ЗВІДКИ їхав товар
                var sourceStock = await _context.AccessoryStock
                    .FirstOrDefaultAsync(a => a.ProductId == transfer.ProductId && a.StoreId == transfer.FromStoreId);

                // Запам'ятовуємо ціни (якщо раптом запис вже видалили, ставимо 0, але це рідкість)
                decimal incomingPurchasePrice = sourceStock?.PurchasePrice ?? 0;
                decimal incomingSellingPrice = sourceStock?.SellingPrice ?? 0;

                // 2. Шукаємо товар на магазині, КУДИ приїхав товар
                var destStock = await _context.AccessoryStock
                    .FirstOrDefaultAsync(a => a.ProductId == transfer.ProductId && a.StoreId == transfer.ToStoreId);

                if (destStock != null)
                {
                    // Якщо товар вже є — додаємо кількість
                    destStock.Quantity += transfer.Quantity;

                    // ОНОВЛЮЄМО ЦІНИ на актуальні (ті, що приїхали)
                    if (incomingPurchasePrice > 0) destStock.PurchasePrice = incomingPurchasePrice;
                    if (incomingSellingPrice > 0) destStock.SellingPrice = incomingSellingPrice;

                    destStock.LastUpdated = DateTime.Now;
                }
                else
                {
                    // Якщо товару ще не було — створюємо новий запис ІЗ ПРАВИЛЬНИМИ ЦІНАМИ
                    _context.AccessoryStock.Add(new AccessoryStock
                    {
                        ProductId = transfer.ProductId,
                        StoreId = transfer.ToStoreId,
                        Quantity = transfer.Quantity,
                        PurchasePrice = incomingPurchasePrice, // <--- Було 0, тепер ціна береться з джерела
                        SellingPrice = incomingSellingPrice,   // <--- Було 0, тепер ціна береться з джерела
                        DateAdded = DateTime.Now
                    });
                }
            }

            transfer.Status = "Completed";
            transfer.DateCompleted = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 4. Допоміжний метод: Отримати магазини
        [HttpGet("stores")]
        public async Task<ActionResult<IEnumerable<Store>>> GetStores()
        {
            return await _context.Stores.ToListAsync();
        }
    }
}
