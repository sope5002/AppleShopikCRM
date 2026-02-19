using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppleShopikCRM.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Отримати список клієнтів (з пошуком)
        [HttpGet]
        public async Task<ActionResult<List<CustomerDto>>> GetCustomers([FromQuery] string? search)
        {
            var query = _context.Customers
                .Include(c => c.Sales)
                    .ThenInclude(s => s.SaleItems)
                .Include(c => c.Warranties)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                query = query.Where(c =>
                    c.Name.ToLower().Contains(search) ||
                    c.Phone.Contains(search) ||
                    (c.Email != null && c.Email.ToLower().Contains(search)));
            }

            var customers = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

            var dtos = customers.Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Email = c.Email,
                Notes = c.Notes,
                TelegramChatId = c.TelegramChatId, // 🔥 Додали, щоб фронтенд бачив статус
                CreatedAt = c.CreatedAt,
                TotalPurchases = c.Sales?.Count ?? 0,
                TotalSpent = c.Sales?.Sum(s => s.FinalAmount) ?? 0,
                TotalProfit = c.Sales?.Sum(s => s.TotalProfit) ?? 0,
                LastVisit = c.Sales?.OrderByDescending(s => s.SaleDate).FirstOrDefault()?.SaleDate ?? c.CreatedAt,

                Purchases = c.Sales?.SelectMany(s => s.SaleItems.Select(i => new CustomerPurchaseDto
                {
                    Date = s.SaleDate,
                    ItemName = i.ProductName ?? "Невідомий товар",
                    Details = i.Identifier,
                    Quantity = i.Quantity,
                    Price = i.SoldSellingPrice * i.Quantity,
                    Profit = i.PositionProfit
                }))
                .OrderByDescending(p => p.Date)
                .ToList() ?? new List<CustomerPurchaseDto>(),

                Warranties = c.Warranties?.Select(w => new CustomerWarrantyDto
                {
                    Id = w.Id,
                    DeviceModel = w.DeviceModel,
                    Imei = w.Imei,
                    WarrantyType = w.WarrantyType,
                    TermMonths = w.TermMonths,
                    StartDate = w.StartDate,
                    EndDate = w.EndDate,
                    IsActive = w.EndDate >= TimeHelper.GetUkraineTime(),
                    DaysLeft = w.EndDate >= TimeHelper.GetUkraineTime() ? (w.EndDate - TimeHelper.GetUkraineTime()).Days : 0
                }).ToList() ?? new List<CustomerWarrantyDto>()
            }).ToList();

            return Ok(dtos);
        }

        // 🔥 НОВИЙ МЕТОД: Отримати клієнта за ID (потрібен для перевірки Telegram)
        [HttpGet("{id}")]
        public async Task<ActionResult<CustomerDto>> GetCustomerById(int id)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null) return NotFound("Клієнта не знайдено");

            return Ok(new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Phone = customer.Phone,
                Email = customer.Email,
                Notes = customer.Notes,
                TelegramChatId = customer.TelegramChatId // 🔥 Головне поле для нашої логіки!
            });
        }

        // 2. Створити клієнта
        [HttpPost]
        public async Task<ActionResult<CustomerDto>> CreateCustomer(CustomerDto dto) // 🔥 Змінено тип повернення
        {
            if (await _context.Customers.AnyAsync(c => c.Phone == dto.Phone))
                return BadRequest("Клієнт з таким номером телефону вже існує.");

            var customer = new Customer
            {
                Name = dto.Name,
                Phone = dto.Phone,
                Email = dto.Email,
                Notes = dto.Notes,
                CreatedAt = TimeHelper.GetUkraineTime()
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            // 🔥 Записуємо ID, який база щойно видала
            dto.Id = customer.Id;

            // 🔥 Повертаємо об'єкт з ID назад на фронтенд
            return Ok(dto);
        }

        // 3. Оновити клієнта
        [HttpPut("{id}")]
        public async Task<ActionResult> UpdateCustomer(int id, CustomerDto dto)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            if (await _context.Customers.AnyAsync(c => c.Phone == dto.Phone && c.Id != id))
                return BadRequest("Цей номер телефону вже належить іншому клієнту.");

            customer.Name = dto.Name;
            customer.Phone = dto.Phone;
            customer.Email = dto.Email;
            customer.Notes = dto.Notes;

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 4. Перевірка гарантії по телефону
        [HttpGet("check-warranty/{phone}")]
        public async Task<ActionResult<WarrantyCheckResult>> CheckWarranty(string phone)
        {
            var cleanPhone = phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");

            var customer = await _context.Customers
                .Include(c => c.Sales)
                .Include(c => c.Warranties)
                .FirstOrDefaultAsync(c => c.Phone.Contains(cleanPhone));

            if (customer == null)
            {
                return Ok(new WarrantyCheckResult { Found = false });
            }

            var warranties = customer.Warranties?.Select(w => new CustomerWarrantyDto
            {
                Id = w.Id,
                DeviceModel = w.DeviceModel,
                Imei = w.Imei,
                WarrantyType = w.WarrantyType,
                TermMonths = w.TermMonths,
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                IsActive = w.EndDate >= DateTime.Today,
                DaysLeft = w.EndDate >= DateTime.Today ? (w.EndDate - DateTime.Today).Days : 0
            }).ToList() ?? new List<CustomerWarrantyDto>();

            return Ok(new WarrantyCheckResult
            {
                Found = true,
                Customer = new CustomerDto
                {
                    Id = customer.Id,
                    Name = customer.Name,
                    Phone = customer.Phone,
                    Email = customer.Email,
                    TelegramChatId = customer.TelegramChatId,
                    TotalPurchases = customer.Sales?.Count ?? 0,
                    TotalSpent = customer.Sales?.Sum(s => s.FinalAmount) ?? 0
                },
                ActiveWarranties = warranties.Where(w => w.IsActive).OrderBy(w => w.DaysLeft).ToList(),
                ExpiredWarranties = warranties.Where(w => !w.IsActive).OrderByDescending(w => w.EndDate).ToList()
            });
        }

        // Отримати клієнта за номером телефону для CustomerSelector
        [HttpGet("by-phone")]
        public async Task<ActionResult<CustomerDto>> GetByPhone([FromQuery] string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return NotFound();

            var cleanPhone = new string(phone.Where(char.IsDigit).ToArray());

            if (string.IsNullOrEmpty(cleanPhone))
                return NotFound();

            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Phone.Contains(cleanPhone));

            if (customer == null)
                return NotFound();

            return Ok(new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Phone = customer.Phone,
                Email = customer.Email,
                Notes = customer.Notes,
                TelegramChatId = customer.TelegramChatId
            });
        }
    }
}