

# FashionFix - Store Management System

ASP.NET Core MVC backend skeleton for the FashionFix fashion retail store,
built from the user stories / functional & non-functional requirements
document (product backlog US-01 through US-20).

## Stack
- ASP.NET Core 8 MVC
- Entity Framework Core 8 + SQL Server
- ASP.NET Core Identity (roles: Administrator, Manager, Employee, Customer, Owner)

## Project layout
See folder structure under `Controllers/`, `Models/Entities/`, `Models/ViewModels/`,
`Views/`, `Data/`, `Services/`. Every controller/entity is commented with the
user story (US-##) or non-functional requirement (NFR) it backs.

## Routing (matches the UI design)
- `/` (`HomeController.Index`) is the branded **login page** - anonymous, redirects
  signed-in users to `/Home/Dashboard`.
- `Account/Login` (POST) processes credentials and redisplays `Home/Index` on error.
- `Account/EmployeeLogin`, `Account/Register` are placeholder pages.
- `Home/Dashboard` is the authenticated business-stats dashboard (former `Home/Index`).
- Controller/view-folder names now match 1:1 for routing on case-sensitive file
  systems: `Pos` (was `POS`), `Employees` (was `Admin`), plus new `PurchaseOrders`
  and `Returns` controllers with their own views.
- `Products`, `Reports`, `Pos`, `Employees`, `PurchaseOrders`, `Returns` `Index`
  views are currently placeholders per the latest UI pass - the working logic
  still lives in the controllers, ready to be reconnected once each page is
  designed (e.g. the previous data-bound Products table can be dropped back in).

## Getting started
1. Install the .NET 8 SDK and SQL Server (or LocalDB).
2. Update `appsettings.json` -> `ConnectionStrings:DefaultConnection` if needed.
3. Restore packages:
   dotnet restore
4. Create the database:
   dotnet ef migrations add InitialCreate
   dotnet ef database update
5. Run:
   dotnet run

## What's scaffolded vs. what's left
Done:
- Full folder structure, EF Core entities + relationships, DbContext, Identity
  + role seeding, controllers with stub/working actions, view stubs wired to
  their models, InventoryService for stock sync + low-stock alerts.
- Branded split-panel login page (`Home/Index`) wired to `LoginViewModel`,
  with `Account/Login` (POST) redisplaying it with validation errors on failure.
- Views/controllers renamed to match the UI design 1:1 (`Pos`, `Employees`,
  new `PurchaseOrders` and `Returns` areas), with working return-processing
  logic in `ReturnsController` wired to `InventoryService`.
- `EmployeeViewModel` + full Create Employee flow (`EmployeesController`):
  form validation, duplicate username/email checks, `UserManager.CreateAsync`
  + role assignment, and `AuditLog` writes on both employee creation and
  `AssignRole`.
- `RegisterViewModel` + full customer self-registration flow (`AccountController`):
  same validation/duplicate-check pattern, assigns the "Customer" role, and
  signs the new user in.
- `PurchaseOrders/Create` and `Returns/Lookup` views added (were missing,
  causing a 500 on those routes) - `Returns/Index` now has a working
  order-number lookup form and lists recent returns; `PurchaseOrders/Index`
  lists POs with a "Mark Received" action.
- Solution-wide consistency pass: no leftover commented-out method
  parameters, every `View()`/`View(model)` call resolves to a `.cshtml` file,
  and every `asp-controller`/`asp-action` reference resolves to a real
  action. This is now a skeleton that **compiles and runs** end-to-end
  (login → dashboard → nav → every page renders, even where the underlying
  feature is still a placeholder).

Still TODO (marked with `// TODO` / `<!-- TODO -->` throughout):
- Employee edit form (Create is done; Edit/deactivate still pending).
- AuditLog writes on login/product/sale events (role-change and employee-create are done).
- POS barcode-scan JS wiring (site.js) and cart -> CartItems serialization.
- Receipt email/SMS delivery.
- Reports: best-sellers, stock turnover, revenue-by-category, PDF/CSV export.
- Purchase order create form + receiving logic (Supplier/PurchaseOrder entities already modelled, `Receive` action is currently a no-op stub).
- Return refund/store-credit ledger hookup (return processing + restock already works).
- First EF Core migration (run once SQL Server is available) - **required before the app will actually connect to a database**; without it the app still builds and serves the login page, but any DB-touching action will throw until you run the commands in "Getting started" below.
=======
# Fashion Fix - Store Management System

A full-stack ASP.NET Core MVC application for managing a fashion retail store:
product catalogue, point-of-sale, purchase orders/suppliers, returns, staff and
role management, business reporting, and a customer-facing storefront with
online checkout. Built against the product backlog user stories (US-01 through
US-20) in the original requirements document.

This README is written for someone who does **not** have the app running
anywhere yet and needs to get it from zero to a working local instance.

---

## 1. What's actually in this app

- **Authentication** - two separate login portals: a customer storefront login
  and a dedicated staff ("Employee") login, backed by ASP.NET Core Identity.
- **Role & permission system** - roles (Administrator, Manager, Employee,
  Customer, Owner, or any custom role you create) are just named bundles of
  permissions, editable at runtime from **Roles & Permissions** in the sidebar
  (Administrator only). No code changes or redeploys needed to add a role.
- **Product catalogue** - full CRUD, search/filter, low-stock thresholds.
- **Point of Sale** - barcode/SKU scan with an image preview popup, cart,
  automatic 15% VAT calculation, receipts (with optional emailed copy).
- **Customer storefront** - browse, cart, checkout, order tracking, order
  cancellation, "My Profile" self-service (with a visible Customer ID to give
  staff at the till).
- **Staff order fulfillment** - move online orders through
  Processing -> Shipped -> Delivered, or cancel and auto-restock.
- **Returns & refunds**, **Purchase Orders & Suppliers**, **Employee
  management**, **Audit log**, **Reports & Analytics** (with CSV export),
  **Dashboard** with live stats and low-stock alerts.
- **Notifications** - every action across the app confirms success/failure via
  an on-screen toast, each with its own short sound.
- **Real email** - genuine SMTP sending (not a stub) for receipts, order
  status changes, and cancellations. Silently no-ops if unconfigured, so the
  app still runs fine without it.

---

## 2. Prerequisites

Install these before doing anything else:

| Tool | Notes |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Check with `dotnet --version` - needs to report `8.x`. |
| SQL Server (or SQL Server Express / LocalDB) | Any edition works. |
| EF Core CLI tools | `dotnet tool install --global dotnet-ef` |
| A code editor | Visual Studio 2022, VS Code, or Rider all work. |

---

## 3. Getting the app running

```bash
# 1. Restore NuGet packages
dotnet restore

# 2. Point it at your database
#    Open appsettings.json and edit ConnectionStrings:DefaultConnection
#    to match your SQL Server instance. The default assumes a local
#    instance with Windows/Trusted authentication:
#    "Server=localhost;Database=FashionFixDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"

# 3. Create the database schema
#    If the Migrations folder already has files in it (it should, if you're
#    cloning this from the team's shared repo), just apply them:
dotnet ef database update

#    Only if there's no Migrations folder at all yet (a genuinely fresh
#    project with none committed), generate one first:
#    dotnet ef migrations add InitialCreate
#    dotnet ef database update

# 4. Run it
dotnet run
```

The console will print something like `Now listening on: https://localhost:7160`
- open that URL in a browser.

### First login

On first startup against an empty database, the app automatically seeds:
- The five built-in roles (Administrator, Manager, Employee, Customer, Owner)
  with a sensible default set of permissions each.
- **One bootstrap Administrator account** so there's a way to log in at all:

```
Username: admin
Password: Ch4ngeMe!Now
```

Log in via the **Employee Login** link (not the customer login on the
homepage). Once in, go to **Change Password** and change this immediately -
this credential is sitting in plain text in `Program.cs`.

> **Important:** This bootstrap account is only ever created if no user named
> `admin` already exists. If you ever want to fully reset, drop the database,
> re-run `dotnet ef database update`, and restart the app.

---

## 4. Project structure

```
Controllers/     One controller per feature area - see the list below.
Models/
  Entities/      EF Core entities (Product, Order, Supplier, ApplicationUser, ...)
  ViewModels/    Form-backing models, separate from the entities.
Views/           Razor views, one folder per controller.
Data/            ApplicationDbContext + EF Core migrations.
  SeedScripts/   Optional demo-data scripts (see section 6).
Services/        IInventoryService, IEmailSender, SessionCart.
Security/        Permissions.cs (the permission catalog) and TaxSettings.cs (VAT rate).
wwwroot/         Static assets - site.css, site.js (POS scanning + cart JS).
```

Controllers: `Account`, `Home`, `Customer`, `Shop`, `Products`, `Pos`,
`Orders`, `Returns`, `PurchaseOrders`, `Suppliers`, `Employees`, `Roles`,
`Reports`.

---

## 5. Configuring email (optional but recommended)

Email is real SMTP, not a stub - but it's disabled by default until you fill
in credentials. Without this, the app works completely normally; receipts and
notifications just silently don't send.

In `appsettings.json`, under `Email`:

```json
"Email": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "Username": "youraddress@gmail.com",
  "Password": "your-16-character-app-password",
  "EnableSsl": true,
  "FromAddress": "youraddress@gmail.com",
  "FromName": "Fashion Fix"
}
```

Free path via Gmail:
1. Turn on 2-Step Verification on the sending Gmail account
   (`myaccount.google.com/security`).
2. Generate an App Password at `myaccount.google.com/apppasswords`.
3. Use that 16-character code as `Password` above - **not** your normal Gmail
   password.

Any standard SMTP provider works the same way (Outlook: `smtp-mail.outlook.com:587`,
Zoho: `smtp.zoho.com:587`).

> Don't commit real credentials to source control. For anything beyond local
> development, move these into [user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
> or environment variables instead of `appsettings.json`.

---

## 6. Seeding demo data

An empty catalogue makes for a boring demo. Two options, in `Data/SeedScripts/`:

### Option A - SQL script (`SeedDemoData.sql`)
Run directly against your database (SSMS, Azure Data Studio, `sqlcmd`, etc.)
**after** the app has run at least once (it needs the roles table to already
be seeded). Adds ~20 products, 4 suppliers, and 5 working login accounts
(3 customers + a Manager + an Employee, all password `Demo@12345`) with
genuinely valid Identity password hashes - not placeholders.

### Option B - Live HTTP seeding script (`seed_live.py`) - recommended
Drives the actual running app over HTTP, the same way a person would use the
forms, so everything goes through real validation and business logic:

```bash
pip install requests
python Data/SeedScripts/seed_live.py --base-url https://localhost:7160 --admin-password "Ch4ngeMe!Now"
```

Creates 200 products, 15 suppliers, and 30 customer accounts by default (all
adjustable via `--products` / `--suppliers` / `--customers` flags). Safe to
re-run - it won't collide with data from a previous run.

---

## 7. Known gotchas

- **Only one `.csproj` file should exist** in the project root
  (`The _Fix_WebApp.csproj`). If you ever see a second one appear, delete it -
  `dotnet build` refuses to run with more than one project file in the same
  folder.
- **Migrations aren't bundled with every code update** - if you're pulling
  entity changes, you may need to run `dotnet ef migrations add <Name>` and
  `dotnet ef database update` yourself. Check `dotnet ef migrations list`
  against your database if something looks out of sync (e.g. a column the app
  expects doesn't exist yet).
- **VAT is fixed at 15%**, computed server-side in `Security/TaxSettings.cs` -
  never trust a tax value posted from a client.
- **Data Protection keys** persist to a local `keys/` folder so login sessions
  survive an app restart. Don't delete that folder in the middle of testing,
  or everyone gets logged out.

---

## 8. What's next

Planned but not yet done: Docker packaging for easier team sharing, and a
general UI/visual polish pass (the app is currently functional Bootstrap
styling, not custom-branded).
>>>>>>> 3f3499ae27af550b7554805f721fd80a6ef3d34a
