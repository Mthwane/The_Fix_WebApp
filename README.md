<div align="center">

<img src="banner.jpg" alt="Fashion Fix — Retail without friction." width="100%" />

<br /><br />

<p>
  A full-stack ASP.NET Core MVC and SQL Server retail platform unifying an online customer storefront,<br />
  in-store Point of Sale, Paystack checkout, inventory control, and dynamic role-based permissions.
</p>

<p>
  <img src="https://img.shields.io/badge/status-in%20development-D96B27?style=flat-square" alt="Finished" />
  <img src="https://img.shields.io/badge/.NET-ASP.NET%20Core%208-512BD4?style=flat-square" alt=".NET: ASP.NET Core 8" />
  <img src="https://img.shields.io/badge/SQL%20Server-2022-CC2927?style=flat-square" alt="SQL Server 2022" />
  <img src="https://img.shields.io/badge/Payments-Paystack-0BA4DB?style=flat-square" alt="Payments: Paystack" />
  <a href="www.linkedin.com/in/okuhle-malishe-6b6994410">
    <img src="https://img.shields.io/badge/LinkedIn-Okuhle%20Malishe-0A66C2?style=flat-square" alt="LinkedIn: Okuhle Malishe" />
  </a>
</p>

</div>

---

## Core Highlights

- **Dual Portals & Dynamic RBAC:** Separate Customer and Employee login portals backed by ASP.NET Core Identity, with runtime-configurable roles and permissions.
- **E-Commerce & Paystack Checkout:** Product browsing, shopping cart, server-side verified Paystack payments, automatic stock deduction, order tracking, and self-service profiles.
- **In-Store Point of Sale (POS):** Dedicated physical sales channel with cart management, automatic server-side 15% VAT calculation, and digital receipts.
- **Order Fulfillment & Inventory:** Full product catalogue CRUD, low-stock dashboard alerts, end-to-end order lifecycle (`Processing → Shipped → Delivered`), and auto-restocking on cancellations and returns.
- **Operations & Reporting:** Staff and supplier management, audit logging, live dashboard stats, CSV analytics export, real SMTP email notifications, and audio-enabled UI toasts.

## Tech Stack

| Layer | Technologies |
| --- | --- |
| **Backend** | C#, ASP.NET Core MVC (.NET 8), Entity Framework Core, ASP.NET Core Identity |
| **Database** | SQL Server |
| **Integrations** | Paystack API (Test Mode), SMTP Email |
| **Frontend** | Razor Views, Bootstrap, JavaScript |

## Quick Start

1. **Configure SQL Server** in `appsettings.json` (`ConnectionStrings:DefaultConnection`) and add your Paystack test keys (plus optional SMTP credentials).
2. **Restore and run** (migrations and default roles apply automatically on startup):
   ```bash
   dotnet restore
   dotnet run
