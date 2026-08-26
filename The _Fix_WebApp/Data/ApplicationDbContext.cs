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
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<ReturnTransaction> ReturnTransactions => Set<ReturnTransaction>();
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

        builder.Entity<PurchaseOrder>()
            .HasIndex(po => po.PONumber)
            .IsUnique();

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

        // --- Purchase order relationships ---
        builder.Entity<PurchaseOrderItem>()
            .HasOne(poi => poi.PurchaseOrder)
            .WithMany(po => po.Items)
            .HasForeignKey(poi => poi.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseOrderItem>()
            .HasOne(poi => poi.Product)
            .WithMany(p => p.PurchaseOrderItems)
            .HasForeignKey(poi => poi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Return relationships ---
        builder.Entity<ReturnTransaction>()
            .HasOne(r => r.Order)
            .WithMany(o => o.Returns)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ReturnTransaction>()
            .HasOne(r => r.OrderItem)
            .WithMany()
            .HasForeignKey(r => r.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Decimal precision guards (belt-and-braces alongside [Column] attributes) ---
        builder.Entity<Product>().Property(p => p.CostPrice).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.SellingPrice).HasPrecision(18, 2);
    }
}
