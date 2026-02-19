using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppleShopikCRM.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfService _pdfService;

        public AdminController(ApplicationDbContext context, PdfService pdfService, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _pdfService = pdfService;
            _userManager = userManager;
        }

        [HttpGet("shifts")]
        public async Task<IActionResult> GetShifts([FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var targetEnd = end.Date.AddDays(1);
            var shifts = await (from a in _context.EmployeeAttendances.AsNoTracking()
                                join s in _context.Stores on a.StoreId equals s.Id into storeGroup
                                from store in storeGroup.DefaultIfEmpty()
                                where a.WorkDate >= start.Date && a.WorkDate < targetEnd
                                orderby a.WorkDate descending, a.ClockInTime descending
                                select new
                                {
                                    UserName = a.UserName,
                                    StoreName = store != null ? store.Name : $"Магазин #{a.StoreId}",
                                    WorkDate = a.WorkDate,
                                    ClockInTime = a.ClockInTime,
                                    ClockOutTime = a.ClockOutTime
                                }).ToListAsync();
            return Ok(shifts);
        }

        public class NewUserRequest
        {
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string Role { get; set; } = "";
            public int StoreId { get; set; }
        }

        [HttpPost("create-user")]
        public async Task<IActionResult> CreateUser([FromBody] NewUserRequest req)
        {
            var existingUser = await _userManager.FindByEmailAsync(req.Email);
            if (existingUser != null) return BadRequest("Користувач з таким Email або логіном вже існує!");

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                FullName = req.FullName,
                StoreId = req.StoreId,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded) return BadRequest("Помилка створення.");

            if (!string.IsNullOrEmpty(req.Role))
                await _userManager.AddToRoleAsync(user, req.Role);

            return Ok();
        }

        public class UserListDto
        {
            public string Id { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Email { get; set; } = "";
            public string Role { get; set; } = "";
            public int? StoreId { get; set; }
            public string StoreName { get; set; } = "";
            public bool IsBlocked { get; set; } // 🔥 ДОДАНО ПОЛЕ БЛОКУВАННЯ
        }

        public class UpdateUserRequest
        {
            public string FullName { get; set; } = "";
            public int StoreId { get; set; }
            public string Role { get; set; } = "";
            public string Password { get; set; } = "";
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _userManager.Users.ToListAsync();
            var stores = await _context.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
            var result = new List<UserListDto>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);

                // Перевіряємо чи акаунт заблоковано (час блокування більший за поточний)
                bool isBlocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow;

                result.Add(new UserListDto
                {
                    Id = u.Id,
                    FullName = u.FullName ?? "Без імені",
                    Email = u.Email ?? "",
                    StoreId = u.StoreId,
                    StoreName = u.StoreId.HasValue && stores.ContainsKey(u.StoreId.Value) ? stores[u.StoreId.Value] : "Не призначено",
                    Role = roles.FirstOrDefault() ?? "Manager",
                    IsBlocked = isBlocked
                });
            }
            return Ok(result);
        }

        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest req)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound("Користувача не знайдено");

            user.FullName = req.FullName;
            user.StoreId = req.StoreId == 0 ? null : req.StoreId;

            if (!string.IsNullOrWhiteSpace(req.Password))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _userManager.ResetPasswordAsync(user, token, req.Password);
            }

            await _userManager.UpdateAsync(user);
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!string.IsNullOrWhiteSpace(req.Role)) await _userManager.AddToRoleAsync(user, req.Role);

            return Ok();
        }

        // 🔥 М'ЯКЕ ВИДАЛЕННЯ (БЛОКУВАННЯ)
        [HttpPost("users/{id}/block")]
        public async Task<IActionResult> BlockUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound("Користувача не знайдено");

            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == id) return BadRequest("Ви не можете заблокувати власний акаунт!");

            // Вмикаємо функцію блокування і ставимо дату блокування на "завжди"
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            return Ok();
        }

        // 🔥 РОЗБЛОКУВАННЯ
        [HttpPost("users/{id}/unblock")]
        public async Task<IActionResult> UnblockUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound("Користувача не знайдено");

            // Знімаємо дату блокування
            await _userManager.SetLockoutEndDateAsync(user, null);

            return Ok();
        }

        [HttpGet("missing-costs")]
        public async Task<IActionResult> GetMissingCosts()
        {
            // Фільтруємо пристрої, де закупка (борг) = 0
            var problemDevices = await _context.DeviceStock
                .AsNoTracking()
                .Where(d => d.PurchasePrice <= 0)
                .Select(d => new ProblemDeviceDto
                {
                    Id = d.Id,
                    ModelName = d.ModelName ?? "Невідомо",
                    Identifier = d.Identifier,
                    IsSold = d.IsSold == 1,
                    Color = d.Color,
                    Capacity = d.Capacity,
                    ConditionStatus = d.ConditionStatus,
                    BatteryHealth = d.BatteryHealth,
                    TempPurchasePrice = 0,
                    TempRepairCost = d.RepairCost // Бачимо поточні вкладення
                })
                .ToListAsync();
            return Ok(problemDevices);
        }

        [HttpPost("fix-purchase-price")]
        public async Task<IActionResult> FixPurchasePrice([FromBody] FixCostRequest req)
        {
            var device = await _context.DeviceStock.FindAsync(req.DeviceId);
            if (device == null) return NotFound("Пристрій не знайдено");

            // Зберігаємо стару ціну, щоб розуміти, наскільки збільшився борг
            decimal oldPrice = device.PurchasePrice;

            // Встановлюємо БОРГ перед постачальником
            device.PurchasePrice = req.RealPurchasePrice;

            // 🔥 ЛОГІКА ПОСТАЧАЛЬНИКА
            if (device.IsConsignment && device.SupplierId > 0)
            {
                var supplier = await _context.Suppliers.FindAsync(device.SupplierId);
                if (supplier != null)
                {
                    // Нараховуємо різницю (якщо раніше було 0, а тепер 500, то додаємо 500)
                    decimal debtDifference = req.RealPurchasePrice - oldPrice;
                    supplier.Debt += debtDifference;
                }
            }

            if (device.IsSold == 1)
            {
                var saleItem = await _context.SaleItems
                    .Include(si => si.Sale)
                    .FirstOrDefaultAsync(si => si.ItemType == "Device" && si.ItemId == req.DeviceId);

                if (saleItem != null && saleItem.Sale != null)
                {
                    decimal course = saleItem.Sale.ExchangeRate > 0 ? saleItem.Sale.ExchangeRate : 41.5m;

                    decimal totalCostUSD = device.PurchasePrice + device.RepairCost;

                    saleItem.PurchasePriceUSD = totalCostUSD;
                    saleItem.SoldPurchasePrice = totalCostUSD * course;
                    saleItem.ProfitUSD = saleItem.SellingPriceUSD - totalCostUSD;
                    saleItem.PositionProfit = saleItem.SoldSellingPrice - saleItem.SoldPurchasePrice + saleItem.WarrantyPrice;

                    var allItems = await _context.SaleItems.Where(si => si.SaleId == saleItem.SaleId).ToListAsync();
                    saleItem.Sale.TotalProfit = allItems.Where(i => i.Id != saleItem.Id).Sum(si => si.PositionProfit) + saleItem.PositionProfit;
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
        [HttpGet("missing-repair-costs")]
        public async Task<IActionResult> GetMissingRepairCosts()
        {
            var problemRepairs = await _context.RepairTickets
                .AsNoTracking()
                .Include(r => r.DeviceStock)
                .Include(r => r.Items)
                .Where(r => r.Items.Any(i => i.Cost == 0))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ProblemRepairDto
                {
                    Id = r.Id,
                    TicketNumber = r.TicketNumber ?? r.Id.ToString(),
                    DeviceModel = r.DeviceStockId != null && r.DeviceStock != null
                        ? $"{r.DeviceStock.ModelName} {r.DeviceStock.Capacity} {r.DeviceStock.Color}".Trim()
                        : r.DeviceModel ?? "Пристрій",

                    // 🔥 ЧІТКО БЕРЕМО IMEI ЗІ СКЛАДУ
                    Identifier = r.DeviceStockId != null && r.DeviceStock != null ? r.DeviceStock.Identifier : "",

                    IsStockDevice = r.DeviceStockId != null,
                    StockDeviceIsSold = r.DeviceStockId != null && r.DeviceStock != null ? r.DeviceStock.IsSold : 0,
                    Items = r.Items.Where(i => i.Cost == 0).Select(i => new RepairItemDto
                    {
                        Id = i.Id,
                        Name = i.Name,
                        Price = i.Price,
                        Cost = 0,
                        TempCost = 0
                    }).ToList()
                })
                .ToListAsync();

            return Ok(problemRepairs);
        }

        [HttpPost("fix-repair-item-cost")]
        public async Task<IActionResult> FixRepairItemCost([FromBody] FixRepairItemCostRequest req)
        {
            var repairItem = await _context.RepairItems.FindAsync(req.ItemId);
            if (repairItem == null) return NotFound("Деталь не знайдено");

            var ticket = await _context.RepairTickets.FindAsync(req.TicketId);
            if (ticket == null) return NotFound("Ремонт не знайдено");

            decimal newCostUSD = req.RealCostPart;
            repairItem.Cost = newCostUSD;

            // 1. Оновлюємо вкладення магазину в пристрій (якщо він зі складу)
            if (ticket.DeviceStockId.HasValue)
            {
                var device = await _context.DeviceStock.FindAsync(ticket.DeviceStockId.Value);
                if (device != null)
                {
                    // 🔥 ЦЕ НАШЕ ВКЛАДЕННЯ, А НЕ БОРГ ПОСТАЧАЛЬНИКУ ТЕЛЕФОНУ
                    device.RepairCost += newCostUSD;

                    // Якщо телефон вже продано — оновлюємо прибуток у чеку
                    if (device.IsSold == 1)
                    {
                        var saleItem = await _context.SaleItems.Include(si => si.Sale)
                            .FirstOrDefaultAsync(si => si.ItemType == "Device" && si.ItemId == device.Id);

                        if (saleItem != null && saleItem.Sale != null)
                        {
                            decimal course = saleItem.Sale.ExchangeRate > 0 ? saleItem.Sale.ExchangeRate : 41.5m;
                            // Собівартість зросла -> Прибуток впав
                            saleItem.PurchasePriceUSD += newCostUSD;
                            saleItem.SoldPurchasePrice += (newCostUSD * course);
                            saleItem.ProfitUSD -= newCostUSD;
                            saleItem.PositionProfit -= (newCostUSD * course);
                            saleItem.Sale.TotalProfit -= (newCostUSD * course);
                        }
                    }
                }
            }

            // 2. Якщо деталь взята у майстра в борг
            if (req.MasterId > 0 && newCostUSD > 0)
            {
                var master = await _context.Suppliers.FindAsync(req.MasterId);
                if (master != null) master.Debt += newCostUSD;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("kpi")]
        public async Task<IActionResult> GetKPIs([FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var targetEnd = end.Date.AddDays(1);
            var sales = await _context.Sales
                .AsNoTracking()
                .Include(s => s.SaleItems)
                .Where(s => s.SaleDate >= start.Date && s.SaleDate < targetEnd)
                .ToListAsync();

            var groupedSales = sales.GroupBy(s => s.UserId).ToList();
            var kpiResult = new List<ManagerKpiDto>();

            foreach (var group in groupedSales)
            {
                var user = await _userManager.FindByIdAsync(group.Key!);
                string userName = user != null ? (user.FullName ?? user.UserName ?? "Невідомий") : "Невідомий (видалений)";

                var kpi = new ManagerKpiDto { UserName = userName };

                foreach (var sale in group)
                {
                    decimal course = sale.ExchangeRate > 10 ? sale.ExchangeRate : 41.5m;
                    kpi.TotalRevenueUAH += sale.FinalAmount;

                    foreach (var i in sale.SaleItems)
                    {
                        kpi.ItemsSold += i.Quantity;
                        kpi.TotalProfitUSD += i.ProfitUSD + (i.WarrantyProfit / course);

                        decimal revUAH = i.SoldSellingPrice * i.Quantity;
                        decimal profUSD = i.ProfitUSD;

                        switch (i.ItemType)
                        {
                            case "Device":
                                kpi.Devices.Count += i.Quantity;
                                kpi.Devices.RevenueUAH += revUAH;
                                kpi.Devices.ProfitUSD += profUSD;
                                break;
                            case "Accessory":
                                kpi.Accessories.Count += i.Quantity;
                                kpi.Accessories.RevenueUAH += revUAH;
                                kpi.Accessories.ProfitUSD += profUSD;
                                break;
                            case "Service":
                                kpi.Repairs.Count += i.Quantity;
                                kpi.Repairs.RevenueUAH += revUAH;
                                kpi.Repairs.ProfitUSD += profUSD;
                                break;
                            case "PosService":
                                kpi.Services.Count += i.Quantity;
                                kpi.Services.RevenueUAH += revUAH;
                                kpi.Services.ProfitUSD += profUSD;
                                break;
                        }

                        kpi.Details.Add(new ManagerSaleItemDetailDto
                        {
                            SaleId = sale.Id,
                            Date = sale.SaleDate,
                            ItemName = i.ProductName,
                            Category = i.ItemType,
                            Quantity = i.Quantity,
                            RevenueUAH = revUAH,
                            ProfitUSD = profUSD
                        });

                        if (i.WarrantyPrice > 0)
                        {
                            kpi.Warranties.Count += i.Quantity;
                            kpi.Warranties.RevenueUAH += i.WarrantyPrice;
                            kpi.Warranties.ProfitUSD += (i.WarrantyProfit / course);

                            kpi.Details.Add(new ManagerSaleItemDetailDto
                            {
                                SaleId = sale.Id,
                                Date = sale.SaleDate,
                                ItemName = $"Гарантія: {i.ProductName}",
                                Category = "Warranty",
                                Quantity = i.Quantity,
                                RevenueUAH = i.WarrantyPrice,
                                ProfitUSD = (i.WarrantyProfit / course)
                            });
                        }
                    }
                }

                kpiResult.Add(kpi);
            }
            return Ok(kpiResult);
        }

        [HttpGet("marketing-stats")]
        public async Task<IActionResult> GetMarketingStats([FromQuery] DateTime start, [FromQuery] DateTime end, [FromQuery] string source, [FromQuery] int storeId = 0, [FromQuery] string deliveryType = "all")
        {
            var targetEnd = end.Date.AddDays(1);

            var query = _context.Sales
                .AsNoTracking()
                .Include(s => s.SaleItems)
                .Where(s => s.SaleDate >= start.Date && s.SaleDate < targetEnd);

            if (storeId > 0) query = query.Where(s => s.StoreId == storeId);

            var rawData = await query
                .Select(s => new
                {
                    s.Id,
                    s.SaleDate,
                    s.Source,
                    s.FinalAmount,
                    s.ExchangeRate,
                    s.TrackingNumber,
                    s.Status,
                    Items = s.SaleItems.Select(i => new
                    {
                        i.ProductName,
                        i.Identifier,
                        i.ItemType,
                        i.Quantity,
                        i.SoldSellingPrice,
                        i.WarrantyPrice,
                        i.WarrantyProfit,
                        i.SellingPriceUSD,
                        i.ProfitUSD
                    }).ToList()
                })
                .ToListAsync();

            var filteredData = rawData.AsEnumerable();
            filteredData = filteredData.Where(s => s.Status != "Return" && s.Status != "Returned");

            if (!string.IsNullOrEmpty(source) && source != "All")
            {
                filteredData = filteredData.Where(s => string.Equals(s.Source?.Trim(), source.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (deliveryType == "offline")
            {
                filteredData = filteredData.Where(s => string.IsNullOrWhiteSpace(s.TrackingNumber) && s.Status != "Shipped");
            }
            else if (deliveryType == "shipping")
            {
                filteredData = filteredData.Where(s => !string.IsNullOrWhiteSpace(s.TrackingNumber) || s.Status == "Shipped");
            }

            var finalList = filteredData.ToList();

            decimal totalProfitUSD = 0;
            decimal totalRevenueUSD = 0;

            int devC = 0, accC = 0, repC = 0, srvC = 0, warC = 0;
            decimal devR = 0, accR = 0, repR = 0, srvR = 0, warR = 0;
            decimal devP = 0, accP = 0, repP = 0, srvP = 0, warP = 0;

            var detailedSales = new List<MarketingSaleDto>();

            foreach (var sale in finalList)
            {
                decimal course = sale.ExchangeRate > 10 ? sale.ExchangeRate : 41.5m;

                totalProfitUSD += sale.Items.Sum(i => i.ProfitUSD + (i.WarrantyProfit / course));
                totalRevenueUSD += sale.Items.Sum(i => (i.SellingPriceUSD * i.Quantity) + (i.WarrantyPrice / course));

                var dItems = sale.Items.Where(i => i.ItemType == "Device").ToList();
                devC += dItems.Sum(i => i.Quantity);
                devR += dItems.Sum(i => (i.SoldSellingPrice * i.Quantity) - i.WarrantyPrice);
                devP += dItems.Sum(i => i.ProfitUSD);

                var aItems = sale.Items.Where(i => i.ItemType == "Accessory").ToList();
                accC += aItems.Sum(i => i.Quantity);
                accR += aItems.Sum(i => (i.SoldSellingPrice * i.Quantity) - i.WarrantyPrice);
                accP += aItems.Sum(i => i.ProfitUSD);

                var rItems = sale.Items.Where(i => i.ItemType == "Service").ToList();
                repC += rItems.Sum(i => i.Quantity);
                repR += rItems.Sum(i => (i.SoldSellingPrice * i.Quantity) - i.WarrantyPrice);
                repP += rItems.Sum(i => i.ProfitUSD);

                var sItems = sale.Items.Where(i => i.ItemType == "PosService").ToList();
                srvC += sItems.Sum(i => i.Quantity);
                srvR += sItems.Sum(i => (i.SoldSellingPrice * i.Quantity) - i.WarrantyPrice);
                srvP += sItems.Sum(i => i.ProfitUSD);

                var wItems = sale.Items.Where(i => i.WarrantyPrice > 0).ToList();
                warC += wItems.Count;
                warR += wItems.Sum(i => i.WarrantyPrice);
                warP += wItems.Sum(i => i.WarrantyProfit / course);

                var itemsList = sale.Items.Select(i => new MarketingSaleItemDto
                {
                    ModelName = string.IsNullOrWhiteSpace(i.Identifier) ? i.ProductName : $"{i.ProductName} ({i.Identifier})",
                    Type = i.ItemType,
                    Quantity = i.Quantity,
                    RevenueUAH = i.SoldSellingPrice * i.Quantity,
                    ProfitUSD = i.ProfitUSD,
                    WarrantyPriceUAH = i.WarrantyPrice,
                    WarrantyProfitUSD = i.WarrantyProfit / course
                }).ToList();

                detailedSales.Add(new MarketingSaleDto
                {
                    SaleId = sale.Id,
                    Date = sale.SaleDate,
                    FinalAmount = sale.FinalAmount,
                    TotalProfitUSD = sale.Items.Sum(i => i.ProfitUSD + (i.WarrantyProfit / course)),
                    Items = itemsList
                });
            }

            var stats = new MarketingStatsDto
            {
                SalesCount = finalList.Count,
                TotalRevenueUAH = finalList.Sum(s => s.FinalAmount),
                TotalProfitUSD = totalProfitUSD,
                TotalRevenueUSD = totalRevenueUSD,
                Devices = new MarketingCategoryStats { Count = devC, RevenueUAH = devR, ProfitUSD = devP },
                Accessories = new MarketingCategoryStats { Count = accC, RevenueUAH = accR, ProfitUSD = accP },
                Repairs = new MarketingCategoryStats { Count = repC, RevenueUAH = repR, ProfitUSD = repP },
                Services = new MarketingCategoryStats { Count = srvC, RevenueUAH = srvR, ProfitUSD = srvP },
                Warranties = new MarketingCategoryStats { Count = warC, RevenueUAH = warR, ProfitUSD = warP },
                SalesDetails = detailedSales
            };

            return Ok(stats);
        }

        [HttpGet("salary-rules")]
        public async Task<IActionResult> GetSalaryRules()
        {
            var users = await _userManager.Users.ToListAsync();
            var rules = await _context.EmployeeSalaryRules.AsNoTracking().ToListAsync();
            var stores = await _context.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);

            var result = new List<SalaryRuleDto>();

            foreach (var u in users)
            {
                // Не показувати заблокованих працівників у правилах ЗП
                if (u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow) continue;

                var roles = await _userManager.GetRolesAsync(u);
                var rule = rules.FirstOrDefault(r => r.UserId == u.Id);

                result.Add(new SalaryRuleDto
                {
                    UserId = u.Id,
                    EmployeeName = u.FullName ?? u.UserName ?? "Невідомий",
                    Role = roles.FirstOrDefault() ?? "Manager",
                    StoreName = u.StoreId.HasValue && stores.ContainsKey(u.StoreId.Value) ? stores[u.StoreId.Value] : "Не призначено",

                    MonthlyBaseSalary = rule?.MonthlyBaseSalary ?? 0,
                    PenaltyPerLateness = rule?.PenaltyPerLateness ?? 0,
                    BonusPerDevice = rule?.BonusPerDevice ?? 0,
                    BonusPerWarranty = rule?.BonusPerWarranty ?? 0,
                    AccessoryBonusPercent = rule?.AccessoryBonusPercent ?? 0,
                    ServiceBonusPercent = rule?.ServiceBonusPercent ?? 0
                });
            }

            return Ok(result.OrderBy(r => r.StoreName).ThenBy(r => r.EmployeeName));
        }

        [HttpPost("salary-rules/update")]
        public async Task<IActionResult> UpdateSalaryRule([FromBody] SalaryRuleDto dto)
        {
            if (string.IsNullOrEmpty(dto.UserId)) return BadRequest("UserId не може бути порожнім");

            var rule = await _context.EmployeeSalaryRules.FirstOrDefaultAsync(r => r.UserId == dto.UserId);

            if (rule == null)
            {
                rule = new EmployeeSalaryRule { UserId = dto.UserId };
                _context.EmployeeSalaryRules.Add(rule);
            }

            rule.MonthlyBaseSalary = dto.MonthlyBaseSalary;
            rule.PenaltyPerLateness = dto.PenaltyPerLateness;
            rule.BonusPerDevice = dto.BonusPerDevice;
            rule.BonusPerWarranty = dto.BonusPerWarranty;
            rule.AccessoryBonusPercent = dto.AccessoryBonusPercent;
            rule.ServiceBonusPercent = dto.ServiceBonusPercent;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("payroll")]
        public async Task<IActionResult> GetPayroll([FromQuery] int month, [FromQuery] int year, [FromQuery] int? storeId)
        {
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1);

            var savedRecords = await _context.PayrollRecords.Where(p => p.Month == month && p.Year == year).ToListAsync();
            var rules = await _context.EmployeeSalaryRules.ToDictionaryAsync(r => r.UserId);
            var attendances = await _context.EmployeeAttendances.Where(a => a.WorkDate >= start && a.WorkDate < end).ToListAsync();
            var sales = await _context.Sales.Include(s => s.SaleItems).Where(s => s.SaleDate >= start && s.SaleDate < end).ToListAsync();
            var users = await _userManager.Users.ToListAsync();
            var stores = await _context.Stores.ToDictionaryAsync(s => s.Id, s => s.Name);

            var result = new List<PayrollDto>();
            int normDays = 23;

            foreach (var user in users)
            {
                if (storeId.HasValue && storeId > 0 && user.StoreId != storeId) continue;

                var saved = savedRecords.FirstOrDefault(r => r.UserId == user.Id);

                var userAttendances = attendances.Where(a =>
                    a.UserId == user.Id ||
                    (!string.IsNullOrEmpty(a.UserName) && (a.UserName == user.UserName || a.UserName == user.Email || a.UserName == user.FullName))
                ).ToList();

                var userSales = sales.Where(s => s.UserId == user.Id).ToList();

                // Якщо людина заблокована і не має активності в цьому місяці - не показуємо її у ЗП
                bool isBlocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
                if (isBlocked && saved == null && userAttendances.Count == 0 && userSales.Count == 0) continue;

                int workingDays = userAttendances.Count;
                TimeSpan allowedClockInTime;

                if (user.StoreId == 1) allowedClockInTime = new TimeSpan(8, 50, 0);
                else if (user.StoreId == 2) allowedClockInTime = new TimeSpan(9, 50, 0);
                else allowedClockInTime = new TimeSpan(10, 0, 0);

                int latenesses = userAttendances.Count(a =>
                    a.ClockInTime.HasValue &&
                    a.ClockInTime.Value.TimeOfDay > allowedClockInTime
                );
                int devicesSold = 0, warrantiesSold = 0;
                decimal accRev = 0, srvRev = 0;

                foreach (var sale in userSales)
                {
                    foreach (var item in sale.SaleItems)
                    {
                        if (item.ItemType == "Device") devicesSold += item.Quantity;
                        if (item.ItemType == "Accessory") accRev += item.SoldSellingPrice * item.Quantity;
                        if (item.ItemType == "Service" || item.ItemType == "PosService") srvRev += item.SoldSellingPrice * item.Quantity;
                        if (item.WarrantyPrice > 0) warrantiesSold += item.Quantity;
                    }
                }

                var dto = new PayrollDto
                {
                    UserId = user.Id,
                    EmployeeName = user.FullName ?? user.UserName ?? "Невідомий",
                    StoreId = user.StoreId ?? 0,
                    StoreName = user.StoreId.HasValue && stores.ContainsKey(user.StoreId.Value) ? stores[user.StoreId.Value] : "Не призначено",

                    RecordId = saved?.Id,
                    WorkingDays = saved?.WorkingDays ?? workingDays,
                    Latenesses = saved?.Latenesses ?? latenesses,
                    DevicesSold = saved?.DevicesSold ?? devicesSold,
                    WarrantiesSold = saved?.WarrantiesSold ?? warrantiesSold,
                    AccessoriesRevenueUAH = saved?.AccessoriesRevenueUAH ?? accRev,
                    ServicesRevenueUAH = saved?.ServicesRevenueUAH ?? srvRev,
                    AdvancePayment = saved?.AdvancePayment ?? 0
                };

                if (rules.TryGetValue(user.Id, out var rule))
                {
                    dto.RuleMonthlyRate = rule.MonthlyBaseSalary;
                    dto.RulePenalty = rule.PenaltyPerLateness;
                    dto.RuleDeviceBonus = rule.BonusPerDevice;
                    dto.RuleWarrantyBonus = rule.BonusPerWarranty;
                    dto.RuleAccPercent = rule.AccessoryBonusPercent;
                    dto.RuleSrvPercent = rule.ServiceBonusPercent;
                }

                if (saved != null)
                {
                    dto.BaseSalaryCalculated = saved.BaseSalaryCalculated;
                    dto.FinalPayout = saved.FinalPayout;
                }
                else
                {
                    dto.BaseSalaryAmount = Math.Round((dto.RuleMonthlyRate / normDays) * dto.WorkingDays, 2);

                    dto.DevicesBonusAmount = dto.DevicesSold * dto.RuleDeviceBonus;
                    dto.WarrantiesBonusAmount = dto.WarrantiesSold * dto.RuleWarrantyBonus;
                    dto.AccessoriesBonusAmount = Math.Round(dto.AccessoriesRevenueUAH * (dto.RuleAccPercent / 100m), 2);
                    dto.ServicesBonusAmount = Math.Round(dto.ServicesRevenueUAH * (dto.RuleSrvPercent / 100m), 2);
                    dto.LatenessPenaltyAmount = dto.Latenesses * dto.RulePenalty;

                    dto.BaseSalaryCalculated = dto.BaseSalaryAmount + dto.DevicesBonusAmount + dto.WarrantiesBonusAmount + dto.AccessoriesBonusAmount + dto.ServicesBonusAmount - dto.LatenessPenaltyAmount;
                    dto.FinalPayout = dto.BaseSalaryCalculated - dto.AdvancePayment;
                }

                result.Add(dto);
            }

            return Ok(result.OrderBy(r => r.StoreName).ThenBy(r => r.EmployeeName));
        }

        [HttpPost("payroll/save")]
        public async Task<IActionResult> SavePayroll([FromBody] List<PayrollDto> records, [FromQuery] int month, [FromQuery] int year)
        {
            foreach (var dto in records)
            {
                PayrollRecord? record = null;

                if (dto.RecordId.HasValue && dto.RecordId > 0)
                {
                    record = await _context.PayrollRecords.FindAsync(dto.RecordId.GetValueOrDefault());
                    if (record == null) continue;
                }
                else
                {
                    record = await _context.PayrollRecords.FirstOrDefaultAsync(p => p.UserId == dto.UserId && p.Month == month && p.Year == year);

                    if (record == null)
                    {
                        record = new PayrollRecord
                        {
                            UserId = dto.UserId,
                            Month = month,
                            Year = year
                        };
                        _context.PayrollRecords.Add(record);
                    }
                }

                record.EmployeeName = dto.EmployeeName;
                record.StoreId = dto.StoreId;
                record.StoreName = dto.StoreName;
                record.WorkingDays = dto.WorkingDays;
                record.Latenesses = dto.Latenesses;
                record.DevicesSold = dto.DevicesSold;
                record.WarrantiesSold = dto.WarrantiesSold;
                record.AccessoriesRevenueUAH = dto.AccessoriesRevenueUAH;
                record.ServicesRevenueUAH = dto.ServicesRevenueUAH;
                record.BaseSalaryCalculated = dto.BaseSalaryCalculated;
                record.AdvancePayment = dto.AdvancePayment;
                record.FinalPayout = dto.BaseSalaryCalculated - dto.AdvancePayment;
                record.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("generate-timesheet")]
        public IActionResult GenerateTimesheetPdf([FromBody] TimesheetFormDto data)
        {
            try
            {
                byte[] pdfBytes = _pdfService.GenerateTimesheetPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.ToString());
            }
        }

        [HttpPost("archive")]
        public async Task<IActionResult> SaveToArchive([FromBody] TimesheetFormDto data)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(data.FullName) || string.IsNullOrWhiteSpace(data.Month))
                    return BadRequest("ПІБ та Місяць є обов'язковими для збереження!");

                var archiveRecord = new TimesheetArchive
                {
                    FullName = data.FullName,
                    Month = data.Month,
                    WorkingDays = data.WorkingDays,
                    Latenesses = data.Latenesses,
                    IphonesSold = data.IphonesSold,
                    OtherAppleSold = data.OtherAppleSold,
                    WarrantiesSold = data.WarrantiesSold,
                    AccessoriesRevenueUAH = data.AccessoriesRevenueUAH,
                    ServicesSold = data.ServicesSold,
                    BaseSalaryAmount = data.BaseSalaryAmount,
                    IphonesBonusAmount = data.IphonesBonusAmount,
                    OtherAppleBonusAmount = data.OtherAppleBonusAmount,
                    WarrantiesBonusAmount = data.WarrantiesBonusAmount,
                    AccessoriesBonusAmount = data.AccessoriesBonusAmount,
                    ServicesBonusAmount = data.ServicesBonusAmount,
                    BonusAmount = data.BonusAmount,
                    PenaltyAmount = data.PenaltyAmount,
                    AdvancePayment = data.AdvancePayment,
                    TotalAccrued = data.TotalAccrued,
                    FinalPayout = data.FinalPayout,
                    CreatedAt = AppleShopikCRM.Helpers.TimeHelper.GetUkraineTime()
                };

                _context.TimesheetArchives.Add(archiveRecord);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Успішно збережено в архів" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Помилка сервера при збереженні: {ex.Message}");
            }
        }

        [HttpGet("archive")]
        public async Task<IActionResult> GetTimesheetArchives()
        {
            try
            {
                var archives = await _context.TimesheetArchives
                    .AsNoTracking()
                    .OrderByDescending(a => a.CreatedAt)
                    .ToListAsync();

                return Ok(archives);
            }
            catch (Exception ex)
            {
                return BadRequest($"Помилка БД: {ex.Message}");
            }
        }
    }
    public class FixCostRequest { public int DeviceId { get; set; } public decimal RealPurchasePrice { get; set; } }
    public class FixRepairItemCostRequest { public int TicketId { get; set; } public int ItemId { get; set; } public decimal RealCostPart { get; set; } public int MasterId { get; set; } }
    public class ProblemDeviceDto
    {
        public int Id { get; set; }
        public string ModelName { get; set; } = ""; public string Identifier { get; set; } = ""; public bool IsSold { get; set; }
        public string? Color { get; set; }
        public string? Capacity { get; set; }
        public string? ConditionStatus { get; set; }
        public int BatteryHealth { get; set; }
        public decimal TempPurchasePrice { get; set; }
        public decimal TempRepairCost { get; set; }
    }
    public class ConsignmentDebtDto { public int DeviceId { get; set; } public string SupplierName { get; set; } = ""; public string ModelName { get; set; } = ""; public string Identifier { get; set; } = ""; public decimal PurchasePrice { get; set; } public decimal PaidAmount { get; set; } public decimal DebtUSD { get; set; } public bool IsSold { get; set; } }
    public class ProblemRepairDto { public int Id { get; set; } public string TicketNumber { get; set; } = ""; public string DeviceModel { get; set; } = ""; public string IssueDescription { get; set; } = ""; public string ResolutionReport { get; set; } = ""; public string Identifier { get; set; } = ""; public string Status { get; set; } = ""; public decimal Price { get; set; } public bool IsStockDevice { get; set; } public int StockDeviceIsSold { get; set; } public List<RepairItemDto> Items { get; set; } = new(); }
    public class RepairItemDto { public int Id { get; set; } public string Name { get; set; } = ""; public decimal Cost { get; set; } public decimal Price { get; set; } public decimal TempCost { get; set; } public int MasterId { get; set; }  }
    public class ManagerCategoryStatsDto { public int Count { get; set; } public decimal RevenueUAH { get; set; } public decimal ProfitUSD { get; set; } }
    public class ManagerSaleItemDetailDto { public int SaleId { get; set; } public DateTime Date { get; set; } public string ItemName { get; set; } = ""; public string Category { get; set; } = ""; public int Quantity { get; set; } public decimal RevenueUAH { get; set; } public decimal ProfitUSD { get; set; } }
    public class ManagerKpiDto { public string UserName { get; set; } = ""; public int ItemsSold { get; set; } public decimal TotalRevenueUAH { get; set; } public decimal TotalProfitUSD { get; set; } public ManagerCategoryStatsDto Devices { get; set; } = new(); public ManagerCategoryStatsDto Accessories { get; set; } = new(); public ManagerCategoryStatsDto Repairs { get; set; } = new(); public ManagerCategoryStatsDto Services { get; set; } = new(); public ManagerCategoryStatsDto Warranties { get; set; } = new(); public List<ManagerSaleItemDetailDto> Details { get; set; } = new(); }
    public class SalaryRuleDto { public string UserId { get; set; } = ""; public string EmployeeName { get; set; } = ""; public string Role { get; set; } = ""; public string StoreName { get; set; } = ""; public decimal MonthlyBaseSalary { get; set; } public decimal PenaltyPerLateness { get; set; } public decimal BonusPerDevice { get; set; } public decimal BonusPerWarranty { get; set; } public decimal AccessoryBonusPercent { get; set; } public decimal ServiceBonusPercent { get; set; } }
    public class PayrollDto { public int? RecordId { get; set; } public string UserId { get; set; } = ""; public string EmployeeName { get; set; } = ""; public int StoreId { get; set; } public string StoreName { get; set; } = ""; public int WorkingDays { get; set; } public int Latenesses { get; set; } public int DevicesSold { get; set; } public int WarrantiesSold { get; set; } public decimal AccessoriesRevenueUAH { get; set; } public decimal ServicesRevenueUAH { get; set; } public decimal BaseSalaryAmount { get; set; } public decimal DevicesBonusAmount { get; set; } public decimal WarrantiesBonusAmount { get; set; } public decimal AccessoriesBonusAmount { get; set; } public decimal ServicesBonusAmount { get; set; } public decimal LatenessPenaltyAmount { get; set; } public decimal RuleMonthlyRate { get; set; } public decimal RulePenalty { get; set; } public decimal RuleDeviceBonus { get; set; } public decimal RuleWarrantyBonus { get; set; } public decimal RuleAccPercent { get; set; } public decimal RuleSrvPercent { get; set; } public decimal BaseSalaryCalculated { get; set; } public decimal AdvancePayment { get; set; } public decimal FinalPayout { get; set; } public bool IsSaved => RecordId.HasValue; }
    public class MarketingCategoryStats { public int Count { get; set; } public decimal RevenueUAH { get; set; } public decimal ProfitUSD { get; set; } }
    public class MarketingSaleItemDto { public string ModelName { get; set; } = ""; public string Type { get; set; } = ""; public int Quantity { get; set; } public decimal RevenueUAH { get; set; } public decimal ProfitUSD { get; set; } public decimal WarrantyPriceUAH { get; set; } public decimal WarrantyProfitUSD { get; set; } }
    public class MarketingSaleDto { public int SaleId { get; set; } public DateTime Date { get; set; } public decimal FinalAmount { get; set; } public decimal TotalProfitUSD { get; set; } public List<MarketingSaleItemDto> Items { get; set; } = new(); }
    public class MarketingStatsDto { public int SalesCount { get; set; } public decimal TotalRevenueUAH { get; set; } public decimal TotalRevenueUSD { get; set; } public decimal TotalProfitUSD { get; set; } public MarketingCategoryStats Devices { get; set; } = new(); public MarketingCategoryStats Accessories { get; set; } = new(); public MarketingCategoryStats Repairs { get; set; } = new(); public MarketingCategoryStats Services { get; set; } = new(); public MarketingCategoryStats Warranties { get; set; } = new(); public List<MarketingSaleDto> SalesDetails { get; set; } = new(); }
}