using AppleShopikCRM.Client.Models;// Змініть на ваш namespace з моделями
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppleShopikCRM.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Це створить шлях "api/stores"
    public class StoresController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public StoresController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Цей метод відповідає на запит GET: api/stores/1
        [HttpGet("{id}")]
        public async Task<ActionResult<Store>> GetStore(int id)
        {
            var store = await _context.Stores.FindAsync(id);

            if (store == null)
            {
                return NotFound("Магазин не знайдено");
            }

            return Ok(store);
        }
    }
}