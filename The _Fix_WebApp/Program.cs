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
// The auth cookie is encrypted with these keys. Left at defaults, ASP.NET Core generates
// ephemeral keys per process, which invalidates every session on every app restart and
// breaks entirely if you ever scale to more than one instance. Persisting keys to disk
// (or a shared store in production - Redis/Blob/DB) keeps sessions valid across restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("FashionFix");

// --- Identity / Authentication ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password policy - meets common baseline (length, mixed character classes).
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;

        // How often a signed-in user's role/claims are re-checked against the database.
        // Keeps permission changes (Roles screen) from being stuck on a stale cookie for too long.
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    // Re-validate the signed-in user's roles/claims against the DB every 5 minutes instead
    // of Identity's default 30 - so a permission change on the Roles screen, or an admin
    // deactivating someone, takes effect quickly instead of waiting out a stale cookie.
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);

    // Hardened cookie flags: never readable by JS, only ever sent over HTTPS, and blocked
    // from being attached to genuinely cross-site requests (defence-in-depth alongside the
    // anti-forgery tokens already used on every POST).
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = "FashionFix.Auth";
});

// --- Authorization: one policy per permission (see Security/Permissions.cs). Controllers
// authorize against these, never against role names - so a brand-new role created on the
// Roles screen works everywhere immediately, with zero code changes or redeploys. ---
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
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<PaystackOptions>(builder.Configuration.GetSection("Paystack"));
builder.Services.AddHttpClient<IPaymentService, PaystackPaymentService>();
builder.Services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
// Registered as a singleton (rather than plain AddHostedService<T>) so the same instance can
// also be injected into a controller for a manual "run now" trigger (see
// OrdersController.RunFulfillmentCycle) - the hosted service and the on-demand trigger are the
// same object, coordinated by its internal cycle lock.
builder.Services.AddSingleton<OrderFulfillmentBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OrderFulfillmentBackgroundService>());

// --- The Courier Guy (Shiplogic) integration ---
// API key lives in user-secrets / env vars, never appsettings.json. Registered via
// AddHttpClient so it gets a pooled, properly-disposed HttpClient rather than a new one per call.
builder.Services.Configure<FashionFix.Web.Services.Courier.CourierGuyOptions>(builder.Configuration.GetSection("CourierGuy"));
// CourierGuy:Provider = "Fake" (only honoured in Development - structurally impossible to
// accidentally ship to production) swaps in FakeCourierService: an in-process double that
// simulates the full booking/tracking lifecycle with no network calls and no cost. "EasyPost"
// (also Development-only) swaps in a REAL courier API call, using EasyPost's free test mode
// (never charges, no card required) instead - useful when you specifically want to exercise
// real HTTP/JSON handling. The real sandbox at shiplogic.com is a billed account with its own
// balance, so iterating on this feature against it repeatedly burns real (if sandbox) money for
// no benefit - use "Fake" or "EasyPost" while building/testing, and "Live" (the default) only
// for a final, deliberately limited pass against the real sandbox before going live.
var courierProvider = builder.Environment.IsDevelopment() ? builder.Configuration["CourierGuy:Provider"] : null;

if (string.Equals(courierProvider, "Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<FashionFix.Web.Services.Courier.ICourierService, FashionFix.Web.Services.Courier.FakeCourierService>();
}
else if (string.Equals(courierProvider, "EasyPost", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<FashionFix.Web.Services.Courier.EasyPostOptions>(builder.Configuration.GetSection("EasyPost"));
    builder.Services.AddHttpClient<FashionFix.Web.Services.Courier.ICourierService, FashionFix.Web.Services.Courier.EasyPostCourierService>();
}
else
{
    builder.Services.AddHttpClient<FashionFix.Web.Services.Courier.ICourierService, FashionFix.Web.Services.Courier.CourierGuyService>();
}

// --- Session (backs the customer's shopping cart - no new DB table needed) ---
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true; // the cart is core functionality, not tracking
});

