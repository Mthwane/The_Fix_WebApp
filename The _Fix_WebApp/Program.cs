
using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using The__Fix_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- Data Protection ---
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(
            Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("FashionFix");

// --- Identity / Authentication ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;

    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);

    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = "FashionFix.Auth";
});

// --- Authorization ---
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All.Keys)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireClaim(Permissions.ClaimType, permission));
    }
});

// --- Application services ---
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IRewardsService, RewardsService>();

builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.Configure<PaystackOptions>(
    builder.Configuration.GetSection("Paystack"));

builder.Services.AddHttpClient<IPaymentService, PaystackPaymentService>();

builder.Services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Registered as a singleton so the same instance can also be injected into controllers
// for a manual "run now" trigger.
builder.Services.AddSingleton<OrderFulfillmentBackgroundService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<OrderFulfillmentBackgroundService>());

// --- The Courier Guy / EasyPost integration ---
builder.Services.Configure<FashionFix.Web.Services.Courier.CourierGuyOptions>(
    builder.Configuration.GetSection("CourierGuy"));

var courierProvider =
    builder.Environment.IsDevelopment()
        ? builder.Configuration["CourierGuy:Provider"]
        : null;

if (string.Equals(
        courierProvider,
        "Fake",
        StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<
        FashionFix.Web.Services.Courier.ICourierService,
        FashionFix.Web.Services.Courier.FakeCourierService>();
}
else if (string.Equals(
             courierProvider,
             "EasyPost",
             StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<
        FashionFix.Web.Services.Courier.EasyPostOptions>(
        builder.Configuration.GetSection("EasyPost"));

    builder.Services.AddHttpClient<
        FashionFix.Web.Services.Courier.ICourierService,
        FashionFix.Web.Services.Courier.EasyPostCourierService>();
}
else
{
    builder.Services.AddHttpClient<
        FashionFix.Web.Services.Courier.ICourierService,
        FashionFix.Web.Services.Courier.CourierGuyService>();
}

// --- Session ---
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// --- MVC ---
builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(
        0,
        new FashionFix.Web.Infrastructure.InvariantDecimalModelBinderProvider());
});

var app = builder.Build();

// -----------------------------------------------------------------------------
// Apply pending EF Core migrations
// -----------------------------------------------------------------------------
using (var migrationScope = app.Services.CreateScope())
{
    var dbContext =
        migrationScope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    dbContext.Database.Migrate();
}

