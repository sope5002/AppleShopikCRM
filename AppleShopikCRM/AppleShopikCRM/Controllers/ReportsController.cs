using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppleShopikCRM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("sales")]
        public async Task<IActionResult> GetSalesReport([FromQuery] int storeId, [FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var targetEnd = end.Date.AddDays(1);

            bool isAdmin = User.IsInRole("Admin");
            if (!isAdmin)
            {
                var storeClaim = User.FindFirst("StoreId")?.Value;
                if (int.TryParse(storeClaim, out int userStoreId)) storeId = userStoreId;
            }

            var sales = await _context.Sales
                .AsNoTracking()
                .Include(s => s.SaleItems)
                .Include(s => s.Payments)
                .Include(s => s.Customer)
                .Where(s => s.StoreId == storeId && s.SaleDate >= start.Date && s.SaleDate < targetEnd)
                .OrderByDescending(s => s.SaleDate)
                .ToListAsync();

            var userIds = sales.Select(s => s.UserId).Distinct().ToList();
            var usersDict = await _context.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "Невідомий");

            var allPayments = sales.SelectMany(s => s.Payments).ToList();

            var totals = new
            {
                UAH = allPayments.Where(p => p.Method == "Готівка").Sum(p => p.Amount),
                USD = allPayments.Where(p => p.Method == "USD").Sum(p => p.Amount),
                EUR = allPayments.Where(p => p.Method == "EUR").Sum(p => p.Amount),
                Card = allPayments.Where(p => p.Method != "Готівка" && p.Method != "USD" && p.Method != "EUR").Sum(p => p.Amount)
            };

            var itemsList = new List<object>();
            var receiptsList = new List<object>();

            foreach (var sale in sales)
            {
                string cashierName = !string.IsNullOrEmpty(sale.UserId) && usersDict.ContainsKey(sale.UserId)
                                        ? usersDict[sale.UserId] : "Невідомий";

                string paymentMethods = string.Join(", ", sale.Payments.Select(p => p.Method).Distinct());
                var salePayments = sale.Payments.Select(p => new { Amount = p.Amount, Method = p.Method }).ToList();

                receiptsList.Add(new
                {
                    Id = sale.Id,
                    Date = sale.SaleDate,
                    FinalAmount = sale.FinalAmount,
                    CashierName = cashierName,
                    CustomerName = sale.Customer?.Name,
                    CustomerPhone = sale.Customer?.Phone,
                    Payments = salePayments,
                    ItemsCount = sale.SaleItems.Sum(i => i.Quantity),
                    Source = sale.Source,
                    TrackingNumber = sale.TrackingNumber,
                    Items = sale.SaleItems.Select(i => new
                    {
                        Id = i.Id,
                        ModelName = i.ProductName,
                        Type = i.ItemType,
                        Quantity = i.Quantity,
                        SellingPrice = i.SoldSellingPrice,
                        SellingPriceUSD = i.SellingPriceUSD,
                        WarrantyPrice = i.WarrantyPrice
                    }).ToList()
                });

                foreach (var item in sale.SaleItems)
                {
                    itemsList.Add(new
                    {
                        SaleId = sale.Id,
                        Date = sale.SaleDate,
                        ItemName = item.ProductName,
                        ItemType = item.ItemType,
                        Quantity = item.Quantity,
                        Price = item.SoldSellingPrice * item.Quantity,
                        Currency = string.IsNullOrWhiteSpace(paymentMethods) ? "Готівка" : paymentMethods,
                        Cashier = cashierName,
                        Payments = salePayments
                    });
                }
            }

            return Ok(new { Totals = totals, Items = itemsList, Receipts = receiptsList });
        }

        public class DeleteReceiptDto { public bool UpdateCash { get; set; } }

        // 🔥 ПОВНЕ ВИДАЛЕННЯ ЧЕКУ
        [HttpDelete("delete-receipt/{saleId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteReceipt(int saleId, [FromQuery] bool updateCash = false)
        {
            var sale = await _context.Sales.Include(s => s.SaleItems).Include(s => s.Payments).FirstOrDefaultAsync(s => s.Id == saleId);
            if (sale == null) return NotFound("Чек не знайдено");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in sale.SaleItems)
                {
                    if (item.ItemType == "Device" && item.ItemId > 0)
                    {
                        var device = await _context.DeviceStock.FindAsync(item.ItemId);
                        if (device != null) device.IsSold = 0;

                        if (item.WarrantyPrice > 0 && sale.CustomerId.HasValue)
                        {
                            var warranty = await _context.CustomerWarranties.FirstOrDefaultAsync(w => w.SaleId == saleId && w.Imei == item.Identifier);
                            if (warranty != null) _context.CustomerWarranties.Remove(warranty);
                        }
                    }
                    else if (item.ItemType == "Accessory" && item.ItemId > 0)
                    {
                        var accStock = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == item.ItemId && a.StoreId == sale.StoreId);
                        if (accStock != null) accStock.Quantity += item.Quantity;
                    }
                }

                if (updateCash)
                {
                    var store = await _context.Stores.FindAsync(sale.StoreId);
                    var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == sale.StoreId && s.ShiftDate == sale.SaleDate.Date);

                    if (shift != null)
                    {
                        foreach (var pay in sale.Payments)
                        {
                            if (pay.Method == "Готівка") { shift.ExpenseUAH += pay.Amount; if (store != null) store.BalanceUAH -= pay.Amount; }
                            else if (pay.Method == "USD") { shift.ExpenseUSD += pay.Amount; if (store != null) store.BalanceUSD -= pay.Amount; }
                            else if (pay.Method == "EUR") { shift.ExpenseEUR += pay.Amount; if (store != null) store.BalanceEUR -= pay.Amount; }
                            else { if (store != null) store.BalanceCard -= pay.Amount; }

                            _context.CashTransactions.Add(new CashTransaction
                            {
                                StoreId = sale.StoreId,
                                ShiftId = shift.Id,
                                Amount = pay.Amount,
                                Currency = pay.Method,
                                Comment = $"Повернення коштів за видалений чек #{sale.Id}",
                                Type = "Expense",
                                Category = "Refund",
                                Date = TimeHelper.GetUkraineTime()
                            });
                        }
                    }
                }

                var existingTransactions = await _context.CashTransactions.Where(t => t.SaleId == saleId).ToListAsync();
                foreach (var t in existingTransactions)
                {
                    t.SaleId = null;
                    t.Comment += " (Чек скасовано)";
                }

                _context.SalePayments.RemoveRange(sale.Payments);
                _context.SaleItems.RemoveRange(sale.SaleItems);
                _context.Sales.Remove(sale);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        public class RemoveItemDto { public bool UpdateCash { get; set; } }

        // 🔥 ВИЛУЧЕННЯ ТОВАРУ З ЧЕКУ
        [HttpPost("remove-item/{saleItemId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RemoveItemFromReceipt(int saleItemId, [FromBody] RemoveItemDto req)
        {
            var item = await _context.SaleItems.Include(i => i.Sale).ThenInclude(s => s!.Payments).FirstOrDefaultAsync(i => i.Id == saleItemId);
            if (item == null || item.Sale == null) return NotFound("Товар не знайдено");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sale = item.Sale;

                if (item.ItemType == "Device" && item.ItemId > 0)
                {
                    var device = await _context.DeviceStock.FindAsync(item.ItemId);
                    if (device != null) device.IsSold = 0;

                    if (item.WarrantyPrice > 0 && sale.CustomerId.HasValue)
                    {
                        var warranty = await _context.CustomerWarranties.FirstOrDefaultAsync(w => w.SaleId == sale.Id && w.Imei == item.Identifier);
                        if (warranty != null) _context.CustomerWarranties.Remove(warranty);
                    }
                }
                else if (item.ItemType == "Accessory" && item.ItemId > 0)
                {
                    var accStock = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == item.ItemId && a.StoreId == sale.StoreId);
                    if (accStock != null) accStock.Quantity += item.Quantity;
                }

                decimal itemTotalValue = (item.SoldSellingPrice * item.Quantity) + item.WarrantyPrice;

                var mainPayment = sale.Payments.FirstOrDefault(p => p.Method == "Готівка") ?? sale.Payments.FirstOrDefault();
                if (mainPayment != null)
                {
                    mainPayment.Amount -= itemTotalValue;
                    if (mainPayment.Amount <= 0) _context.SalePayments.Remove(mainPayment);
                }

                if (req.UpdateCash)
                {
                    var store = await _context.Stores.FindAsync(sale.StoreId);
                    var today = DateTime.Today;
                    var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == sale.StoreId && s.ShiftDate == today);

                    bool isNewShift = false;
                    if (shift == null)
                    {
                        shift = new CashShift { StoreId = sale.StoreId, ShiftDate = today };
                        isNewShift = true;
                    }

                    if (mainPayment?.Method == "Готівка") { shift.ExpenseUAH += itemTotalValue; if (store != null) store.BalanceUAH -= itemTotalValue; }
                    else if (mainPayment?.Method == "USD") { shift.ExpenseUSD += itemTotalValue; if (store != null) store.BalanceUSD -= itemTotalValue; }
                    else if (mainPayment?.Method == "EUR") { shift.ExpenseEUR += itemTotalValue; if (store != null) store.BalanceEUR -= itemTotalValue; }
                    else { if (store != null) store.BalanceCard -= itemTotalValue; }

                    if (isNewShift) _context.CashShifts.Add(shift);
                    else _context.CashShifts.Update(shift);

                    if (store != null) _context.Stores.Update(store);

                    _context.CashTransactions.Add(new CashTransaction
                    {
                        StoreId = sale.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Amount = itemTotalValue,
                        Currency = mainPayment?.Method ?? "UAH",
                        Comment = $"Вилучення товару '{item.ProductName}' з чеку #{sale.Id} (Повернення коштів)",
                        Type = "Expense",
                        Category = "Refund",
                        Date = TimeHelper.GetUkraineTime()
                    });
                }

                sale.FinalAmount -= itemTotalValue;
                if (sale.FinalAmount < 0) sale.FinalAmount = 0;

                sale.TotalAmount -= itemTotalValue;
                if (sale.TotalAmount < 0) sale.TotalAmount = 0;

                sale.TotalProfit -= item.PositionProfit;

                _context.SaleItems.Remove(item);

                var remainingItems = await _context.SaleItems.Where(i => i.SaleId == sale.Id && i.Id != item.Id).CountAsync();
                if (remainingItems == 0)
                {
                    var existingTransactions = await _context.CashTransactions.Where(t => t.SaleId == sale.Id).ToListAsync();
                    foreach (var t in existingTransactions)
                    {
                        t.SaleId = null;
                        t.Comment += " (Чек спорожнів і видалений)";
                    }

                    _context.SalePayments.RemoveRange(sale.Payments);
                    _context.Sales.Remove(sale);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        public class UpdatePriceDto { public int SaleId { get; set; } public int SaleItemId { get; set; } public decimal NewSellingPrice { get; set; } public bool UpdateCash { get; set; } }

        // 🔥 ЗМІНА ЦІНИ ТОВАРУ
        [HttpPost("update-item-price")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateItemPrice([FromBody] UpdatePriceDto req)
        {
            if (req.NewSellingPrice < 0) return BadRequest("Ціна не може бути від'ємною");

            var sale = await _context.Sales.Include(s => s.SaleItems).Include(s => s.Payments).FirstOrDefaultAsync(s => s.Id == req.SaleId);
            if (sale == null) return NotFound("Чек не знайдено");

            var item = sale.SaleItems.FirstOrDefault(i => i.Id == req.SaleItemId);
            if (item == null) return NotFound("Товар у чеку не знайдено");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal oldPriceTotal = item.SoldSellingPrice * item.Quantity;
                decimal newPriceTotal = req.NewSellingPrice * item.Quantity;
                decimal difference = newPriceTotal - oldPriceTotal;

                item.SoldSellingPrice = req.NewSellingPrice;
                decimal course = sale.ExchangeRate > 10 ? sale.ExchangeRate : 41.5m;

                item.SellingPriceUSD = Math.Round(req.NewSellingPrice / course, 2);
                item.ProfitUSD = Math.Round((req.NewSellingPrice / course) - item.PurchasePriceUSD, 2);

                item.PositionProfit = (req.NewSellingPrice * item.Quantity) - item.SoldPurchasePrice + item.WarrantyPrice;

                sale.FinalAmount += difference;
                sale.TotalAmount += difference;
                sale.TotalProfit = sale.SaleItems.Sum(i => i.PositionProfit);

                var mainPayment = sale.Payments.FirstOrDefault(p => p.Method == "Готівка") ?? sale.Payments.FirstOrDefault();
                if (mainPayment != null) mainPayment.Amount += difference;

                if (req.UpdateCash && difference != 0)
                {
                    var store = await _context.Stores.FindAsync(sale.StoreId);
                    var today = DateTime.Today;
                    var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == sale.StoreId && s.ShiftDate == today);

                    bool isNewShift = false;
                    if (shift == null)
                    {
                        shift = new CashShift { StoreId = sale.StoreId, ShiftDate = today };
                        isNewShift = true;
                    }

                    if (difference > 0)
                    {
                        if (mainPayment?.Method == "Готівка") { shift.IncomeUAH += difference; if (store != null) store.BalanceUAH += difference; }
                        else if (mainPayment?.Method == "USD") { shift.IncomeUSD += difference; if (store != null) store.BalanceUSD += difference; }
                        else if (mainPayment?.Method == "EUR") { shift.IncomeEUR += difference; if (store != null) store.BalanceEUR += difference; }
                        else { shift.IncomeCard += difference; if (store != null) store.BalanceCard += difference; }
                    }
                    else
                    {
                        decimal absDiff = Math.Abs(difference);
                        if (mainPayment?.Method == "Готівка") { shift.ExpenseUAH += absDiff; if (store != null) store.BalanceUAH -= absDiff; }
                        else if (mainPayment?.Method == "USD") { shift.ExpenseUSD += absDiff; if (store != null) store.BalanceUSD -= absDiff; }
                        else if (mainPayment?.Method == "EUR") { shift.ExpenseEUR += absDiff; if (store != null) store.BalanceEUR -= absDiff; }
                        else { if (store != null) store.BalanceCard -= absDiff; }
                    }

                    if (isNewShift) _context.CashShifts.Add(shift);
                    else _context.CashShifts.Update(shift);

                    if (store != null) _context.Stores.Update(store);

                    _context.CashTransactions.Add(new CashTransaction
                    {
                        StoreId = sale.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Amount = Math.Abs(difference),
                        Currency = mainPayment?.Method ?? "UAH",
                        Comment = $"Коригування ціни товару в чеку #{sale.Id}",
                        Type = difference > 0 ? "Income" : "Expense",
                        Category = "Adjustment",
                        Date = TimeHelper.GetUkraineTime()
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        public class SwapItemDto { public int SaleId { get; set; } public int SaleItemId { get; set; } public string ItemType { get; set; } = ""; public int NewStockId { get; set; } public bool UpdateCash { get; set; } }

        // 🔥 ЗАМІНА ТОВАРУ (ВИПРАВЛЕНО ЦІНУ ЗАКУПКИ)
        [HttpPost("swap-item")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SwapItem([FromBody] SwapItemDto req)
        {
            var sale = await _context.Sales.Include(s => s.SaleItems).Include(s => s.Payments).FirstOrDefaultAsync(s => s.Id == req.SaleId);
            if (sale == null) return NotFound("Чек не знайдено");

            var item = sale.SaleItems.FirstOrDefault(i => i.Id == req.SaleItemId);
            if (item == null || item.ItemType != req.ItemType) return BadRequest("Некоректний тип товару для заміни");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal newSellingPrice = 0;
                decimal newPurchasePriceUSD = 0;
                decimal newPurchasePriceUAH = 0;
                string newProductName = "";
                int newItemId = 0;

                decimal course = sale.ExchangeRate > 10 ? sale.ExchangeRate : 41.5m;

                if (req.ItemType == "Accessory")
                {
                    if (item.ItemId > 0)
                    {
                        var oldStock = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == item.ItemId && a.StoreId == sale.StoreId);
                        if (oldStock != null) oldStock.Quantity += item.Quantity;
                    }

                    var newStock = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == req.NewStockId && a.StoreId == sale.StoreId);
                    if (newStock == null) return BadRequest("На складі немає нового товару");

                    var prodNameFromDb = await _context.Products.Where(p => p.Id == newStock.ProductId).Select(p => p.Name).FirstOrDefaultAsync();

                    newStock.Quantity -= item.Quantity;
                    newSellingPrice = newStock.SellingPrice;

                    // Аксесуари зберігаються в UAH
                    newPurchasePriceUAH = newStock.PurchasePrice;
                    newPurchasePriceUSD = Math.Round(newPurchasePriceUAH / course, 2);

                    newProductName = prodNameFromDb ?? newStock.ProductName ?? "Аксесуар";
                    newItemId = newStock.ProductId;
                }
                else if (req.ItemType == "Device")
                {
                    if (item.Quantity > 1) return BadRequest("Неможливо замінити відразу декілька пристроїв");
                    if (item.ItemId > 0)
                    {
                        var oldDevice = await _context.DeviceStock.FindAsync(item.ItemId);
                        if (oldDevice != null) oldDevice.IsSold = 0;
                    }

                    var newDevice = await _context.DeviceStock.FirstOrDefaultAsync(d => d.Id == req.NewStockId && d.StoreId == sale.StoreId && d.IsSold == 0);
                    if (newDevice == null) return BadRequest("Пристрою немає на складі");

                    newDevice.IsSold = 1;
                    newSellingPrice = newDevice.SellingPrice;

                    // Техніка зберігається в USD
                    newPurchasePriceUSD = newDevice.PurchasePrice + newDevice.RepairCost;
                    newPurchasePriceUAH = newPurchasePriceUSD * course;

                    newProductName = $"{newDevice.ModelName} {newDevice.Capacity} {newDevice.Color}".Trim();
                    newItemId = newDevice.Id;
                }
                else
                {
                    return BadRequest("Підтримується заміна лише техніки та аксесуарів");
                }

                decimal oldPriceTotal = item.SoldSellingPrice * item.Quantity;
                decimal newPriceTotal = newSellingPrice * item.Quantity;
                decimal difference = newPriceTotal - oldPriceTotal;

                item.ItemId = newItemId;
                item.ProductName = newProductName;
                item.SoldSellingPrice = newSellingPrice;

                // 🔥 Коректні дані про закупку
                item.SoldPurchasePrice = newPurchasePriceUAH;
                item.PurchasePriceUSD = newPurchasePriceUSD;

                item.SellingPriceUSD = Math.Round(newSellingPrice / course, 2);
                item.ProfitUSD = Math.Round(item.SellingPriceUSD - newPurchasePriceUSD, 2);
                item.PositionProfit = (newSellingPrice * item.Quantity) - (newPurchasePriceUAH * item.Quantity) + item.WarrantyPrice;

                sale.FinalAmount += difference;
                sale.TotalAmount += difference;
                sale.TotalProfit = sale.SaleItems.Sum(i => i.PositionProfit);

                var mainPayment = sale.Payments.FirstOrDefault(p => p.Method == "Готівка") ?? sale.Payments.FirstOrDefault();
                if (mainPayment != null) mainPayment.Amount += difference;

                // 🔥 Коригуємо касу
                if (req.UpdateCash && difference != 0)
                {
                    var store = await _context.Stores.FindAsync(sale.StoreId);
                    var today = DateTime.Today;
                    var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == sale.StoreId && s.ShiftDate == today);

                    bool isNewShift = false;
                    if (shift == null)
                    {
                        shift = new CashShift { StoreId = sale.StoreId, ShiftDate = today };
                        isNewShift = true;
                    }

                    if (difference > 0)
                    {
                        if (mainPayment?.Method == "Готівка") { shift.IncomeUAH += difference; if (store != null) store.BalanceUAH += difference; }
                        else if (mainPayment?.Method == "USD") { shift.IncomeUSD += difference; if (store != null) store.BalanceUSD += difference; }
                        else if (mainPayment?.Method == "EUR") { shift.IncomeEUR += difference; if (store != null) store.BalanceEUR += difference; }
                        else { shift.IncomeCard += difference; if (store != null) store.BalanceCard += difference; }
                    }
                    else
                    {
                        decimal absDiff = Math.Abs(difference);
                        if (mainPayment?.Method == "Готівка") { shift.ExpenseUAH += absDiff; if (store != null) store.BalanceUAH -= absDiff; }
                        else if (mainPayment?.Method == "USD") { shift.ExpenseUSD += absDiff; if (store != null) store.BalanceUSD -= absDiff; }
                        else if (mainPayment?.Method == "EUR") { shift.ExpenseEUR += absDiff; if (store != null) store.BalanceEUR -= absDiff; }
                        else { if (store != null) store.BalanceCard -= absDiff; }
                    }

                    if (isNewShift) _context.CashShifts.Add(shift);
                    else _context.CashShifts.Update(shift);

                    if (store != null) _context.Stores.Update(store);

                    _context.CashTransactions.Add(new CashTransaction
                    {
                        StoreId = sale.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Amount = Math.Abs(difference),
                        Currency = mainPayment?.Method ?? "UAH",
                        Comment = $"Заміна товару в чеку #{sale.Id} (Різниця в ціні)",
                        Type = difference > 0 ? "Income" : "Expense",
                        Category = "Adjustment",
                        Date = TimeHelper.GetUkraineTime()
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        public class AddItemDto { public int SaleId { get; set; } public string ItemType { get; set; } = ""; public int StockId { get; set; } public decimal SellingPrice { get; set; } public bool UpdateCash { get; set; } }

        // 🔥 ДОДАВАННЯ ТОВАРУ ДО ЧЕКУ
        [HttpPost("add-item-to-receipt")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddItemToReceipt([FromBody] AddItemDto req)
        {
            var sale = await _context.Sales.Include(s => s.SaleItems).Include(s => s.Payments).FirstOrDefaultAsync(s => s.Id == req.SaleId);
            if (sale == null) return NotFound("Чек не знайдено");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal purchasePriceUSD = 0;
                decimal purchasePriceUAH = 0;
                string productName = "";

                if (req.ItemType == "Accessory")
                {
                    var stock = await _context.AccessoryStock.FirstOrDefaultAsync(a => a.ProductId == req.StockId && a.StoreId == sale.StoreId);
                    if (stock == null) return BadRequest("Немає на складі");

                    var prodNameFromDb = await _context.Products.Where(p => p.Id == stock.ProductId).Select(p => p.Name).FirstOrDefaultAsync();

                    stock.Quantity -= 1;
                    purchasePriceUAH = stock.PurchasePrice;
                    purchasePriceUSD = Math.Round(purchasePriceUAH / sale.ExchangeRate, 2);
                    productName = prodNameFromDb ?? stock.ProductName ?? "Аксесуар";
                }
                else if (req.ItemType == "Device")
                {
                    var device = await _context.DeviceStock.FirstOrDefaultAsync(d => d.Id == req.StockId && d.StoreId == sale.StoreId && d.IsSold == 0);
                    if (device == null) return BadRequest("Пристрою немає на складі");

                    device.IsSold = 1;
                    purchasePriceUSD = device.PurchasePrice + device.RepairCost;
                    purchasePriceUAH = purchasePriceUSD * sale.ExchangeRate;
                    productName = $"{device.ModelName} {device.Capacity} {device.Color}".Trim();
                }
                else { return BadRequest("Невідомий тип товару"); }

                decimal course = sale.ExchangeRate > 10 ? sale.ExchangeRate : 41.5m;
                var newItem = new SaleItem
                {
                    SaleId = sale.Id,
                    ItemId = req.StockId,
                    ItemType = req.ItemType,
                    ProductName = productName,
                    Quantity = 1,
                    SoldSellingPrice = req.SellingPrice,
                    SoldPurchasePrice = purchasePriceUAH,
                    PurchasePriceUSD = purchasePriceUSD,
                    SellingPriceUSD = Math.Round(req.SellingPrice / course, 2),
                    WarrantyPrice = 0,
                    WarrantyProfit = 0,
                    ProfitUSD = Math.Round((req.SellingPrice / course) - purchasePriceUSD, 2),
                    PositionProfit = req.SellingPrice - purchasePriceUAH
                };

                _context.SaleItems.Add(newItem);
                sale.FinalAmount += req.SellingPrice;
                sale.TotalAmount += req.SellingPrice;
                sale.TotalProfit += newItem.PositionProfit;

                var mainPayment = sale.Payments.FirstOrDefault(p => p.Method == "Готівка") ?? sale.Payments.FirstOrDefault();
                if (mainPayment != null) mainPayment.Amount += req.SellingPrice;

                if (req.UpdateCash)
                {
                    var store = await _context.Stores.FindAsync(sale.StoreId);
                    var today = DateTime.Today;
                    var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == sale.StoreId && s.ShiftDate == today);

                    bool isNewShift = false;
                    if (shift == null)
                    {
                        shift = new CashShift { StoreId = sale.StoreId, ShiftDate = today };
                        isNewShift = true;
                    }

                    if (mainPayment?.Method == "Готівка") { shift.IncomeUAH += req.SellingPrice; if (store != null) store.BalanceUAH += req.SellingPrice; }
                    else if (mainPayment?.Method == "USD") { shift.IncomeUSD += req.SellingPrice; if (store != null) store.BalanceUSD += req.SellingPrice; }
                    else if (mainPayment?.Method == "EUR") { shift.IncomeEUR += req.SellingPrice; if (store != null) store.BalanceEUR += req.SellingPrice; }
                    else { shift.IncomeCard += req.SellingPrice; if (store != null) store.BalanceCard += req.SellingPrice; }

                    if (isNewShift) _context.CashShifts.Add(shift);
                    else _context.CashShifts.Update(shift);

                    if (store != null) _context.Stores.Update(store);

                    _context.CashTransactions.Add(new CashTransaction
                    {
                        StoreId = sale.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Amount = req.SellingPrice,
                        Currency = mainPayment?.Method ?? "UAH",
                        Comment = $"Додано товар у чек #{sale.Id}",
                        Type = "Income",
                        Category = "Adjustment",
                        Date = TimeHelper.GetUkraineTime()
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        public class UpdateReceiptDetailsDto
        {
            public string Source { get; set; } = "";
            public string TrackingNumber { get; set; } = "";
        }

        [HttpPost("update-receipt-details/{saleId}")]
        public async Task<IActionResult> UpdateReceiptDetails(int saleId, [FromBody] UpdateReceiptDetailsDto req)
        {
            var sale = await _context.Sales.FindAsync(saleId);
            if (sale == null) return NotFound("Чек не знайдено");

            bool isAdmin = User.IsInRole("Admin");

            if (!isAdmin)
            {
                if (string.IsNullOrWhiteSpace(sale.TrackingNumber) && !string.IsNullOrWhiteSpace(req.TrackingNumber))
                {
                    sale.TrackingNumber = req.TrackingNumber;
                    sale.Status = "Shipped";
                }
            }
            else
            {
                sale.Source = req.Source;
                sale.TrackingNumber = req.TrackingNumber;
                sale.Status = !string.IsNullOrWhiteSpace(req.TrackingNumber) ? "Shipped" : "Completed";
            }

            _context.Entry(sale).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("search-customers")]
        public async Task<IActionResult> SearchCustomers([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Ok(new List<object>());

            string lowerQuery = query.ToLower();
            var cleanPhone = new string(query.Where(char.IsDigit).ToArray());

            var queryable = _context.Customers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(cleanPhone))
            {
                string altPhone = cleanPhone.StartsWith("38") ? cleanPhone.Substring(2) : (cleanPhone.StartsWith("0") ? "38" + cleanPhone : cleanPhone);
                queryable = queryable.Where(c => c.Phone.StartsWith(cleanPhone) || c.Phone.StartsWith(altPhone) || c.Phone.StartsWith("+" + cleanPhone) || c.Phone.StartsWith("+" + altPhone));
            }
            else
            {
                queryable = queryable.Where(c => c.Name.ToLower().StartsWith(lowerQuery) || c.Name.ToLower().Contains(" " + lowerQuery));
            }

            var customers = await queryable
                .Take(5)
                .Select(c => new { c.Id, c.Name, c.Phone })
                .ToListAsync();

            return Ok(customers);
        }

        public class AssignCustomerDto { public string CustomerPhone { get; set; } = ""; public string CustomerName { get; set; } = ""; }

        [HttpPost("assign-customer/{saleId}")]
        public async Task<IActionResult> AssignCustomer(int saleId, [FromBody] AssignCustomerDto req)
        {
            if (string.IsNullOrWhiteSpace(req.CustomerPhone)) return BadRequest("Телефон обов'язковий");

            var sale = await _context.Sales.FindAsync(saleId);
            if (sale == null) return NotFound("Чек не знайдено");

            string cleanPhone = new string(req.CustomerPhone.Where(char.IsDigit).ToArray());
            if (cleanPhone.Length > 10) cleanPhone = cleanPhone.Substring(cleanPhone.Length - 10);

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Phone.Contains(cleanPhone));
            if (customer == null)
            {
                customer = new Customer { Phone = req.CustomerPhone, Name = string.IsNullOrWhiteSpace(req.CustomerName) ? "Клієнт" : req.CustomerName, CreatedAt = TimeHelper.GetUkraineTime(), LastVisit = TimeHelper.GetUkraineTime() };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else
            {
                customer.LastVisit = TimeHelper.GetUkraineTime();
                if (!string.IsNullOrWhiteSpace(req.CustomerName) && customer.Name == "Клієнт") customer.Name = req.CustomerName;
            }

            sale.CustomerId = customer.Id;
            await _context.SaveChangesAsync();

            return Ok(new { customerName = customer.Name });
        }

        [HttpPost("remove-customer/{saleId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RemoveCustomer(int saleId)
        {
            var sale = await _context.Sales.FindAsync(saleId);
            if (sale == null) return NotFound("Чек не знайдено");

            var warranties = await _context.CustomerWarranties.Where(w => w.SaleId == saleId).ToListAsync();
            if (warranties.Any())
            {
                _context.CustomerWarranties.RemoveRange(warranties);
            }

            sale.CustomerId = null;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("warranty-rates")]
        public async Task<IActionResult> GetWarrantyRates()
        {
            var rates = await _context.WarrantyRates.AsNoTracking().OrderBy(w => w.Price).ToListAsync();
            return Ok(rates);
        }

        public class IssueWarrantyDto { public int SaleItemId { get; set; } public int WarrantyRateId { get; set; } }

        [HttpPost("add-warranty")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateWarranty([FromBody] IssueWarrantyDto req)
        {
            var item = await _context.SaleItems.Include(i => i.Sale).ThenInclude(s => s!.Payments).FirstOrDefaultAsync(i => i.Id == req.SaleItemId);
            if (item == null || item.Sale == null) return NotFound("Товар не знайдено");

            if (item.WarrantyPrice > 0) return BadRequest("Для цього товару вже оформлено гарантію");

            var warrantyRate = await _context.WarrantyRates.FindAsync(req.WarrantyRateId);
            if (warrantyRate == null) return NotFound("Тариф не знайдено");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (item.Sale.CustomerId.HasValue)
                {
                    var cw = new CustomerWarranty
                    {
                        CustomerId = item.Sale.CustomerId.Value,
                        SaleId = item.Sale.Id,
                        DeviceModel = item.ProductName,
                        Imei = item.Identifier,
                        WarrantyRateId = warrantyRate.Id,
                        WarrantyType = warrantyRate.Type,
                        TermMonths = warrantyRate.TermMonths,
                        StartDate = TimeHelper.GetUkraineTime(),
                        EndDate = TimeHelper.GetUkraineTime().AddMonths(warrantyRate.TermMonths)
                    };
                    _context.CustomerWarranties.Add(cw);
                }

                item.WarrantyId = warrantyRate.Id;
                item.WarrantyPrice = warrantyRate.Price;

                decimal course = item.Sale.ExchangeRate > 10 ? item.Sale.ExchangeRate : 41.5m;
                item.WarrantyProfit = Math.Round(warrantyRate.Price / course, 2);
                item.PositionProfit += warrantyRate.Price;

                item.Sale.FinalAmount += warrantyRate.Price;
                item.Sale.TotalAmount += warrantyRate.Price;
                item.Sale.TotalProfit += item.WarrantyProfit;

                var mainPayment = item.Sale.Payments.FirstOrDefault(p => p.Method == "Готівка") ?? item.Sale.Payments.FirstOrDefault();
                if (mainPayment != null) mainPayment.Amount += warrantyRate.Price;

                var store = await _context.Stores.FindAsync(item.Sale.StoreId);
                var today = DateTime.Today;
                var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == item.Sale.StoreId && s.ShiftDate == today);

                bool isNewShift = false;
                if (shift == null)
                {
                    shift = new CashShift { StoreId = item.Sale.StoreId, ShiftDate = today };
                    isNewShift = true;
                }

                if (mainPayment?.Method == "Готівка") { shift.IncomeUAH += warrantyRate.Price; if (store != null) store.BalanceUAH += warrantyRate.Price; }
                else if (mainPayment?.Method == "USD") { shift.IncomeUSD += warrantyRate.Price; if (store != null) store.BalanceUSD += warrantyRate.Price; }
                else if (mainPayment?.Method == "EUR") { shift.IncomeEUR += warrantyRate.Price; if (store != null) store.BalanceEUR += warrantyRate.Price; }
                else { shift.IncomeCard += warrantyRate.Price; if (store != null) store.BalanceCard += warrantyRate.Price; }

                if (isNewShift) _context.CashShifts.Add(shift);
                else _context.CashShifts.Update(shift);

                if (store != null) _context.Stores.Update(store);

                _context.CashTransactions.Add(new CashTransaction
                {
                    StoreId = item.Sale.StoreId,
                    ShiftId = shift.Id,
                    Amount = warrantyRate.Price,
                    Currency = mainPayment?.Method ?? "UAH",
                    Comment = $"Дооформлення гарантії до чеку #{item.Sale.Id}",
                    Type = "Income",
                    Category = "Sale",
                    Date = TimeHelper.GetUkraineTime()
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        // 🔥 ГЛОБАЛЬНИЙ ПОШУК ЧЕКІВ
        [HttpGet("search-receipts")]
        public async Task<IActionResult> SearchReceipts([FromQuery] string query, [FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var targetEnd = end.Date.AddDays(1);

            if (string.IsNullOrWhiteSpace(query)) return Ok(new List<object>());

            var salesQuery = _context.Sales
                .AsNoTracking()
                .Include(s => s.SaleItems)
                .Include(s => s.Payments)
                .Include(s => s.Customer)
                .Where(s => s.SaleDate >= start.Date && s.SaleDate < targetEnd);

            if (int.TryParse(query, out int receiptId))
            {
                salesQuery = salesQuery.Where(s => s.Id == receiptId);
            }
            else
            {
                salesQuery = salesQuery.Where(s =>
                    s.SaleItems.Any(i => i.Identifier != null && i.Identifier.Contains(query)) ||
                    s.TrackingNumber != null && s.TrackingNumber.Contains(query));
            }

            var sales = await salesQuery.OrderByDescending(s => s.SaleDate).ToListAsync();

            var userIds = sales.Select(s => s.UserId).Distinct().ToList();
            var usersDict = await _context.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "Невідомий");

            var receiptsList = sales.Select(sale => new
            {
                Id = sale.Id,
                Date = sale.SaleDate,
                FinalAmount = sale.FinalAmount,
                CashierName = !string.IsNullOrEmpty(sale.UserId) && usersDict.ContainsKey(sale.UserId) ? usersDict[sale.UserId] : "Невідомий",
                CustomerName = sale.Customer?.Name,
                CustomerPhone = sale.Customer?.Phone,
                Payments = sale.Payments.Select(p => new { Amount = p.Amount, Method = p.Method }).ToList(),
                ItemsCount = sale.SaleItems.Sum(i => i.Quantity),
                Source = sale.Source,
                TrackingNumber = sale.TrackingNumber,
                Items = sale.SaleItems.Select(i => new
                {
                    Id = i.Id,
                    ModelName = i.ProductName,
                    Type = i.ItemType,
                    Quantity = i.Quantity,
                    SellingPrice = i.SoldSellingPrice,
                    SellingPriceUSD = i.SellingPriceUSD,
                    WarrantyPrice = i.WarrantyPrice
                }).ToList()
            }).ToList();

            return Ok(receiptsList);
        }
    }
}