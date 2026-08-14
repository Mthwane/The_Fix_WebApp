using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- Identity / Authentication ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password policy - tighten as needed for production.
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Home/Index";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});

// --- Application services ---
builder.Services.AddScoped<IInventoryService, InventoryService>();

// --- MVC ---
builder.Services.AddControllersWithViews();

var app = builder.Build();

// --- Seed roles on startup (Administrator, Manager, Employee, Customer, Owner) ---
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = { "Administrator", "Manager", "Employee", "Customer", "Owner" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // --- Bootstrap the first Administrator account ---
    // Staff accounts can only be created by an existing Administrator (US-15/16), so on a
    // brand-new database there'd be no way to sign in at all. Seed one default admin once,
    // with a console warning to change the password immediately after first login.
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

        var createResult = await userManager.CreateAsync(admin, "Admin@12345");
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Administrator");
            app.Logger.LogWarning(
                "Seeded default Administrator account - username: '{Username}', password: 'Admin@12345'. " +
                "Log in via Employee Login and change this password immediately.", seedAdminUsername);
        }
    }
}

// --- HTTP pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
