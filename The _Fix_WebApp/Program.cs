using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using The__Fix_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Fail fast with a message you can act on: outside Development, a database on "localhost" can never be
// reached from Azure. (appsettings.json ships with a localhost string for local work; production must
// override it with the ConnectionStrings__DefaultConnection setting.)
{
    var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    var dataSource = csb.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
    var isLocalServer = dataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
        || dataSource.StartsWith("(local", StringComparison.OrdinalIgnoreCase)
        || dataSource.StartsWith(".", StringComparison.Ordinal)
        || dataSource.StartsWith("127.0.0.1", StringComparison.Ordinal);
    if (!builder.Environment.IsDevelopment() && isLocalServer)
        throw new InvalidOperationException(
            $"The database connection string points at '{csb.DataSource}', which is not reachable from a hosted server. " +
            "Set the ConnectionStrings__DefaultConnection setting (App Service > Environment variables) to your Azure SQL connection string.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<FashionFix.Web.Services.Audit.AuditLedgerService>();

// --- Data Protection ---
// On Azure App Service the deployed folder can be read-only (run-from-package) and is replaced on every
// deploy, so keys live under %HOME% (persistent, writable). Locally they stay in ./keys.
var azureHome = Environment.GetEnvironmentVariable("HOME");
var dataProtectionKeyPath = (builder.Environment.IsDevelopment() || string.IsNullOrEmpty(azureHome))
    ? Path.Combine(builder.Environment.ContentRootPath, "keys")
    : Path.Combine(azureHome, "ASP.NET", "DataProtection-Keys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("FashionFix");

// Behind Azure's front end the app sees the proxy, not the visitor: trust X-Forwarded-* so cookies,
// https redirects, Paystack callback URLs and the audit-log IP use the real client details.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

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
builder.Services.Configure<FashionFix.Web.Services.GoogleMapsOptions>(builder.Configuration.GetSection("GoogleMaps"));

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
builder.Services.Configure<FashionFix.Web.Services.AddressAutocompleteSettings>(
    builder.Configuration.GetSection("AddressAutocomplete"));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("geoapify", c =>
{
    c.BaseAddress = new Uri("https://api.geoapify.com/v1/");
    c.Timeout = TimeSpan.FromSeconds(6);
});

// --- Image storage (Cloudinary when configured, local wwwroot/uploads otherwise) ---
builder.Services.Configure<FashionFix.Web.Services.Images.CloudinarySettings>(
    builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddHttpClient("cloudinary", c =>
{
    c.BaseAddress = new Uri("https://api.cloudinary.com/v1_1/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
if (builder.Configuration.GetSection("Cloudinary").Get<FashionFix.Web.Services.Images.CloudinarySettings>()?.IsConfigured == true)
    builder.Services.AddScoped<FashionFix.Web.Services.Images.IImageStorage, FashionFix.Web.Services.Images.CloudinaryImageStorage>();
else
    builder.Services.AddScoped<FashionFix.Web.Services.Images.IImageStorage, FashionFix.Web.Services.Images.LocalImageStorage>();

// --- Authorization ---
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All.Keys)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireClaim(Permissions.ClaimType, permission));
    }

    // Raising/managing purchase orders and approving them are independent permissions. This policy lets EITHER holder
    // open the PO list and details; each action then requires the specific permission it needs.
    options.AddPolicy(Permissions.LowStockAccessPolicy, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(Permissions.ClaimType, Permissions.LowStockView) ||
            ctx.User.HasClaim(Permissions.ClaimType, Permissions.ProductsManage)));

    options.AddPolicy(Permissions.PurchaseOrdersAccessPolicy, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(Permissions.ClaimType, Permissions.PurchaseOrdersManage) ||
            ctx.User.HasClaim(Permissions.ClaimType, Permissions.PurchaseOrdersApprove)));
});

// --- Application services ---
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IRewardsService, RewardsService>();
builder.Services.AddScoped<IDiscountService, DiscountService>();

builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ICustomerNotificationService, CustomerNotificationService>();

builder.Services.Configure<PaystackOptions>(
    builder.Configuration.GetSection("Paystack"));

builder.Services.AddHttpClient<IPaymentService, PaystackPaymentService>();

builder.Services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
builder.Services.AddScoped<IOrderCancellationService, OrderCancellationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Registered as a singleton so the same instance can also be injected into controllers
// for a manual "run now" trigger.
builder.Services.AddSingleton<OrderFulfillmentBackgroundService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<OrderFulfillmentBackgroundService>());

// --- The Courier Guy / EasyPost integration ---
builder.Services.Configure<FashionFix.Web.Services.Courier.CourierGuyOptions>(
    builder.Configuration.GetSection("CourierGuy"));

