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

Still TODO (marked with `// TODO` / `<!-- TODO -->` throughout):
- The branded login page UI (in progress separately).
- RegisterViewModel + registration flow.
- EmployeeViewModel + employee create/edit forms.
- AuditLog writes on login/product/sale/role-change events.
- POS barcode-scan JS wiring (site.js) and cart -> CartItems serialization.
- Receipt email/SMS delivery.
- Reports: best-sellers, stock turnover, revenue-by-category, PDF/CSV export.
- Purchase order UI (Supplier/PurchaseOrder entities already modelled).
- Returns & exchanges UI (ReturnTransaction entity already modelled).
- First EF Core migration (run once SQL Server is available).