// --- MVC ---
builder.Services.AddControllersWithViews(options =>
{
    // Must run before the framework's own decimal binder - see the class doc comment for why
    // this is needed at all (a period-decimal posted value failing to parse under a
    // comma-decimal server culture).
    options.ModelBinderProviders.Insert(0, new FashionFix.Web.Infrastructure.InvariantDecimalModelBinderProvider());
});

var app = builder.Build();
// --- Apply any pending EF Core migrations, creating the database/tables if they don't exist yet ---
using (var migrationScope = app.Services.CreateScope())
{
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}
// --- Seed roles + their default permission claims, and bootstrap the first Administrator ---
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    foreach (var (roleName, defaultPermissions) in Permissions.DefaultRolePermissions)
    {
        var role = await roleManager.FindByNameAsync(roleName);

        if (role is null)
        {
            role = new IdentityRole(roleName);
            await roleManager.CreateAsync(role);
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = existingClaims
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        // Administrator always keeps every permission - guaranteed on every startup, not just
        // when the role is first created, so an upgrade from an older version (or any other
        // way this role ended up with stale/missing claims) can never lock every admin out.
        // Other built-in roles are only backfilled with their defaults while they have ZERO
        // permission claims at all, so an Administrator's deliberate customizations on the
        // Roles screen are never silently overwritten on restart.
        var permissionsToGrant = roleName == "Administrator"
            ? Permissions.All.Keys.Where(p => !existingPermissions.Contains(p))
            : (existingPermissions.Count == 0 ? defaultPermissions : Array.Empty<string>());

        foreach (var permission in permissionsToGrant)
            await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));
    }

    // --- Bootstrap the first Administrator account ---
    // Staff accounts can only be created by an existing Administrator, so on a brand-new
    // database there'd be no way to sign in at all. Seed one default admin once, with a
    // console warning to change the password immediately after first login.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
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
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(admin, "Ch4ngeMe!Now");
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Administrator");
            if (app.Environment.IsDevelopment())
            {
                app.Logger.LogWarning(
                    "Seeded default Administrator account - username: '{Username}', password: 'Ch4ngeMe!Now'. " +
                    "Log in via Employee Login and change this password immediately (My Profile > Change Password).",
                    seedAdminUsername);
            }
        }
    }

    // --- Seed the storefront departments (Women/Men/Kids/Footwear/Accessories/Sale) ---
    // Only inserted the first time - if an admin later renames/reorders/deactivates one via
    // a future Department management screen, restarting the app must never overwrite that.
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (!await dbContext.Departments.AnyAsync())
    {
        dbContext.Departments.AddRange(
            new Department { Name = "Women", Slug = "women", DisplayOrder = 1, IsActive = true },
            new Department { Name = "Men", Slug = "men", DisplayOrder = 2, IsActive = true },
            new Department { Name = "Kids", Slug = "kids", DisplayOrder = 3, IsActive = true },
            new Department { Name = "Footwear", Slug = "footwear", DisplayOrder = 4, IsActive = true },
            new Department { Name = "Accessories", Slug = "accessories", DisplayOrder = 5, IsActive = true },
            new Department { Name = "Sale & Outlet", Slug = "sale", DisplayOrder = 6, IsActive = true }
        );
        await dbContext.SaveChangesAsync();
    }

    // --- Seed default homepage content (hero + Style Box copy) ---
    // Editable afterwards via Storefront/Content - this seed only ever runs once.
    if (!await dbContext.SiteSettings.AnyAsync())
    {
        dbContext.SiteSettings.Add(new SiteSettings());
        await dbContext.SaveChangesAsync();
    }

    // --- Seed default pricing settings (60% markup) ---
    // Editable afterwards via /Pricing - this seed only ever runs once.
    if (!await dbContext.PricingSettings.AnyAsync())
    {
        dbContext.PricingSettings.Add(new PricingSettings());
        await dbContext.SaveChangesAsync();
    }

    // --- Optional: a couple of sample products with stocked variants, purely so there's
    // something in the catalogue to check out with while testing the order -> courier ->
    // email pipeline end to end. Development-only and off unless explicitly enabled - never
    // touches a real/production catalogue, and only ever inserts once (skipped the moment
    // any Product already exists, seeded or otherwise).
    if (app.Environment.IsDevelopment()
        && app.Configuration.GetValue<bool>("DemoData:SeedSampleCatalog")
        && !await dbContext.Products.AnyAsync())
    {
        var women = await dbContext.Departments.FirstOrDefaultAsync(d => d.Slug == "women");
        var footwear = await dbContext.Departments.FirstOrDefaultAsync(d => d.Slug == "footwear");

        var tee = new Product
        {
            Name = "Demo Test - Classic Tee",
            Description = "Seeded test product - safe to delete once you're done testing checkout.",
            SKU = "DEMO-0001",
            Category = "Clothing",
            Brand = "Fashion Fix",
            CostPrice = 60,
            SellingPrice = 150,
            ImageUrl = "https://placehold.co/400x400/E4DEC9/24211B?text=Demo+Tee",
            DepartmentId = women?.DepartmentId,
            IsActive = true
        };
        tee.Variants.Add(new ProductVariant { SKU = "DEMO-0001-M-BLK", Size = "M", Color = "Black", ColorHex = "#000000", StockQuantity = 25, IsActive = true });
        tee.Variants.Add(new ProductVariant { SKU = "DEMO-0001-L-BLK", Size = "L", Color = "Black", ColorHex = "#000000", StockQuantity = 25, IsActive = true });

        var sneaker = new Product
        {
            Name = "Demo Test - Canvas Sneaker",
            Description = "Seeded test product - safe to delete once you're done testing checkout.",
            SKU = "DEMO-0002",
            Category = "Footwear",
            Brand = "Fashion Fix",
            CostPrice = 220,
            SellingPrice = 550,
            ImageUrl = "https://placehold.co/400x400/E4DEC9/24211B?text=Demo+Sneaker",
            DepartmentId = footwear?.DepartmentId,
            IsActive = true
        };
        sneaker.Variants.Add(new ProductVariant { SKU = "DEMO-0002-8-WHT", Size = "8", Color = "White", ColorHex = "#FFFFFF", StockQuantity = 15, IsActive = true });
        sneaker.Variants.Add(new ProductVariant { SKU = "DEMO-0002-9-WHT", Size = "9", Color = "White", ColorHex = "#FFFFFF", StockQuantity = 15, IsActive = true });

        dbContext.Products.AddRange(tee, sneaker);
        await dbContext.SaveChangesAsync();

        app.Logger.LogWarning(
            "DemoData:SeedSampleCatalog is enabled - seeded 2 demo products (DEMO-0001, DEMO-0002) for testing. " +
            "Turn this off in appsettings.Development.json once you're done, and remove the demo products from Product Management.");
    }
}

// --- HTTP pipeline ---
if (app.Environment.IsDevelopment())
{
    // Detailed in-browser stack traces during development.
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Friendly fallback for 404s instead of a bare status page.
app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");

// Fixes every .ToString("C") call site across the app (Orders, POS, Reports, Checkout, Cart,
// Dashboard, etc.) rendering with the wrong currency symbol - with no culture configured at
// all, that call falls back to whatever the host OS/container's default locale is (commonly
// "$" on en-US, or the generic "¤" sign if the environment has no locale data), instead of
// "R" for Rand. Storefront pages that build their own "R" + number string are unaffected
// either way, but this is what makes .ToString("C") consistent with them everywhere else.
var siteCulture = new CultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = siteCulture;
CultureInfo.DefaultThreadCurrentUICulture = siteCulture;
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-ZA")
    .AddSupportedCultures("en-ZA")
    .AddSupportedUICultures("en-ZA"));

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "department",
    pattern: "Shop/Department/{slug}",
    defaults: new { controller = "Shop", action = "Department" });

app.MapControllerRoute(
    name: "orderTracking",
    pattern: "Customer/Orders/{id}/Track",
    defaults: new { controller = "Customer", action = "Track" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