// The simulated courier is the only courier this deployment uses, in EVERY environment (Azure
// runs as "Production", where the old code ignored the Provider setting and fell back to the
// real Courier Guy client - which has no API key, so fulfilment never ran). Defaults to Fake;
// set CourierGuy:Provider to "EasyPost" or "Live" explicitly to use a real provider instead.
var courierProvider = builder.Configuration["CourierGuy:Provider"];
if (string.IsNullOrWhiteSpace(courierProvider)) courierProvider = "Fake";

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
builder.Services.AddScoped<IPaymentRecoveryService, PaymentRecoveryService>();

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
    options.Filters.Add<FashionFix.Web.Infrastructure.FriendlyErrorFilter>();
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

    // A paused serverless Azure SQL database can take a minute to wake: retry before giving up, and say
    // clearly what failed (server and database name only - never the password).
    var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            dbContext.Database.Migrate();
            app.Logger.LogInformation("Database '{Database}' on '{Server}' is up to date.", target.InitialCatalog, target.DataSource);
            break;
        }
        // EF wraps transient SQL errors (e.g. 40613 'database not currently available' while a serverless
        // database resumes) in an InvalidOperationException, so match on the inner SqlException too.
        catch (Exception ex) when (attempt < 8 && (ex is Microsoft.Data.SqlClient.SqlException || ex.InnerException is Microsoft.Data.SqlClient.SqlException))
        {
            app.Logger.LogWarning(ex, "Database not reachable yet (attempt {Attempt}/8) - '{Database}' on '{Server}'. Retrying in 15s.",
                attempt, target.InitialCatalog, target.DataSource);
            Thread.Sleep(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex, "STARTUP FAILED applying migrations to '{Database}' on '{Server}'.", target.InitialCatalog, target.DataSource);
            throw;
        }
    }
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

        // Markers record "this newer default permission has already been offered to this role", so a permission an
        // administrator later removes (e.g. from the Administrator role itself) is NOT silently re-granted on
        // the next restart. Only a role with no permissions at all receives its full defaults (as before).
        var seededMarkers = existingClaims
            .Where(c => c.Type == Permissions.SeededMarkerClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        var isFreshRole = existingPermissions.Count == 0;

        foreach (var permission in defaultPermissions)
        {
            if (!existingPermissions.Contains(permission))
            {
                var grant =
                    isFreshRole
                    || (Permissions.IntroducedLater.Contains(permission) && !seededMarkers.Contains(permission));

                if (grant)
                {
                    await roleManager.AddClaimAsync(
                        role,
                        new System.Security.Claims.Claim(
                            Permissions.ClaimType,
                            permission));
                    existingPermissions.Add(permission);
                }
            }

            if (Permissions.IntroducedLater.Contains(permission) && !seededMarkers.Contains(permission))
            {
                await roleManager.AddClaimAsync(
                    role,
                    new System.Security.Claims.Claim(
                        Permissions.SeededMarkerClaimType,
                        permission));
            }
        }

        // The Administrator can trim their own permissions, but never "Roles & Permissions" - that's the way back in.
        if (roleName == "Administrator")
        {
            foreach (var locked in Permissions.AdministratorLocked)
            {
                if (!existingPermissions.Contains(locked))
                {
                    await roleManager.AddClaimAsync(
                        role,
                        new System.Security.Claims.Claim(
                            Permissions.ClaimType,
                            locked));
                }
            }
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

    // Development keeps the well-known password for convenience. Anywhere else the password MUST come from
    // configuration (App Service setting Seed__AdminPassword) - a published site must never ship a default login.
    var seedAdminPassword = app.Configuration["Seed:AdminPassword"];
    if (string.IsNullOrWhiteSpace(seedAdminPassword) && app.Environment.IsDevelopment())
        seedAdminPassword = "Ch4ngeMe!Now";

    if (await userManager.FindByNameAsync(seedAdminUsername) is null && string.IsNullOrWhiteSpace(seedAdminPassword))
    {
        app.Logger.LogError(
            "No administrator account exists and Seed:AdminPassword is not set, so none was created. " +
            "Set the Seed__AdminPassword setting (12+ characters with upper, lower, digit and symbol) and restart the app.");
    }
    else if (await userManager.FindByNameAsync(seedAdminUsername) is null)
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
                seedAdminPassword!);

        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(
                admin,
                "Administrator");

            if (app.Environment.IsDevelopment())
            {
                app.Logger.LogWarning(
                    "Seeded Administrator account - username: '{Username}'. " +
                    "Log in via Employee Login and change the password immediately (My Profile > Change Password).",
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
app.UseForwardedHeaders();

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
// en-ZA's own decimal separator is a COMMA, but HTML <input type="number"> only accepts / emits a
// DOT. Rendering "100,7" into a number input made the browser blank the field (prices "disappeared"
// after saving), and the old parser read "100,70" as 10070. So: keep everything else about en-ZA
// (Rand symbol "R", date formats) but use a dot as the decimal mark and a space for thousands.
var siteCulture = (CultureInfo)new CultureInfo("en-ZA").Clone();
siteCulture.NumberFormat.NumberDecimalSeparator = ".";
siteCulture.NumberFormat.CurrencyDecimalSeparator = ".";
siteCulture.NumberFormat.PercentDecimalSeparator = ".";
siteCulture.NumberFormat.NumberGroupSeparator = " ";
siteCulture.NumberFormat.CurrencyGroupSeparator = " ";
siteCulture.NumberFormat.PercentGroupSeparator = " ";

CultureInfo.DefaultThreadCurrentCulture = siteCulture;
CultureInfo.DefaultThreadCurrentUICulture = siteCulture;

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(siteCulture, siteCulture),
    SupportedCultures = new List<CultureInfo> { siteCulture },
    SupportedUICultures = new List<CultureInfo> { siteCulture }
};
// Ignore the browser's Accept-Language so every visitor gets the same number formatting.
localizationOptions.RequestCultureProviders.Clear();
app.UseRequestLocalization(localizationOptions);

app.UseHttpsRedirection();
app.UseStaticFiles();

// Make the client IP available to the audit ledger (see AuditRequestContext).
app.Use(async (ctx, next) =>
{
    FashionFix.Web.Services.Audit.AuditRequestContext.IpAddress = ctx.Connection.RemoteIpAddress?.ToString();
    await next();
});

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