[README.md](https://github.com/user-attachments/files/31116647/README.md)
# The Fix (FashionFix) – Store Management System

An ASP.NET Core 8 MVC web app for running a fashion retail store: product
catalogue, point-of-sale, purchase orders, returns, staff/role management,
a customer-facing shop, and reporting — all backed by SQL Server and
ASP.NET Core Identity.

Repository: https://github.com/Mthwane/The_Fix_WebApp

---

## 1. What the app does

The app has **two audiences** that sign in through two different doors:

| Audience | Signs in via | Lands on |
|---|---|---|
| **Staff** (Administrator, Manager, Employee, Owner) | `Account/EmployeeLogin` | `Home/Dashboard` |
| **Customers** | the login form on the home page (`Home/Index`) | `Customer/Orders` |

### Staff-side modules (each mapped to a controller)

| Module | Controller | What it's for |
|---|---|---|
| Dashboard | `HomeController` | Business overview stats after login |
| Products | `ProductsController` | Add / edit / deactivate catalogue items, SKUs, pricing, stock |
| Point of Sale | `PosController` | In-store checkout — scan/select items, take payment, print a receipt |
| Orders | `OrdersController` | View orders placed through the online shop, advance status, cancel |
| Returns | `ReturnsController` | Look up an order by number, process a return/exchange, restock or write off |
| Purchase Orders | `PurchaseOrdersController` | Create POs to suppliers, mark them received (restocks inventory) |
| Suppliers | `SuppliersController` | Manage the supplier list used by Purchase Orders |
| Employees | `EmployeesController` | Create staff accounts, assign roles, edit, deactivate/reactivate, view audit logs |
| Roles & Permissions | `RolesController` | Create custom roles and tick which permissions each role has |
| Reports | `ReportsController` | Sales/revenue, inventory, and employee reports; CSV/PDF export |

### Customer-side modules

| Module | Controller | What it's for |
|---|---|---|
| Shop | `ShopController` | Browse/search products, add to cart, checkout |
| My Orders | `CustomerController` | View order history, cancel an order, edit profile |
| Account | `AccountController` | Register, login/logout, change password |

### How permissions actually work (important for setup and support)

Nothing in the app checks a **role name** directly (except the customer-only
areas, which use `[Authorize(Roles = "Customer")]`). Every staff controller
instead checks a **permission policy**, e.g. `[Authorize(Policy =
Permissions.ProductsManage)]`. Roles are just named bundles of permissions
that live in the database and can be edited from the **Roles & Permissions**
screen at runtime — so an Administrator can create a brand-new role (say,
"Cashier") and hand it exactly the permissions it needs, with zero code
changes or redeploys.

The full permission catalogue (`Security/Permissions.cs`):

- `products.manage`, `pos.use`, `returns.process`, `purchaseorders.manage`,
  `suppliers.manage`, `employees.manage`, `roles.manage`, `orders.manage`,
  `dashboard.view`, `reports.view`, `auditlogs.view`

Default permission bundles seeded for the built-in roles the *first* time
each role is created (an Administrator's later customizations on the Roles
screen are never overwritten):

- **Administrator** – every permission (and this is re-guaranteed on every
  app startup, so an Administrator can never accidentally lock themselves out)
- **Manager** – products, POS, returns, purchase orders, suppliers,
  dashboard, reports, orders
- **Employee** – POS, returns, dashboard, orders
- **Owner** – dashboard, reports, purchase orders, suppliers
- **Customer** – none (customers use the self-service shop, not the
  permission-gated staff screens)

---

## 2. Logins

### First-time staff login (seeded automatically on first run)

The app seeds one Administrator account the very first time it starts up
against a fresh database, because there'd otherwise be no way to create the
first staff member at all:

| Field | Value |
|---|---|
| Username | `admin` |
| Password | `Ch4ngeMe!Now` |
| Sign in via | `Account/EmployeeLogin` (the **Employee Login** page, not the customer login on the home page) |

**Change this password immediately after your first login** — go to your
profile menu → **Change Password**. In Development, the console also logs a
warning with these credentials as a reminder every time the app starts and
this seed account still exists.

### Creating more staff accounts

Only an Administrator (or anyone holding the `employees.manage` permission)
can create staff accounts, from **Employees → Create Employee**. This
assigns a role (Administrator/Manager/Employee/Owner), which in turn grants
whatever permissions that role currently has.

### Customer accounts

Anyone can self-register as a customer from the home page (**Register**).
Customer accounts always get the `Customer` role and land on their order
history after signing in. Staff accounts cannot sign in through the
customer login form, and customer accounts cannot sign in through
`Account/EmployeeLogin` — the app checks this and rejects the mismatched
attempt with a clear error message.

---

## 3. Getting it running from Visual Studio

### Prerequisites

- **Visual Studio 2022** (17.8+) with the **ASP.NET and web development**
  workload, or VS Code + the .NET 8 SDK
- **.NET 8 SDK**
- **SQL Server** — LocalDB (installed automatically with the Visual Studio
  workload above) is enough for local development; SQL Server Express or a
  full instance also works
- Git

### Step 1 — Clone the repo

```bash
git clone https://github.com/Mthwane/The_Fix_WebApp.git
cd The_Fix_WebApp
```

Open the solution/`.csproj` in Visual Studio (**File → Open → Project/Solution**,
select `The _Fix_WebApp.csproj`), or run everything from a terminal in the
project folder — both are covered below.

### Step 2 — Point it at your database

Open `appsettings.json` and check the connection string under
`ConnectionStrings:DefaultConnection`:

```json
"DefaultConnection": "Server=localhost;Database=FashionFixDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
```

- If you're using **LocalDB**, change `Server=localhost` to
  `Server=(localdb)\\mssqllocaldb` (this is the most common setup for a
  fresh Visual Studio install and needs no separate SQL Server install).
- If you're pointing at a real SQL Server instance (including a remote/Azure
  one), use that server's connection details instead — `appsettings.SqlServer.example.json`
  in the repo has a template for a SQL Server/Azure SQL connection string.
