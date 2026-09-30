using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Controllers;

/// <summary>Configures the rewards programme. Gated by its own permission (Permissions.RewardsManage),
/// not ProductsManage - this sets how much money the store gives back.</summary>
[Authorize(Policy = Permissions.RewardsManage)]
public class RewardsAdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IRewardsService _rewards;
    private readonly UserManager<ApplicationUser> _userManager;

    public RewardsAdminController(ApplicationDbContext context, IRewardsService rewards, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _rewards = rewards;
        _userManager = userManager;
    }

    // GET: /RewardsAdmin
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await _rewards.GetSettingsAsync();
        var model = new RewardsSettingsViewModel
        {
            IsEnabled = settings.IsEnabled,
            EarnMode = settings.EarnMode,
            FlatPointsPerPurchase = settings.FlatPointsPerPurchase,
            PointsPerRand = settings.PointsPerRand,
            CashBackPercentage = settings.CashBackPercentage,
            PointValueRands = settings.PointValueRands,
            MinimumOrderAmount = settings.MinimumOrderAmount,
            MinimumPointsToRedeem = settings.MinimumPointsToRedeem,
            MaxRedeemPercentOfOrder = settings.MaxRedeemPercentOfOrder
        };
        await FillDerivedAsync(model, settings);
        return View(model);
    }

    // POST: /RewardsAdmin
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(RewardsSettingsViewModel model)
    {
        if (!ModelState.IsValid)
        {
            this.ToastError("Settings not saved - please fix the highlighted values.");
            await FillDerivedAsync(model, await _rewards.GetSettingsAsync());
            return View(model);
        }

        var entity = await _context.RewardsSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (entity is null)
        {
            entity = new RewardsSettings { Id = 1 };
            _context.RewardsSettings.Add(entity);
        }

        entity.IsEnabled = model.IsEnabled;
        entity.EarnMode = model.EarnMode;
        entity.FlatPointsPerPurchase = model.FlatPointsPerPurchase;
        entity.PointsPerRand = model.PointsPerRand;
        entity.CashBackPercentage = model.CashBackPercentage;
        entity.PointValueRands = model.PointValueRands;
        entity.MinimumOrderAmount = model.MinimumOrderAmount;
        entity.MinimumPointsToRedeem = model.MinimumPointsToRedeem;
        entity.MaxRedeemPercentOfOrder = model.MaxRedeemPercentOfOrder;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByUserId = _userManager.GetUserId(User);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = entity.UpdatedByUserId,
            Action = "RewardsSettingsUpdated",
            Details = $"Rewards {(model.IsEnabled ? "ON" : "OFF")}; mode {model.EarnMode} " +
                      $"(flat {model.FlatPointsPerPurchase}, per R1 {model.PointsPerRand}, cash-back {model.CashBackPercentage}%); " +
                      $"1 point = {model.PointValueRands:C}; min order {model.MinimumOrderAmount:C}; " +
                      $"min redeem {model.MinimumPointsToRedeem} pts; max {model.MaxRedeemPercentOfOrder}% of order."
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess(model.IsEnabled
            ? "Rewards settings saved. Changes apply to new orders from now on."
            : "Rewards settings saved. The programme is currently OFF - nobody earns or redeems.");
        return RedirectToAction(nameof(Index));
    }

    private async Task FillDerivedAsync(RewardsSettingsViewModel model, RewardsSettings savedSettings)
    {
        model.Overview = await _rewards.GetOverviewAsync();
        model.Preview = new[] { 100m, 500m, 1000m }
            .Select(amount =>
            {
                var points = _rewards.CalculateEarnedPoints(savedSettings, amount);
                return new EarnPreviewRow { PurchaseAmount = amount, Points = points, Value = _rewards.PointsToRands(savedSettings, points) };
            })
            .ToList();
    }
}