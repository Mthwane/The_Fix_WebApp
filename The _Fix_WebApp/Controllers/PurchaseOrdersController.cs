using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using FashionFix.Web.Services.Courier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Restock requests, end to end: raise (Draft) -> submit (AwaitingApproval) -> a manager approves
/// or rejects -> book a courier collection -> receive stock into the right variants.
///
/// The approval gate is the point of the whole flow: raising a request needs
/// PurchaseOrdersManage, but moving it past AwaitingApproval needs PurchaseOrdersApprove, which
/// Employees deliberately don't have. Stock only ever moves on Receive, never on approval.
/// </summary>
[Authorize(Policy = Permissions.PurchaseOrdersManage)]
public class PurchaseOrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IInventoryService _inventoryService;
    private readonly ICourierService _courierService;

    public PurchaseOrdersController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IInventoryService inventoryService,
        ICourierService courierService)
    {
        _context = context;
        _userManager = userManager;
        _inventoryService = inventoryService;
        _courierService = courierService;
    }

    // GET: /PurchaseOrders?status=
    [HttpGet]
    public async Task<IActionResult> Index(PurchaseOrderStatus? status)
    {
        var query = _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .Include(p => p.CreatedByUser)
            .AsQueryable();

        if (status.HasValue) query = query.Where(p => p.Status == status.Value);

        var orders = await query.OrderByDescending(p => p.DateCreated).ToListAsync();

        ViewBag.StatusCounts = await _context.PurchaseOrders
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        ViewBag.SelectedStatus = status;
        ViewBag.CanApprove = User.HasClaim(Permissions.ClaimType, Permissions.PurchaseOrdersApprove);

        return View(orders);
    }

    // GET: /PurchaseOrders/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.CreatedByUser)
            .Include(p => p.ApprovedByUser)
            .Include(p => p.RestockBundle)
            .Include(p => p.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(p => p.PurchaseOrderId == id);

        if (order is null) return NotFound();

        ViewBag.CanApprove = User.HasClaim(Permissions.ClaimType, Permissions.PurchaseOrdersApprove);
        ViewBag.Shipment = await _context.CourierShipments
            .AsNoTracking()
            .Include(s => s.TrackingEvents)
            .FirstOrDefaultAsync(s => s.PurchaseOrderId == id && s.Status != "cancelled");
        ViewBag.CourierConfigured = _courierService.IsConfigured;

        return View(order);
    }

    // GET: /PurchaseOrders/Create - a blank manual restock request.
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        return View(new PurchaseOrder { DateExpected = DateTime.UtcNow.AddDays(7) });
    }

    // POST: /PurchaseOrders/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int supplierId, DateTime? dateExpected, string? notes, List<int> variantIds, List<int> quantities, List<decimal> unitCosts)
    {
        if (variantIds is null || variantIds.Count == 0)
        {
            this.ToastError("Add at least one line before saving.");
            await PopulateLookupsAsync();
            return View(new PurchaseOrder { SupplierId = supplierId, DateExpected = dateExpected, Notes = notes });
        }

        var order = new PurchaseOrder
        {
            PONumber = await GeneratePoNumberAsync(),
            SupplierId = supplierId,
            CreatedByUserId = _userManager.GetUserId(User)!,
            Status = PurchaseOrderStatus.Draft,
            DateExpected = dateExpected,
            Notes = notes
        };

        for (var i = 0; i < variantIds.Count; i++)
        {
            var qty = i < quantities.Count ? quantities[i] : 0;
            if (qty <= 0) continue; // a zero line is a removed line, not an error

            order.Items.Add(new PurchaseOrderItem
            {
                ProductVariantId = variantIds[i],
                QuantityOrdered = qty,
                UnitCost = i < unitCosts.Count ? unitCosts[i] : 0
            });
        }

        if (order.Items.Count == 0)
        {
            this.ToastError("Every line had a quantity of zero - nothing to order.");
            await PopulateLookupsAsync();
            return View(order);
        }

        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderCreated", $"Raised {order.PONumber} ({order.Items.Count} line(s)).");

        this.ToastSuccess($"{order.PONumber} saved as a draft - submit it when you're ready for approval.");
        return RedirectToAction(nameof(Details), new { id = order.PurchaseOrderId });
    }

    // POST: /PurchaseOrders/Submit/5 - Draft -> AwaitingApproval.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var order = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.PurchaseOrderId == id);
        if (order is null) return NotFound();

        if (!order.IsEditable)
        {
            this.ToastError($"{order.PONumber} can't be submitted from status {order.Status}.");
            return RedirectToAction(nameof(Details), new { id });
        }

        if (order.Items.Count == 0)
        {
            this.ToastError("Add at least one line before submitting.");
            return RedirectToAction(nameof(Details), new { id });
        }

        order.Status = PurchaseOrderStatus.AwaitingApproval;
        order.DateSubmitted = DateTime.UtcNow;
        order.ReviewNotes = null; // clear any previous rejection note on resubmit
        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderSubmitted", $"Submitted {order.PONumber} for approval ({order.TotalCost:C}).");

        this.ToastSuccess($"{order.PONumber} submitted for approval.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/Approve/5 - requires the separate approve permission.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.PurchaseOrdersApprove)]
    public async Task<IActionResult> Approve(int id, string? reviewNotes)
    {
        var order = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.PurchaseOrderId == id);
        if (order is null) return NotFound();

        if (order.Status != PurchaseOrderStatus.AwaitingApproval)
        {
            this.ToastError($"{order.PONumber} isn't awaiting approval (currently {order.Status}).");
            return RedirectToAction(nameof(Details), new { id });
        }

        order.Status = PurchaseOrderStatus.Approved;
        order.ApprovedByUserId = _userManager.GetUserId(User);
        order.DateApproved = DateTime.UtcNow;
        order.ReviewNotes = reviewNotes;
        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderApproved", $"Approved {order.PONumber} ({order.TotalCost:C}).");

        this.ToastSuccess($"{order.PONumber} approved - you can now book a courier collection.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/Reject/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.PurchaseOrdersApprove)]
    public async Task<IActionResult> Reject(int id, string? reviewNotes)
    {
        var order = await _context.PurchaseOrders.FindAsync(id);
        if (order is null) return NotFound();

        if (order.Status != PurchaseOrderStatus.AwaitingApproval)
        {
            this.ToastError($"{order.PONumber} isn't awaiting approval.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // Rejected, not cancelled - the raiser can edit and resubmit rather than start over.
        order.Status = PurchaseOrderStatus.Rejected;
        order.ApprovedByUserId = _userManager.GetUserId(User);
        order.DateApproved = DateTime.UtcNow;
        order.ReviewNotes = reviewNotes;
        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderRejected", $"Rejected {order.PONumber}. Notes: {reviewNotes}");

        this.ToastSuccess($"{order.PONumber} rejected and sent back to the raiser.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/BookCollection/5 - books The Courier Guy to collect from the supplier.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookCollection(int id)
    {
        var order = await _context.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.PurchaseOrderId == id);

        if (order is null) return NotFound();

        if (order.Status != PurchaseOrderStatus.Approved)
        {
            this.ToastError("Only an approved purchase order can be booked for collection.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _courierService.CreatePurchaseOrderShipmentAsync(order);
        if (!result.Success)
        {
            this.ToastError(result.ErrorMessage!);
            return RedirectToAction(nameof(Details), new { id });
        }

        order.Status = PurchaseOrderStatus.Shipped;
        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderShipped", $"Booked collection for {order.PONumber}, waybill {result.Data!.TrackingReference}.");

        this.ToastSuccess($"Collection booked - waybill {result.Data.TrackingReference}.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/Receive/5 - the only place a PO moves stock. Accepts per-line
    // received quantities so a partial delivery is recorded accurately rather than all-or-nothing.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(int id, List<int> itemIds, List<int> receivedQuantities)
    {
        var order = await _context.PurchaseOrders
            .Include(p => p.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(p => p.PurchaseOrderId == id);

        if (order is null) return NotFound();

        if (order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.AwaitingApproval or PurchaseOrderStatus.Rejected)
        {
            this.ToastError("This purchase order hasn't been approved yet.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var stockLines = new List<(int VariantId, int Quantity)>();

        for (var i = 0; i < itemIds.Count; i++)
        {
            var item = order.Items.FirstOrDefault(x => x.PurchaseOrderItemId == itemIds[i]);
            if (item is null) continue;

            var receivingNow = i < receivedQuantities.Count ? receivedQuantities[i] : 0;
            if (receivingNow <= 0) continue;

            // Never accept more than was ordered on a line - a genuine over-delivery should be
            // raised as a separate adjustment so it's visible, not absorbed silently here.
            receivingNow = Math.Min(receivingNow, item.QuantityOutstanding);
            if (receivingNow <= 0) continue;

            item.QuantityReceived += receivingNow;
            stockLines.Add((item.ProductVariantId, receivingNow));
        }

        if (stockLines.Count == 0)
        {
            this.ToastError("Nothing to receive - enter a quantity against at least one line.");
            return RedirectToAction(nameof(Details), new { id });
        }

        await _inventoryService.IncrementStockBatchAsync(stockLines, InventoryChangeReason.PurchaseOrderReceived);

        // Fully received only when every line is satisfied; otherwise it stays open for the rest.
        var fullyReceived = order.Items.All(i => i.QuantityOutstanding == 0);
        if (fullyReceived)
        {
            order.Status = PurchaseOrderStatus.Received;
            order.DateReceived = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderReceived",
            $"Received {stockLines.Sum(l => l.Quantity)} unit(s) against {order.PONumber}{(fullyReceived ? " (complete)" : " (partial)")}.");

        this.ToastSuccess(fullyReceived
            ? $"{order.PONumber} fully received and stock updated."
            : $"Partial delivery recorded against {order.PONumber} - the rest stays outstanding.");

        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /PurchaseOrders/Cancel/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.PurchaseOrderId == id);
        if (order is null) return NotFound();

        if (order.Status == PurchaseOrderStatus.Received)
        {
            this.ToastError("A received purchase order can't be cancelled - process a return instead.");
            return RedirectToAction(nameof(Details), new { id });
        }

        if (order.Items.Any(i => i.QuantityReceived > 0))
        {
            this.ToastError("Stock has already been received against this order - it can't be cancelled.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // Cancel any live courier booking too, so we're not billed for a collection we don't want.
        var shipment = await _context.CourierShipments.FirstOrDefaultAsync(s => s.PurchaseOrderId == id && s.Status != "cancelled");
        if (shipment is not null)
        {
            var cancelResult = await _courierService.CancelShipmentAsync(shipment.CourierShipmentId);
            if (!cancelResult.Success)
                this.ToastWarning($"Purchase order cancelled, but the courier booking could not be: {cancelResult.ErrorMessage}");
        }

        order.Status = PurchaseOrderStatus.Cancelled;
        await _context.SaveChangesAsync();
        await LogAuditAsync("PurchaseOrderCancelled", $"Cancelled {order.PONumber}.");

        this.ToastSuccess($"{order.PONumber} cancelled.");
        return RedirectToAction(nameof(Index));
    }

    // ===================== helpers =====================

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Suppliers = await _context.Suppliers.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();

        ViewBag.Variants = await _context.ProductVariants.AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product.IsActive)
            .OrderBy(v => v.Product.Name).ThenBy(v => v.Size)
            .ToListAsync();
    }

    private async Task<string> GeneratePoNumberAsync()
    {
        var prefix = $"PO-{DateTime.UtcNow:yyyyMM}-";
        var existing = await _context.PurchaseOrders
            .Where(p => p.PONumber.StartsWith(prefix))
            .Select(p => p.PONumber)
            .ToListAsync();

        var set = existing.ToHashSet();
        for (var i = existing.Count + 1; ; i++)
        {
            var candidate = $"{prefix}{i:D4}";
            if (!set.Contains(candidate)) return candidate;
        }
    }

    private async Task LogAuditAsync(string action, string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = action,
            Details = details
        });
        await _context.SaveChangesAsync();
    }
}