- Don't commit real credentials to `appsettings.json`. For anything beyond
  local development, use **.NET user-secrets** (right-click the project in
  Visual Studio → **Manage User Secrets**) or environment variables instead.

> **Security note:** the copy of `appsettings.json` in this project currently
> has live SMTP credentials committed under the `Email` section. Treat that
> password as compromised — rotate/regenerate it (e.g. revoke and reissue the
> Gmail App Password) and move real credentials to user-secrets or
> environment variables rather than the checked-in file, both locally and
> especially before deploying anywhere public.

### Step 3 — Restore packages

Visual Studio does this automatically on open. From the command line:

```bash
dotnet restore
```

### Step 4 — Create/update the database (EF Core)

The repo already includes two migrations (`InitialCreate` and
`AddProductDescription`), so you just need to **apply** them — you don't
need to add a new migration unless you've changed a model.

**From the Visual Studio Package Manager Console** (Tools → NuGet Package
Manager → Package Manager Console; make sure the project is selected as the
Default project):

```powershell
Update-Database
```

**Or from a terminal**, using the EF Core CLI tool (install it once if you
don't have it: `dotnet tool install --global dotnet-ef`):

```bash
dotnet ef database update
```

This creates the `FashionFixDb` database (Identity tables, Products, Orders,
Suppliers, PurchaseOrders, Returns, AuditLogs, etc.) and applies both
migrations. You only need to run this once per database — after that, just
rerun it whenever a new migration is added to the repo.

If you ever change an entity class and need a **new** migration:

```powershell
Add-Migration YourMigrationName   # Package Manager Console
# or
dotnet ef migrations add YourMigrationName   # CLI
```
then `Update-Database` / `dotnet ef database update` again to apply it.

### Step 5 — Run the app

**In Visual Studio:** press **F5** (or the green ▶ Run button), with either
the `https` or `http` launch profile selected in the toolbar dropdown.

**From the command line:**

```bash
dotnet run
```

By default it listens on:
- `https://localhost:7160`
- `http://localhost:5238`

The browser opens automatically to the home page, which is the **customer
login / branded landing page**. To reach the staff dashboard, go to
`/Account/EmployeeLogin` and sign in with the seeded `admin` account (see
section 2), or use the **Employee Login** link if one is present on the
landing page.

### Step 6 — First login and cleanup

1. Sign in at `/Account/EmployeeLogin` with `admin` / `Ch4ngeMe!Now`.
2. Immediately go to **Change Password** and set a real password.
3. Create real staff accounts under **Employees → Create Employee**, then
   consider deactivating or repurposing the seeded `admin` account.
4. Review **Roles & Permissions** and adjust what each role can do, if the
   defaults in section 1 don't match how your store operates.

---

## 4. Notes on configuration

- **Data protection keys** (used to encrypt the auth cookie) persist to a
  `keys/` folder next to the project, so signed-in sessions survive app
  restarts. Don't delete that folder in production, or everyone gets
  signed out.
- **Password policy**: minimum 10 characters, requires upper/lowercase, a
  digit, and a non-alphanumeric character. Accounts lock for 10 minutes
  after 5 failed attempts.
- **Session/cookie lifetime**: sign-in cookie lasts 8 hours with sliding
  expiration; the shopping cart session lasts 2 hours idle.
- **Email**: optional. Leave `Email:Host` blank in `appsettings.json` to
  disable email entirely — the app works fine without it. If you do want
  email (e.g. order confirmations), fill in real SMTP credentials via
  user-secrets/environment variables rather than the committed file (see
  the security note in Step 2).
- **Low stock threshold**: `InventorySettings:DefaultLowStockThreshold`
  controls when a product is flagged as low stock (default `5`).

### Deploying beyond your own machine

The repo includes `DEPLOYMENT.md` with a full walkthrough for deploying to
an Ubuntu ARM (Oracle Cloud Ampere A1) server behind Nginx with Let's
Encrypt, plus ready-made `deploy/fashionfix.service` (systemd) and
`deploy/nginx-fashionfix.conf` files. That guide targets a PostgreSQL
setup for ARM compatibility — if you deploy to a normal x86 Linux or
Windows server instead, you can keep the SQL Server setup this repo ships
with by default; just point `ConnectionStrings:DefaultConnection` at your
production SQL Server/Azure SQL instance the same way you did for local
development.

---

## 5. Known gaps (per the repo's own README/TODOs)

This is an actively evolving project. As of this snapshot, the following
are still placeholders or in progress — check the repo's `README.md` for
the latest status:

- Receipt email/SMS delivery after a POS sale
- POS barcode-scan JS wiring
- Reports: best-sellers / stock-turnover breakdowns, PDF export
- Return refund/store-credit ledger (return processing + restock already work)

---

## 6. Quick reference — common commands

```bash
# Clone
git clone https://github.com/Mthwane/The_Fix_WebApp.git
cd The_Fix_WebApp

# Restore
dotnet restore

# Apply database migrations
dotnet ef database update

# Run
dotnet run

# Add a new migration after changing a model
dotnet ef migrations add <MigrationName>
dotnet ef database update
```
