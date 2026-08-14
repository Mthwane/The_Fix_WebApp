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
