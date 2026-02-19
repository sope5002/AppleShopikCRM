using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Helpers;
using AppleShopikCRM.Server.Models;
using AppleShopikCRM.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppleShopikCRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SuppliersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TelegramService _telegramService;

        public SuppliersController(ApplicationDbContext context, TelegramService telegramService)
        {
            _context = context;
            _telegramService = telegramService;
        }

        [HttpGet]
        public async Task<IActionResult> GetSuppliers()
        {
            var suppliers = await _context.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
            return Ok(suppliers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSupplier([FromBody] Supplier supplier)
        {
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSupplier(int id, [FromBody] Supplier supplier)
        {
            var existing = await _context.Suppliers.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name = supplier.Name;
            existing.Phone = supplier.Phone;
            existing.Notes = supplier.Notes;
            existing.Debt = supplier.Debt;

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("consignment-debts")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetConsignmentDebts()
        {
            var debts = await _context.DeviceStock
                .Where(d => d.IsConsignment && !d.IsSupplierPaid && d.SupplierId != null)
                .Select(d => new
                {
                    DeviceId = d.Id,
                    SupplierName = d.SupplierName ?? "Невідомо",
                    ModelName = $"{d.ModelName} {d.Capacity} {d.Color}",
                    Identifier = d.Identifier,
                    PurchasePrice = d.PurchasePrice,
                    PaidAmount = d.SupplierPaidAmount,
                    DebtUSD = d.PurchasePrice - d.SupplierPaidAmount,
                    IsSold = d.IsSold == 1
                })
                .OrderByDescending(d => d.IsSold)
                .ToListAsync();

            return Ok(debts);
        }

        [HttpGet("paid-history")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetPaidConsignmentHistory()
        {
            var history = await _context.DeviceStock
                .Where(d => d.IsConsignment && d.IsSupplierPaid && d.SupplierId != null)
                .Select(d => new
                {
                    DeviceId = d.Id,
                    SupplierName = d.SupplierName ?? "Невідомо",
                    ModelName = $"{d.ModelName} {d.Capacity} {d.Color}",
                    Identifier = d.Identifier,
                    PurchasePrice = d.PurchasePrice,
                    DateAdded = d.DateAdded
                })
                .OrderByDescending(d => d.DateAdded)
                .Take(100)
                .ToListAsync();

            return Ok(history);
        }

        public class PayDebtDto
        {
            public int StoreId { get; set; }
            public decimal Amount { get; set; }
            public string Comment { get; set; } = "";
        }

        // 🔥 МИ ПЛАТИМО ГРОШІ ПОСТАЧАЛЬНИКУ (ПОГАШЕННЯ БОРГУ)
        [HttpPost("{id}/pay")]
        public async Task<IActionResult> PayDebt(int id, [FromBody] PayDebtDto req)
        {
            if (req.Amount <= 0) return BadRequest("Сума має бути більшою за нуль.");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var supplier = await _context.Suppliers.FindAsync(id);
                if (supplier == null) return NotFound("Постачальника не знайдено.");

                var store = await _context.Stores.FindAsync(req.StoreId);
                if (store == null) return NotFound("Магазин не знайдено.");

                var today = DateTime.Today;
                var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == req.StoreId && s.ShiftDate == today);
                if (shift == null)
                {
                    shift = new CashShift { StoreId = req.StoreId, ShiftDate = today };
                    _context.CashShifts.Add(shift);
                    await _context.SaveChangesAsync();
                }

                shift.ExpenseUSD += req.Amount;
                store.BalanceUSD -= req.Amount;

                string userName = User.FindFirst(ClaimTypes.UserData)?.Value ?? User.Identity?.Name ?? "Менеджер";
                string fullComment = string.IsNullOrWhiteSpace(req.Comment)
                    ? $"Оплата постачальнику: {supplier.Name} ({userName})"
                    : $"Оплата постачальнику: {supplier.Name} | {req.Comment} ({userName})";

                _context.CashTransactions.Add(new CashTransaction
                {
                    StoreId = req.StoreId,
                    ShiftId = shift.Id,
                    Amount = req.Amount,
                    Currency = "USD",
                    Comment = fullComment,
                    Type = "Expense",
                    Category = "SupplierPayment",
                    Date = TimeHelper.GetUkraineTime()
                });

                // Розкидаємо готівку на найстаріші борги
                var devices = await _context.DeviceStock
                    .Where(d => d.SupplierId == id && d.IsConsignment && !d.IsSupplierPaid)
                    .OrderBy(d => d.DateAdded)
                    .ToListAsync();

                decimal paymentLeft = req.Amount;
                foreach (var dev in devices)
                {
                    if (paymentLeft <= 0) break;

                    decimal deviceRemainingDebt = dev.PurchasePrice - dev.SupplierPaidAmount;
                    if (deviceRemainingDebt <= 0) continue;

                    if (paymentLeft >= deviceRemainingDebt)
                    {
                        dev.SupplierPaidAmount = dev.PurchasePrice;
                        dev.IsSupplierPaid = true;
                        paymentLeft -= deviceRemainingDebt;
                    }
                    else
                    {
                        dev.SupplierPaidAmount += paymentLeft;
                        paymentLeft = 0;
                    }
                }

                supplier.Debt -= req.Amount;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 🔥 ВІДПРАВКА СПОВІЩЕННЯ В TELEGRAM ПРО ВИДАЧУ ГОТІВКИ
                try
                {
                    string commentLine = string.IsNullOrWhiteSpace(req.Comment) ? "" : $"\n📝 <b>Коментар:</b> {req.Comment}";

                    string tgMsg = $"📦 <b>Оплата постачальнику</b>\n\n" +
                                   $"🏢 <b>Магазин:</b> {store.Name}\n" +
                                   $"👤 <b>Співробітник:</b> {userName}\n" +
                                   $"🚛 <b>Постачальник:</b> {supplier.Name}\n" +
                                   $"💸 <b>Сума:</b> {req.Amount:N0} USD\n" +
                                   $"📊 <b>Залишок боргу:</b> {supplier.Debt:N0}" +
                                   commentLine;

                    await _telegramService.SendMessageAsync(tgMsg);
                }
                catch (Exception ex) { Console.WriteLine($"TG Error: {ex.Message}"); }

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
        }

        public class ReceivePaymentDto
        {
            public int StoreId { get; set; }
            public decimal Amount { get; set; }
            public string Currency { get; set; } = "USD";
            public decimal ExchangeRate { get; set; } = 41.5m;
            public string Comment { get; set; } = "";
            public int? SaleId { get; set; }
        }

        // 🔥 ПОСТАЧАЛЬНИК ПОВЕРТАЄ НАМ ГРОШІ
        [HttpPost("{id}/receive-payment")]
        public async Task<IActionResult> ReceivePayment(int id, [FromBody] ReceivePaymentDto req)
        {
            if (req.Amount <= 0) return BadRequest("Сума має бути більшою за нуль.");
            if (req.ExchangeRate <= 0) req.ExchangeRate = 1;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var supplier = await _context.Suppliers.FindAsync(id);
                if (supplier == null) return NotFound("Постачальника не знайдено.");

                var store = await _context.Stores.FindAsync(req.StoreId);
                var shift = await GetOrCreateShift(req.StoreId);

                decimal amountInUSD = req.Currency == "USD" ? req.Amount : Math.Round(req.Amount / req.ExchangeRate, 2);

                string dbCurrencyType = "UAH";
                switch (req.Currency)
                {
                    case "USD": shift.IncomeUSD += req.Amount; if (store != null) store.BalanceUSD += req.Amount; dbCurrencyType = "USD"; break;
                    case "EUR": shift.IncomeEUR += req.Amount; if (store != null) store.BalanceEUR += req.Amount; dbCurrencyType = "EUR"; break;
                    case "Картка":
                    case "Термінал":
                    case "Картковий переказ": shift.IncomeCard += req.Amount; if (store != null) store.BalanceCard += req.Amount; dbCurrencyType = "Card"; break;
                    default: shift.IncomeUAH += req.Amount; if (store != null) store.BalanceUAH += req.Amount; dbCurrencyType = "UAH"; break;
                }

                string userName = User.FindFirst(ClaimTypes.UserData)?.Value ?? User.Identity?.Name ?? "Менеджер";
                string currencyLabel = req.Currency == "Готівка" ? "грн" : req.Currency;
                string fullComment = string.IsNullOrWhiteSpace(req.Comment)
                    ? $"Отримано борг від: {supplier.Name} ({req.Amount} {currencyLabel}) [{userName}]"
                    : $"Отримано борг від: {supplier.Name} | {req.Amount} {currencyLabel} | {req.Comment} [{userName}]";

                _context.CashTransactions.Add(new CashTransaction
                {
                    StoreId = req.StoreId,
                    ShiftId = shift.Id,
                    Amount = req.Amount,
                    Currency = dbCurrencyType,
                    Comment = fullComment,
                    Type = "Income",
                    Category = "SupplierPayment",
                    Date = TimeHelper.GetUkraineTime()
                });

                if (req.SaleId.HasValue && req.SaleId > 0)
                {
                    var outbound = await _context.SupplierOutbounds.FindAsync(req.SaleId.Value);
                    if (outbound != null)
                    {
                        outbound.PaidInCashUSD += amountInUSD;

                        if (outbound.SaleId > 0)
                        {
                            _context.SalePayments.Add(new SalePayment
                            {
                                SaleId = outbound.SaleId,
                                Method = req.Currency,
                                Amount = req.Amount,
                                ExchangeRate = req.Currency == "USD" ? 1 : req.ExchangeRate
                            });
                        }
                    }
                }
                else
                {
                    var unpaidOutbounds = await _context.SupplierOutbounds
                        .Where(o => o.SupplierId == supplier.Id && (o.AgreedPriceUSD - o.PaidInCashUSD - o.OffsetDebtUSD) > 0)
                        .OrderBy(o => o.Date)
                        .ToListAsync();

                    decimal tempAmount = amountInUSD;
                    foreach (var outb in unpaidOutbounds)
                    {
                        if (tempAmount <= 0) break;
                        decimal itemDebt = outb.AgreedPriceUSD - outb.PaidInCashUSD - outb.OffsetDebtUSD;
                        decimal payToItem = Math.Min(itemDebt, tempAmount);

                        outb.PaidInCashUSD += payToItem;
                        tempAmount -= payToItem;

                        if (outb.SaleId > 0 && payToItem > 0)
                        {
                            _context.SalePayments.Add(new SalePayment
                            {
                                SaleId = outb.SaleId,
                                Method = req.Currency,
                                Amount = req.Currency == "USD" ? payToItem : payToItem * req.ExchangeRate,
                                ExchangeRate = req.Currency == "USD" ? 1 : req.ExchangeRate
                            });
                        }
                    }
                }

                supplier.Debt += amountInUSD;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.InnerException != null ? ex.InnerException.Message : ex.Message);
            }
        }

        public class GiveDeviceDto
        {
            public int StoreId { get; set; }
            public int DeviceId { get; set; }
            public decimal AgreedPriceUSD { get; set; }
            public decimal PaidNowAmount { get; set; }
            public string PaidNowCurrency { get; set; } = "USD";
            public decimal ExchangeRate { get; set; } = 41.5m;
            public string Source { get; set; } = "Опт";
        }

        // 🔥 МИ ВІДДАЄМО ТОВАР ПОСТАЧАЛЬНИКУ
        [HttpPost("{id}/give-device")]
        public async Task<IActionResult> GiveDeviceToSupplier(int id, [FromBody] GiveDeviceDto req)
        {
            if (req.AgreedPriceUSD <= 0) return BadRequest("Ціна має бути більшою за нуль.");
            if (req.ExchangeRate <= 0) req.ExchangeRate = 1;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var supplier = await _context.Suppliers.FindAsync(id);
                if (supplier == null) return NotFound("Постачальника не знайдено.");

                var device = await _context.DeviceStock.FindAsync(req.DeviceId);
                if (device == null || device.IsSold != 0) return BadRequest("Пристрій не знайдено або вже продано.");

                var store = await _context.Stores.FindAsync(req.StoreId);
                decimal course = store?.ExchangeRate ?? 41.5m;
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "System";
                string userName = User.FindFirst(ClaimTypes.UserData)?.Value ?? User.Identity?.Name ?? "Менеджер";

                decimal paidNowUSD = req.PaidNowCurrency == "USD"
                    ? req.PaidNowAmount
                    : Math.Round(req.PaidNowAmount / req.ExchangeRate, 2);

                var sale = new Sale
                {
                    StoreId = req.StoreId,
                    SaleDate = TimeHelper.GetUkraineTime(),
                    Comment = $"Віддача постачальнику: {supplier.Name}",
                    TotalAmount = req.AgreedPriceUSD * course,
                    FinalAmount = req.AgreedPriceUSD * course,
                    ExchangeRate = course,
                    UserId = currentUserId,
                    Status = "Completed",
                    Source = req.Source
                };

                _context.Sales.Add(sale);
                await _context.SaveChangesAsync();

                var saleItem = new SaleItem
                {
                    SaleId = sale.Id,
                    ItemType = "Device",
                    ItemId = device.Id,
                    ProductName = $"{device.ModelName} {device.Capacity} {device.Color}".Trim(),
                    Identifier = device.Identifier,
                    Quantity = 1,
                    SoldPurchasePrice = device.PurchasePrice * course,
                    SoldSellingPrice = req.AgreedPriceUSD * course,
                    PurchasePriceUSD = device.PurchasePrice,
                    SellingPriceUSD = req.AgreedPriceUSD,
                    ProfitUSD = req.AgreedPriceUSD - device.PurchasePrice,
                    PositionProfit = (req.AgreedPriceUSD * course) - (device.PurchasePrice * course)
                };
                _context.SaleItems.Add(saleItem);
                sale.TotalProfit = saleItem.PositionProfit;

                device.IsSold = 1;
                device.SupplierName = supplier.Name;

                if (device.IsConsignment && !device.IsSupplierPaid)
                {
                    device.IsSupplierPaid = true;
                    device.SupplierPaidAmount = device.PurchasePrice;
                }

                var outbound = new SupplierOutbound
                {
                    SupplierId = supplier.Id,
                    StoreId = req.StoreId,
                    SaleId = sale.Id,
                    Date = TimeHelper.GetUkraineTime(),
                    ItemType = "Device",
                    ItemId = device.Id,
                    ModelName = saleItem.ProductName,
                    Imei = device.Identifier,
                    PurchasePriceUSD = device.PurchasePrice,
                    AgreedPriceUSD = req.AgreedPriceUSD,
                    PaidInCashUSD = paidNowUSD,
                    OffsetDebtUSD = 0,
                    Source = req.Source
                };

                decimal totalToPayUsUSD = req.AgreedPriceUSD - paidNowUSD;

                if (req.PaidNowAmount > 0)
                {
                    var shift = await GetOrCreateShift(req.StoreId);

                    string dbCurrencyType = "UAH";
                    switch (req.PaidNowCurrency)
                    {
                        case "USD": shift.IncomeUSD += req.PaidNowAmount; if (store != null) store.BalanceUSD += req.PaidNowAmount; dbCurrencyType = "USD"; break;
                        case "EUR": shift.IncomeEUR += req.PaidNowAmount; if (store != null) store.BalanceEUR += req.PaidNowAmount; dbCurrencyType = "EUR"; break;
                        case "Картка":
                        case "Термінал":
                        case "Картковий переказ": shift.IncomeCard += req.PaidNowAmount; if (store != null) store.BalanceCard += req.PaidNowAmount; dbCurrencyType = "Card"; break;
                        default: shift.IncomeUAH += req.PaidNowAmount; if (store != null) store.BalanceUAH += req.PaidNowAmount; dbCurrencyType = "UAH"; break;
                    }

                    _context.CashTransactions.Add(new CashTransaction
                    {
                        StoreId = req.StoreId,
                        ShiftId = shift.Id,
                        SaleId = sale.Id,
                        Amount = req.PaidNowAmount,
                        Currency = dbCurrencyType,
                        Comment = $"Оплата від {supplier.Name} при отриманні {device.ModelName}",
                        Type = "Income",
                        Category = "Sale",
                        Date = TimeHelper.GetUkraineTime()
                    });

                    _context.SalePayments.Add(new SalePayment
                    {
                        SaleId = sale.Id,
                        Method = req.PaidNowCurrency,
                        Amount = req.PaidNowAmount,
                        ExchangeRate = req.PaidNowCurrency == "USD" ? 1 : req.ExchangeRate
                    });
                }

                // Взаємозалік
                decimal offsetAppliedUSD = 0;
                if (totalToPayUsUSD > 0 && supplier.Debt > 0)
                {
                    offsetAppliedUSD = Math.Min(supplier.Debt, totalToPayUsUSD);
                    outbound.OffsetDebtUSD = offsetAppliedUSD;

                    _context.SalePayments.Add(new SalePayment
                    {
                        SaleId = sale.Id,
                        Method = "Взаємозалік",
                        Amount = offsetAppliedUSD * course,
                        ExchangeRate = course
                    });

                    var unpaidDevices = await _context.DeviceStock
                        .Where(d => d.SupplierId == supplier.Id && d.IsConsignment && !d.IsSupplierPaid)
                        .OrderBy(d => d.DateAdded)
                        .ToListAsync();

                    decimal tempOffset = offsetAppliedUSD;
                    foreach (var dev in unpaidDevices)
                    {
                        if (tempOffset <= 0) break;
                        decimal itemDebt = dev.PurchasePrice - dev.SupplierPaidAmount;
                        if (itemDebt <= 0) continue;

                        decimal payToItem = Math.Min(itemDebt, tempOffset);
                        dev.SupplierPaidAmount += payToItem;
                        if (dev.SupplierPaidAmount >= dev.PurchasePrice)
                        {
                            dev.IsSupplierPaid = true;
                        }
                        tempOffset -= payToItem;
                    }
                }

                if (totalToPayUsUSD > 0)
                {
                    decimal remainingAsDebt = totalToPayUsUSD - offsetAppliedUSD;
                    if (remainingAsDebt > 0)
                    {
                        _context.SalePayments.Add(new SalePayment
                        {
                            SaleId = sale.Id,
                            Method = "Борг постачальника",
                            Amount = remainingAsDebt * course,
                            ExchangeRate = course
                        });
                    }

                    supplier.Debt -= totalToPayUsUSD;
                }

                _context.SupplierOutbounds.Add(outbound);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                try
                {
                    string currencySign = req.PaidNowCurrency == "USD" ? "$" : (req.PaidNowCurrency == "EUR" ? "€" : "₴");
                    string tgMsg = $"📦 <b>Оптовий продаж (Постачальнику)</b>\n\n" +
                                   $"🚛 <b>Кому:</b> {supplier.Name}\n" +
                                   $"📱 <b>Товар:</b> {saleItem.ProductName}\n" +
                                   $"🆔 <b>IMEI:</b> <code>{device.Identifier}</code>\n" +
                                   $"💰 <b>Ціна:</b> ${req.AgreedPriceUSD}\n" +
                                   $"--------------------------\n" +
                                   $"💵 <b>Оплачено в касу:</b> {req.PaidNowAmount} {currencySign}\n" +
                                   $"🔄 <b>Взаємозалік боргу:</b> ${offsetAppliedUSD}\n" +
                                   $"📉 <b>Залишок у борг:</b> ${Math.Max(0, totalToPayUsUSD - offsetAppliedUSD)}\n" +
                                   $"--------------------------\n" +
                                   $"📊 <b>Новий баланс постачальника:</b> " +
                                   (supplier.Debt < 0 ? $"<b>+${Math.Abs(supplier.Debt)} (винен нам)</b>" : $"<b>-${supplier.Debt} (ми винні)</b>") + "\n" +
                                   $"👤 <b>Видав:</b> {userName}";

                    await _telegramService.SendMessageAsync(tgMsg);
                }
                catch (Exception ex) { Console.WriteLine($"TG Error: {ex.Message}"); }

                return Ok(new { saleId = outbound.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        private async Task<CashShift> GetOrCreateShift(int storeId)
        {
            var today = DateTime.Today;
            var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == storeId && s.ShiftDate == today);
            if (shift == null)
            {
                shift = new CashShift { StoreId = storeId, ShiftDate = today };
                _context.CashShifts.Add(shift);
                await _context.SaveChangesAsync();
            }
            return shift;
        }

        [HttpGet("{id}/outbound-debts")]
        public async Task<IActionResult> GetOutboundDebts(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();

            var debts = await _context.SupplierOutbounds
                .Where(o => o.SupplierId == id)
                .OrderByDescending(o => o.Date)
                .Take(100)
                .Select(o => new {
                    OutboundId = o.Id,
                    Date = o.Date,
                    ModelName = o.ModelName,
                    Imei = o.Imei,
                    AgreedPriceUSD = o.AgreedPriceUSD,
                    PaidAmountUSD = o.PaidInCashUSD + o.OffsetDebtUSD,
                    RemainingDebtUSD = Math.Round(o.AgreedPriceUSD - (o.PaidInCashUSD + o.OffsetDebtUSD), 2),
                    OffsetAmountUSD = o.OffsetDebtUSD,
                    PaidInCashUSD = o.PaidInCashUSD
                })
                .ToListAsync();

            var result = debts.Select(d => {
                bool isFullyPaid = d.RemainingDebtUSD <= 0;

                string status = "Очікує оплати";
                if (isFullyPaid)
                {
                    if (d.OffsetAmountUSD > 0 && d.PaidInCashUSD <= 0) status = "Взаємозалік";
                    else if (d.PaidInCashUSD > 0 && d.OffsetAmountUSD <= 0) status = "Кошти повернуто";
                    else if (d.PaidInCashUSD > 0 && d.OffsetAmountUSD > 0) status = "Взаємозалік + Оплата";
                    else status = "Оплачено";
                }
                else if (d.PaidAmountUSD > 0)
                {
                    status = "Частково оплачено";
                }

                return new
                {
                    SaleId = d.OutboundId,
                    Date = d.Date,
                    ModelName = d.ModelName,
                    Imei = d.Imei,
                    AgreedPriceUSD = d.AgreedPriceUSD,
                    RemainingDebtUSD = d.RemainingDebtUSD < 0 ? 0 : d.RemainingDebtUSD,
                    IsFullyPaid = isFullyPaid,
                    Status = status
                };
            });

            return Ok(result);
        }

        [HttpPost("pay-supplier/{deviceId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PaySupplierDebtIndividual(int deviceId)
        {
            var device = await _context.DeviceStock.FindAsync(deviceId);
            if (device == null) return NotFound("Пристрій не знайдено");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal remainingDebt = device.PurchasePrice - device.SupplierPaidAmount;

                if (remainingDebt > 0 && device.SupplierId.HasValue)
                {
                    var supplier = await _context.Suppliers.FindAsync(device.SupplierId);
                    if (supplier != null) supplier.Debt -= remainingDebt;
                }

                device.SupplierPaidAmount = device.PurchasePrice;
                device.IsSupplierPaid = true;

                if (remainingDebt > 0)
                {
                    var today = DateTime.Today;
                    var shift = await _context.CashShifts.FirstOrDefaultAsync(s => s.StoreId == device.StoreId && s.ShiftDate == today);
                    if (shift == null)
                    {
                        shift = new CashShift { StoreId = device.StoreId, ShiftDate = today };
                        _context.CashShifts.Add(shift);
                        await _context.SaveChangesAsync();
                    }

                    var store = await _context.Stores.FindAsync(device.StoreId);
                    if (store != null)
                    {
                        shift.ExpenseUSD += remainingDebt;
                        store.BalanceUSD -= remainingDebt;
                    }

                    var cashTransaction = new CashTransaction
                    {
                        StoreId = device.StoreId,
                        ShiftId = shift.Id,
                        Amount = remainingDebt,
                        Currency = "USD",
                        Comment = $"Оплата постачальнику (закриття позиції {device.Identifier})",
                        Type = "Expense",
                        Category = "SupplierPayment",
                        Date = TimeHelper.GetUkraineTime()
                    };
                    _context.CashTransactions.Add(cashTransaction);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest($"Помилка: {ex.Message}");
            }
        }
    }
}