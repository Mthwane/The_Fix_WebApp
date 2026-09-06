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
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerPaymentMethod> CustomerPaymentMethods => Set<CustomerPaymentMethod>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentSubCategory> DepartmentSubCategories => Set<DepartmentSubCategory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<FeaturedProduct> FeaturedProducts => Set<FeaturedProduct>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<PricingSettings> PricingSettings => Set<PricingSettings>();

    public DbSet<CategoryPricingRule> CategoryPricingRules => Set<CategoryPricingRule>();
    public DbSet<EmailSubscriber> EmailSubscribers => Set<EmailSubscriber>();
    public DbSet<ShiftSession> ShiftSessions => Set<ShiftSession>();

    // --- Supply chain (returning from V1, now variant-based) ---
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<RestockBundle> RestockBundles => Set<RestockBundle>();
    public DbSet<RestockBundleItem> RestockBundleItems => Set<RestockBundleItem>();
    public DbSet<ReturnTransaction> ReturnTransactions => Set<ReturnTransaction>();

    // --- Courier integration ---
    public DbSet<CourierShipment> CourierShipments => Set<CourierShipment>();
    public DbSet<CourierTrackingEvent> CourierTrackingEvents => Set<CourierTrackingEvent>();

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
            .WithMany()
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<OrderItem>()
            .HasOne(oi => oi.ProductVariant)
            .WithMany(v => v.OrderItems)
            .HasForeignKey(oi => oi.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Product variants ---
        builder.Entity<ProductVariant>()
            .HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseOrder>()
        .HasOne(p => p.CreatedByUser)
        .WithMany()
        .HasForeignKey(p => p.CreatedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

       builder.Entity<RestockBundle>()
      .HasOne(b => b.CreatedByUser)
      .WithMany()
      .HasForeignKey(b => b.CreatedByUserId)
      .OnDelete(DeleteBehavior.Restrict);

     builder.Entity<ReturnTransaction>()
    .HasOne(r => r.ProcessedByUser)
    .WithMany()
    .HasForeignKey(r => r.ProcessedByUserId)
    .OnDelete(DeleteBehavior.Restrict);

        // The sellable/scannable SKU lives on the variant now, and must be unique across
        // the whole catalogue (POS scans and storefront URLs both key off this).
        builder.Entity<ProductVariant>()
            .HasIndex(v => v.SKU)
            .IsUnique();

        // A style can't have the same Size/Colour combination twice.
        builder.Entity<ProductVariant>()
            .HasIndex(v => new { v.ProductId, v.Size, v.Color })
            .IsUnique();

        builder.Entity<ProductVariant>().Property(v => v.PriceOverride).HasPrecision(18, 2);

        builder.Entity<InventoryTransaction>()
            .HasOne(t => t.Product)
            .WithMany()
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InventoryTransaction>()
            .HasOne(t => t.ProductVariant)
            .WithMany(v => v.InventoryTransactions)
            .HasForeignKey(t => t.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Departments ---
        builder.Entity<Product>()
            .HasOne(p => p.Department)
            .WithMany(d => d.Products)
            .HasForeignKey(p => p.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Department>().HasIndex(d => d.Slug).IsUnique();

        // SiteSettings is a manually-assigned singleton row (always Id = 1), not an
        // auto-incrementing identity column - without this, EF treats the int primary key as
        // IDENTITY by convention and rejects the explicit Id=1 insert in Program.cs.



        builder.Entity<SiteSettings>().Property(s => s.Id).ValueGeneratedNever();

        builder.Entity<PricingSettings>().Property(p => p.Id).ValueGeneratedNever();
        builder.Entity<PricingSettings>().Property(p => p.DefaultMarkupPercentage).HasPrecision(9, 2);

        // A category can only have one markup rule...

        // A category can only have one markup rule (Category is never null here, unlike a
        // theoretical "global" row, so a plain unique index is safe - SQL Server would let
        // multiple NULLs through a unique index but every row here has a real category).
        builder.Entity<CategoryPricingRule>().HasIndex(r => r.Category).IsUnique();
        builder.Entity<CategoryPricingRule>().Property(r => r.MarkupPercentage).HasPrecision(9, 2);

        // Re-subscribing after an unsubscribe reactivates the same row rather than creating a
        // duplicate - enforced here, not just in the controller.
        builder.Entity<EmailSubscriber>().HasIndex(s => s.Email).IsUnique();

        // --- Till shifts ---
        builder.Entity<ShiftSession>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ShiftSession>().Property(s => s.OpeningFloat).HasPrecision(18, 2);
        builder.Entity<ShiftSession>().Property(s => s.ClosingFloat).HasPrecision(18, 2);

        // A user can only have one shift open at a time - enforced with a filtered unique
        // index (SQL Server) so it's a real DB-level guarantee, not just a controller check.
        builder.Entity<ShiftSession>()
            .HasIndex(s => s.UserId)
            .HasFilter("[Status] = 0")
            .IsUnique();

        // --- Supply chain ---
        builder.Entity<PurchaseOrder>().HasIndex(p => p.PONumber).IsUnique();

        builder.Entity<PurchaseOrder>()
            .HasOne(p => p.Supplier)
            .WithMany(s => s.PurchaseOrders)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrder>()
            .HasOne(p => p.RestockBundle)
            .WithMany(b => b.GeneratedPurchaseOrders)
            .HasForeignKey(p => p.RestockBundleId)
            .OnDelete(DeleteBehavior.SetNull); // deleting a template must never delete its history

        builder.Entity<PurchaseOrder>()
            .HasOne(p => p.ApprovedByUser)
            .WithMany()
            .HasForeignKey(p => p.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PurchaseOrderItem>()
            .HasOne(i => i.ProductVariant)
            .WithMany()
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RestockBundleItem>()
            .HasOne(i => i.ProductVariant)
            .WithMany()
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // A bundle lists each variant once - quantity is a field, not a repeated row.
        builder.Entity<RestockBundleItem>()
            .HasIndex(i => new { i.RestockBundleId, i.ProductVariantId })
            .IsUnique();

        builder.Entity<ReturnTransaction>()
            .HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ReturnTransaction>()
            .HasOne(r => r.OrderItem)
            .WithMany()
            .HasForeignKey(r => r.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ReturnTransaction>()
            .HasOne(r => r.ProductVariant)
            .WithMany()
            .HasForeignKey(r => r.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ReturnTransaction>()
    .HasOne(r => r.ProcessedByUser)
    .WithMany()
    .HasForeignKey(r => r.ProcessedByUserId)
    .OnDelete(DeleteBehavior.Restrict);


        // --- Courier ---
        // A shipment hangs off EITHER an Order or a PurchaseOrder; both FKs are nullable and
        // each side is optional, so no cascade path can delete a shipment record out from under
        // its tracking history.
        builder.Entity<CourierShipment>()
            .HasOne(s => s.Order)
            .WithMany()
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CourierShipment>()
            .HasOne(s => s.PurchaseOrder)
            .WithMany()
            .HasForeignKey(s => s.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CourierShipment>().HasIndex(s => s.TrackingReference);

        builder.Entity<CourierTrackingEvent>()
            .HasOne(e => e.CourierShipment)
            .WithMany(s => s.TrackingEvents)
            .HasForeignKey(e => e.CourierShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // De-dupes repeated polls of the same tracking event.
        builder.Entity<CourierTrackingEvent>()
            .HasIndex(e => new { e.CourierShipmentId, e.ProviderEventId })
            .IsUnique();

        builder.Entity<DepartmentSubCategory>()
            .HasOne(s => s.Department)
            .WithMany(d => d.SubCategories)
            .HasForeignKey(s => s.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Product images ---
        builder.Entity<ProductImage>()
            .HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Reviews ---
        builder.Entity<ProductReview>()
            .HasOne(r => r.Product)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProductReview>()
            .HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // A customer can only review a given product once.
        builder.Entity<ProductReview>()
            .HasIndex(r => new { r.ProductId, r.CustomerId })
            .IsUnique();

        // --- Wishlist ---
        builder.Entity<WishlistItem>()
            .HasOne(w => w.Customer)
            .WithMany()
            .HasForeignKey(w => w.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WishlistItem>()
            .HasOne(w => w.Product)
            .WithMany(p => p.WishlistedBy)
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WishlistItem>()
            .HasIndex(w => new { w.CustomerId, w.ProductId })
            .IsUnique();

        // --- Customer addresses / saved cards ---
        // Both are owned by exactly one customer and should disappear if that account is
        // deleted (unlike Orders, which are kept for financial history via Restrict).
        builder.Entity<CustomerAddress>()
            .HasOne(a => a.Customer)
            .WithMany(u => u.Addresses)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CustomerAddress>()
            .HasIndex(a => a.CustomerId);

        builder.Entity<CustomerPaymentMethod>()
            .HasOne(p => p.Customer)
            .WithMany(u => u.PaymentMethods)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CustomerPaymentMethod>()
            .HasIndex(p => p.CustomerId);

        // A customer should never end up with the exact same saved card twice.
        builder.Entity<CustomerPaymentMethod>()
            .HasIndex(p => new { p.CustomerId, p.AuthorizationCode })
            .IsUnique();

        // --- Decimal precision guards (belt-and-braces alongside [Column] attributes) ---
        builder.Entity<Product>().Property(p => p.CostPrice).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.SellingPrice).HasPrecision(18, 2);
        builder.Entity<Product>().Property(p => p.CompareAtPrice).HasPrecision(18, 2);
    }
}