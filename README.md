# Fashion Fix - Store Management System

A full-stack ASP.NET Core MVC application for managing a fashion retail store:
product catalogue, point-of-sale, purchase orders/suppliers, returns, staff and
role management, business reporting, and a customer-facing storefront with
online checkout. Built against the product backlog user stories (US-01 through
US-20) in the original requirements document, for APDP201 (Applications
Development Project 2B) Sprint 1.

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
- **Customer storefront** - browse, cart, checkout with **Paystack** payment
  integration (test mode), order tracking, order cancellation, "My Profile"
  self-service (with a visible Customer ID to give staff at the till).
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
| Postman (optional) | Used for the API test collection - see section 9. |

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

> Note: `Program.cs` now calls `Database.Migrate()` on startup, so migrations
> are applied automatically the moment the app boots - this was previously
> missing and caused an `Invalid object name 'AspNetRoles'` error on a fresh
> database.

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
Services/        IInventoryService, IEmailSender, IPaymentService, SessionCart.
Security/        Permissions.cs (the permission catalog) and TaxSettings.cs (VAT rate).
wwwroot/         Static assets - site.css, site.js (POS scanning + cart JS).
```

Controllers: `Account`, `Home`, `Customer`, `Shop`, `Products`, `Pos`,
`Orders`, `Returns`, `PurchaseOrders`, `Suppliers`, `Employees`, `Roles`,
`Reports`, `Payments`.

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

## 7. Payment integration (Paystack)

Checkout is wired to **Paystack** in test mode:

- `IPaymentService` / `PaystackPaymentService` handle transaction initialization.
- `ShopController.Checkout` initializes a Paystack transaction and redirects
  the customer to Paystack's hosted payment page.
- `PaymentsController.Callback` verifies the payment server-side before it
  creates the `Order`, decrements stock, and sends the confirmation email.

Add your Paystack test secret/public keys to `appsettings.json` (or
user-secrets) before testing checkout end-to-end.

---

## 8. What's done so far

- Full folder structure, EF Core entities + relationships, DbContext, Identity
  + role seeding, controllers wired to their views.
- Branded login pages (customer + employee), registration, role/permission
  management.
- Online storefront: browse, cart, checkout.
- **Paystack payment integration** (test mode) end-to-end: initialize ->
  redirect -> callback verification -> order creation -> stock decrement ->
  confirmation email.
- Fixed startup bug: missing `Database.Migrate()` call in `Program.cs` that
  caused `Invalid object name 'AspNetRoles'` on a fresh database.
- Fixed a Razor bug in `Confirmation.cshtml` where `@item.Quantity` was
  rendering as literal text instead of the value.
- POS (`PosController`) confirmed as a genuine second, in-person sales
  channel alongside the online store - not leftover/contradictory spec.
- Staff order fulfillment flow (Processing -> Shipped -> Delivered / cancel
  + restock).
- Returns processing wired to inventory restock.
- Employee creation flow (`EmployeesController`): validation, duplicate
  checks, role assignment, audit log entries.
- Customer self-registration flow, assigns "Customer" role, signs user in.
- Postman API test collection ("Fashion Fix payment API Tests") started:
  - **Test 1** - successful Paystack initialize: **passed 4/4**.

---

## 9. Incomplete / in progress

**API & database testing (active work):**
- Test 2 - invalid key, expect 401 response: **in progress**.
- Test 3 - own `/Payments/Callback` endpoint: **not started**.
- Database-level tests still outstanding: stock decrement correctness,
  order integrity, foreign-key constraint checks.

**Features still marked `// TODO` in code:**
- Employee **Edit**/deactivate form (Create is done, Edit is not).
- Full audit log coverage on login/product/sale events (role-change and
  employee-create are already logged; other events are not yet).
- POS barcode-scan JS wiring (`site.js`) and cart -> `CartItems`
  serialization.
- Receipt email/SMS delivery beyond the current basic confirmation email.
- Reports: best-sellers, stock turnover, revenue-by-category, PDF export
  (CSV export works; PDF via iTextSharp is planned but not built).
- Purchase order create form + receiving logic - entities are modelled but
  the `Receive` action is currently a no-op stub.
- Return refund/store-credit ledger hookup (the processing + restock side
  already works; the actual refund/credit accounting does not).

**Not started:**
- Docker packaging for easier team sharing.
- General UI/visual polish pass - currently functional Bootstrap styling,
  not custom-branded.

> Reminder for Sprint 1: the rubric only requires 50-65% functionality
> completed for full marks on that criterion, so not everything above needs
> to be finished before submission - just prioritise what maps to the
> highest-weighted rubric items (UI/UX, MVC architecture & functionality).

---

## 10. Known gotchas

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