// -----------------------------------------------------------------------------
// Seed roles, permissions, administrator and application defaults
// -----------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var roleManager =
        scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

    // -------------------------------------------------------------------------
    // Roles + default permissions
    // -------------------------------------------------------------------------
    foreach (var (roleName, defaultPermissions)
             in Permissions.DefaultRolePermissions)
    {
        var role =
            await roleManager.FindByNameAsync(roleName);

        if (role is null)
        {
            role = new IdentityRole(roleName);
            await roleManager.CreateAsync(role);
        }

        var existingClaims =
            await roleManager.GetClaimsAsync(role);

        var existingPermissions = existingClaims
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        var permissionsToGrant =
            roleName == "Administrator"
                ? Permissions.All.Keys
                    .Where(p => !existingPermissions.Contains(p))
                    .ToArray()
                : existingPermissions.Count == 0
                    ? defaultPermissions
                    : Array.Empty<string>();

        foreach (var permission in permissionsToGrant)
        {
            await roleManager.AddClaimAsync(
                role,
                new System.Security.Claims.Claim(
                    Permissions.ClaimType,
                    permission));
        }
    }

    // -------------------------------------------------------------------------
    // Services / managers needed for seeds
    // -------------------------------------------------------------------------
    var userManager =
        scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

    var dbContext =
        scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    // -------------------------------------------------------------------------
    // Rewards settings
    // -------------------------------------------------------------------------
    // Rewards starts OFF. Existing balances are not affected by this seed.
    if (!await dbContext.RewardsSettings.AnyAsync())
    {
        dbContext.RewardsSettings.Add(
            new RewardsSettings
            {
                Id = 1,
                IsEnabled = false
            });

        await dbContext.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Bootstrap first Administrator
    // -------------------------------------------------------------------------
    const string seedAdminUsername = "admin";

    if (await userManager.FindByNameAsync(seedAdminUsername) is null)
    {
        var admin = new ApplicationUser
        {
            UserName = seedAdminUsername,
            Email = "admin@fashionfix.local",
            FullName = "System Administrator",
            JobPosition = "Administrator",
            EmploymentStatus = "Active",
            DateHired = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = true
        };

        var createResult =
            await userManager.CreateAsync(
                admin,
                "Ch4ngeMe!Now");

        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(
                admin,
                "Administrator");

            if (app.Environment.IsDevelopment())
            {
                app.Logger.LogWarning(
                    "Seeded default Administrator account - username: '{Username}', password: 'Ch4ngeMe!Now'. " +
                    "Log in via Employee Login and change this password immediately (My Profile > Change Password).",
                    seedAdminUsername);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Seed storefront departments
    // -------------------------------------------------------------------------
    if (!await dbContext.Departments.AnyAsync())
    {
        dbContext.Departments.AddRange(
            new Department
            {
                Name = "Women",
                Slug = "women",
                DisplayOrder = 1,
                IsActive = true
            },
            new Department
            {
                Name = "Men",
                Slug = "men",
                DisplayOrder = 2,
                IsActive = true
            },
            new Department
            {
                Name = "Kids",
                Slug = "kids",
                DisplayOrder = 3,
                IsActive = true
            },
            new Department
            {
                Name = "Footwear",
                Slug = "footwear",
                DisplayOrder = 4,
                IsActive = true
            },
            new Department
            {
                Name = "Accessories",
                Slug = "accessories",
                DisplayOrder = 5,
                IsActive = true
            },
            new Department
            {
                Name = "Sale & Outlet",
                Slug = "sale",
                DisplayOrder = 6,
                IsActive = true
            });

        await dbContext.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Seed default homepage content
    // -------------------------------------------------------------------------
    if (!await dbContext.SiteSettings.AnyAsync())
    {
        dbContext.SiteSettings.Add(
            new SiteSettings());

        await dbContext.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Seed default pricing settings
    // -------------------------------------------------------------------------
    if (!await dbContext.PricingSettings.AnyAsync())
    {
        dbContext.PricingSettings.Add(
            new PricingSettings());

        await dbContext.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Optional development demo catalogue
    // -------------------------------------------------------------------------
    if (app.Environment.IsDevelopment()
        && app.Configuration.GetValue<bool>(
            "DemoData:SeedSampleCatalog")
        && !await dbContext.Products.AnyAsync())
    {
        var women =
            await dbContext.Departments
                .FirstOrDefaultAsync(d => d.Slug == "women");

        var footwear =
            await dbContext.Departments
                .FirstOrDefaultAsync(d => d.Slug == "footwear");

        var tee = new Product
        {
            Name = "Demo Test - Classic Tee",
            Description =
                "Seeded test product - safe to delete once you're done testing checkout.",
            SKU = "DEMO-0001",
            Category = "Clothing",
            Brand = "Fashion Fix",
            CostPrice = 60,
            SellingPrice = 150,
            ImageUrl =
                "https://placehold.co/400x400/E4DEC9/24211B?text=Demo+Tee",
            DepartmentId = women?.DepartmentId,
            IsActive = true
        };

        tee.Variants.Add(
            new ProductVariant
            {
                SKU = "DEMO-0001-M-BLK",
                Size = "M",
                Color = "Black",
                ColorHex = "#000000",
                StockQuantity = 25,
                IsActive = true
            });

        tee.Variants.Add(
            new ProductVariant
            {
                SKU = "DEMO-0001-L-BLK",
                Size = "L",
                Color = "Black",
                ColorHex = "#000000",
                StockQuantity = 25,
                IsActive = true
            });

        var sneaker = new Product
        {
            Name = "Demo Test - Canvas Sneaker",
            Description =
                "Seeded test product - safe to delete once you're done testing checkout.",
            SKU = "DEMO-0002",
            Category = "Footwear",
            Brand = "Fashion Fix",
            CostPrice = 220,
            SellingPrice = 550,
            ImageUrl =
                "https://placehold.co/400x400/E4DEC9/24211B?text=Demo+Sneaker",
            DepartmentId = footwear?.DepartmentId,
            IsActive = true
        };

        sneaker.Variants.Add(
            new ProductVariant
            {
                SKU = "DEMO-0002-8-WHT",
                Size = "8",
                Color = "White",
                ColorHex = "#FFFFFF",
                StockQuantity = 15,
                IsActive = true
            });

        sneaker.Variants.Add(
            new ProductVariant
            {
                SKU = "DEMO-0002-9-WHT",
                Size = "9",
                Color = "White",
                ColorHex = "#FFFFFF",
                StockQuantity = 15,
                IsActive = true
            });

        dbContext.Products.AddRange(
            tee,
            sneaker);

        await dbContext.SaveChangesAsync();

        app.Logger.LogWarning(
            "DemoData:SeedSampleCatalog is enabled - seeded 2 demo products " +
            "(DEMO-0001, DEMO-0002) for testing. Turn this off in " +
            "appsettings.Development.json once you're done.");
    }
}

// -----------------------------------------------------------------------------
// HTTP pipeline
// -----------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute(
    "/Home/StatusCode/{0}");

// --- South African currency / culture ---
var siteCulture =
    new CultureInfo("en-ZA");

CultureInfo.DefaultThreadCurrentCulture =
    siteCulture;

CultureInfo.DefaultThreadCurrentUICulture =
    siteCulture;

app.UseRequestLocalization(
    new RequestLocalizationOptions()
        .SetDefaultCulture("en-ZA")
        .AddSupportedCultures("en-ZA")
        .AddSupportedUICultures("en-ZA"));

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// --- Routes ---
app.MapControllerRoute(
    name: "department",
    pattern: "Shop/Department/{slug}",
    defaults: new
    {
        controller = "Shop",
        action = "Department"
    });

app.MapControllerRoute(
    name: "orderTracking",
    pattern: "Customer/Orders/{id}/Track",
    defaults: new
    {
        controller = "Customer",
        action = "Track"
    });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();