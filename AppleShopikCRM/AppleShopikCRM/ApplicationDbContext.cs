using AppleShopikCRM.Client.Models;
using AppleShopikCRM.Server.Models; // <--- 1. ДОДАЙТЕ ЦЕ (тут лежить ApplicationUser)
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // <--- 2. ДОДАЙТЕ ЦЕ (для Identity)
using Microsoft.EntityFrameworkCore;

namespace AppleShopikCRM
{
    // 👇 3. ЗМІНІТЬ DbContext НА IdentityDbContext<ApplicationUser>
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Таблиці довідників
        public DbSet<Store> Stores { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<WarrantyRate> WarrantyRates { get; set; }

        // Таблиці обліку (Склад)
        public DbSet<DeviceStock> DeviceStock { get; set; }
        public DbSet<AccessoryStock> AccessoryStock { get; set; }
        public DbSet<Service> Services { get; set; }

        public DbSet<RepairTicket> RepairTickets { get; set; }

        // Таблиці продажів та Trade-in
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SaleItem> SaleItems { get; set; }
        public DbSet<TradeIns> TradeIns { get; set; }

        public DbSet<CashShift> CashShifts { get; set; }
        public DbSet<CashTransaction> CashTransactions { get; set; }

        public DbSet<RestockHistory> RestockHistories { get; set; }
        public DbSet<TransferRequest> TransferRequests { get; set; }

        public DbSet<RepairService> RepairServices { get; set; }
        public DbSet<SalePayment> SalePayments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 👇 Цей рядок КРИТИЧНО ВАЖЛИВИЙ для Identity (створює ключі для User, Roles тощо)
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var currentTableName = entity.GetTableName();
                if (!string.IsNullOrEmpty(currentTableName))
                {
                    entity.SetTableName(currentTableName.ToLower());
                }
            }
            // --- DeviceStock ---
            modelBuilder.Entity<DeviceStock>()
                .Property(d => d.ConditionStatus)
                .HasConversion<string>();

            modelBuilder.Entity<DeviceStock>()
                .HasIndex(d => d.Identifier)
                .IsUnique();

            // --- Sale -> SaleItems cascade ---
            modelBuilder.Entity<Sale>()
                .HasMany(s => s.SaleItems)
                .WithOne(si => si.Sale)
                .HasForeignKey(si => si.SaleId)
                .OnDelete(DeleteBehavior.Cascade);

            // ✅ Sale -> Customer (nullable FK)
            modelBuilder.Entity<Sale>()
                 .HasOne(s => s.Customer) // 🔥 ВИПРАВЛЕНО: Тепер EF Core точно знає, про яку властивість мова
                 .WithMany(c => c.Sales)
                 .HasForeignKey(s => s.CustomerId)
                 .OnDelete(DeleteBehavior.SetNull)
                 .IsRequired(false);

            // ✅ CustomerWarranty -> Customer
            modelBuilder.Entity<CustomerWarranty>()
                .HasOne(w => w.Customer)
                .WithMany(c => c.Warranties)
                .HasForeignKey(w => w.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            // ✅ CustomerWarranty -> Sale (no cascade щоб не ламав продажі)
            modelBuilder.Entity<CustomerWarranty>()
                .HasOne(w => w.Sale)
                .WithMany(s => s.CustomerWarranties)
                .HasForeignKey(w => w.SaleId)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ CustomerWarranty -> WarrantyRate
            modelBuilder.Entity<CustomerWarranty>()
                .HasOne(w => w.WarrantyRate)
                .WithMany()
                .HasForeignKey(w => w.WarrantyRateId)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ Індекс на телефон клієнта для швидкого пошуку
            modelBuilder.Entity<Customer>()
                .HasIndex(c => c.Phone);

            // --- TransferRequests ---
            modelBuilder.Entity<TransferRequest>().ToTable("TransferRequests");
            modelBuilder.Entity<TransferRequest>()
                .Property(t => t.Status)
                .HasConversion<string>();

            modelBuilder.Entity<DeviceStock>()
            .Property(d => d.ConditionStatus)
            .HasConversion<string>();

            // Автоматичне налаштування точності для ВСІХ полів decimal (18,2)
            var decimalProperties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?));

            modelBuilder.Entity<TransferRequest>().ToTable("TransferRequests");
            modelBuilder.Entity<TransferRequest>().Property(t => t.Status).HasConversion<string>();

            foreach (var property in decimalProperties)
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }

            modelBuilder.Entity<DeviceStock>()
                .HasIndex(d => d.Identifier)
                .IsUnique();

            modelBuilder.Entity<Sale>()
                .HasMany(s => s.SaleItems)
                .WithOne(si => si.Sale)
                .HasForeignKey(si => si.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}