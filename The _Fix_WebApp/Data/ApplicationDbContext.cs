using FashionFix.Web.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // --- Data integrity: unique SKU / PO number / order number ---
        builder.Entity<Product>()
            .HasIndex(p => p.SKU)
            .IsUnique();

        builder.Entity<Order>()
            .HasIndex(o => o.OrderNumber)
            .IsUnique();

<<<<<<< HEAD
        // --- Performance indexes: cover the columns that are actually filtered/sorted on ---
        // Products.Index / Shop.Index filter on IsActive + Category (and friends) and always
        // sort by Name - this pair of indexes lets SQL Server seek instead of scanning the
        // whole table on every catalogue page load.
        builder.Entity<Product>()
            .HasIndex(p => new { p.IsActive, p.Category });

        builder.Entity<Product>()
            .HasIndex(p => p.Name);

        // Reports.Index/Export filters by a DateCreated range; Orders.Index filters by
        // Status/OrderType. Neither had a supporting index, so both were doing full table
        // scans that get slower as the Orders table grows.
        builder.Entity<Order>()
            .HasIndex(o => o.DateCreated);

        builder.Entity<Order>()
            .HasIndex(o => new { o.Status, o.OrderType });

=======
>>>>>>> origin/SprintPresent
        // --- Order relationships ---
        builder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Order>()
            .HasOne(o => o.ProcessedByUser)
            .WithMany()
            .HasForeignKey(o => o.ProcessedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Decimal precision guards (belt-and-braces alongside [Column] attributes) ---
        builder.Entity<Product>().Property(p => p.CostPrice).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.SellingPrice).HasPrecision(18, 2);
    }
}
