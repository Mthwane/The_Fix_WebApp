using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    options.LoginPath = "/Home/Index";
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

// --- Session (backs the customer's shopping cart - no new DB table needed) ---
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true; // the cart is core functionality, not tracking
});

// --- MVC ---
builder.Services.AddControllersWithViews();

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

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
