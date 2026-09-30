using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FashionFix.Web.Controllers;

[Authorize(Roles = "Customer")]
public class RewardsController : Controller
{
    private readonly IRewardsService _rewards;
    private readonly UserManager<ApplicationUser> _userManager;

    public RewardsController(IRewardsService rewards, UserManager<ApplicationUser> userManager)
    {
        _rewards = rewards;
        _userManager = userManager;
    }

    // GET: /Rewards
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var settings = await _rewards.GetSettingsAsync();
        var balance = await _rewards.GetBalanceAsync(userId);

        return View(new RewardsCustomerViewModel
        {
            Balance = balance,
            BalanceValue = _rewards.PointsToRands(settings, balance),
            Settings = settings,
            EarnRule = _rewards.DescribeEarnRule(settings),
            History = await _rewards.GetHistoryAsync(userId)
        });
    }
}