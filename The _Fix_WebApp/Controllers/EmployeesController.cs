using FashionFix.Web.Data;
using FashionFix.Web.Models.Entities;
using FashionFix.Web.Models.ViewModels;
using FashionFix.Web.Security;
using FashionFix.Web.Services.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace FashionFix.Web.Controllers;

[Authorize]
public class EmployeesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EmployeesController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // GET: /Employees - lists staff accounts (US-15).
    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Index()
    {

        // Staff = any user NOT solely in the Customer role. Previously this called
        // GetRolesAsync per user (twice - once to filter, once to build the label), which is
        // 2N+1 round trips for N users. One joined query gets every user's roles at once.
        var allUsers = await _context.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync();

        var rolesByUserId = (await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName ?? string.Empty).ToList());

        var employees = allUsers
            .Where(u => rolesByUserId.TryGetValue(u.Id, out var roles) && roles.Any(r => r != "Customer"))
            .ToList();

        var roleLookup = employees.ToDictionary(
            e => e.Id,
            e => string.Join(", ", rolesByUserId.TryGetValue(e.Id, out var roles) ? roles : new List<string>()));

        ViewBag.Roles = roleLookup;
        ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();

        return View(employees);
    }

    // GET: /Employees/ExportRoster - CSV download of the current staff directory.
    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> ExportRoster()
    {
        var allUsers = await _context.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync();
        var rolesByUserId = (await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName ?? string.Empty).ToList());

        var staff = allUsers.Where(u => rolesByUserId.TryGetValue(u.Id, out var roles) && roles.Any(r => r != "Customer"));

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("FullName,Username,Email,Role,JobPosition,EmploymentStatus,DateHired,IsActive,TwoFactorEnabled");
        foreach (var u in staff)
        {
            var role = rolesByUserId.TryGetValue(u.Id, out var roles) ? string.Join("/", roles.Where(r => r != "Customer")) : "";
            string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            csv.AppendLine(string.Join(",", Csv(u.FullName), Csv(u.UserName), Csv(u.Email), Csv(role),
                Csv(u.JobPosition), Csv(u.EmploymentStatus), Csv(u.DateHired?.ToString("yyyy-MM-dd")), u.IsActive, u.TwoFactorEnabled));
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"staff-roster-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>Suggestions for the Job Position combo box: a seed list plus whatever titles are already in use, so a new title typed once is offered next time.</summary>
    private async Task<List<string>> GetJobPositionOptionsAsync()
    {
        var seed = new[] { "Store Manager", "Assistant Manager", "Floor Manager", "Sales Associate", "Cashier", "Stock Controller", "Visual Merchandiser", "Customer Support Agent" };
        var used = await _userManager.Users.AsNoTracking()
            .Where(u => u.JobPosition != null && u.JobPosition != "")
            .Select(u => u.JobPosition!)
            .Distinct()
            .ToListAsync();
        return seed.Union(used, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>All roles except "Customer" - customers self-register and are never assigned via this screen.</summary>
    private async Task<List<string>> GetAssignableRoleNamesAsync()
    {
        return await Task.FromResult(_roleManager.Roles
            .Where(r => r.Name != "Customer")
            .Select(r => r.Name!)
            .OrderBy(n => n)
            .ToList());
    }

    // GET: /Employees/Create
    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> CreateEmployee()
    {
        ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
        ViewBag.JobPositions = await GetJobPositionOptionsAsync();
        return View(new EmployeeViewModel());
    }

    // POST: /Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> CreateEmployee(EmployeeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            ViewBag.JobPositions = await GetJobPositionOptionsAsync();
            return View(model);
        }

        var existingByUsername = await _userManager.FindByNameAsync(model.Username);
        if (existingByUsername is not null)
        {
            ModelState.AddModelError(nameof(model.Username), "That username is already taken.");
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            ViewBag.JobPositions = await GetJobPositionOptionsAsync();
            return View(model);
        }

        var existingByEmail = await _userManager.FindByEmailAsync(model.Email);
        if (existingByEmail is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            ViewBag.JobPositions = await GetJobPositionOptionsAsync();
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Username,
            Email = model.Email,
            FullName = model.FullName,
            JobPosition = model.JobPosition,
            EmploymentStatus = "Active",
            DateHired = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = true,
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            ViewBag.JobPositions = await GetJobPositionOptionsAsync();
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, model.Role);

      

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeCreated",
            Details = $"Created {model.Role} account for '{user.UserName}' ({model.JobPosition}).",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was created as a {model.Role}.");
        return RedirectToAction(nameof(Index));
    }
   

    // POST: /Employees/AssignRole - role/permission assignment (US-16).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> AssignRole(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var previousRoles = string.Join(", ", currentRoles);

        var rolesToRemove = currentRoles.Where(r => r != "Customer").ToList();
        await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
        await _userManager.AddToRoleAsync(user, role);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "RoleAssigned",
            Details = $"Changed '{user.UserName}' role from [{previousRoles}] to '{role}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' is now a {role}.");
        return RedirectToAction(nameof(Index));
    }

    // GET: /Employees/Edit/{id} - update staff profile details (US-15).
    [HttpGet]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        var model = new EmployeeEditViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            JobPosition = user.JobPosition,
            EmploymentStatus = user.EmploymentStatus ?? "Active",
            Role = roles.FirstOrDefault(r => r != "Customer") ?? roles.FirstOrDefault() ?? string.Empty,
            IsActive = user.IsActive
        };

        ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
        ViewBag.JobPositions = await GetJobPositionOptionsAsync();
        return View(model);
    }
    // POST: /Employees/Edit/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Edit(string id, EmployeeEditViewModel model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            ViewBag.JobPositions = await GetJobPositionOptionsAsync();
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing is not null && existing.Id != user.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already registered to another account.");
                ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
                ViewBag.JobPositions = await GetJobPositionOptionsAsync();
                return View(model);
            }
            await _userManager.SetEmailAsync(user, model.Email);
        }

        user.FullName = model.FullName;
        user.JobPosition = model.JobPosition;
        user.EmploymentStatus = model.EmploymentStatus;
        user.IsActive = model.IsActive;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            ViewBag.AssignableRoles = await GetAssignableRoleNamesAsync();
            ViewBag.JobPositions = await GetJobPositionOptionsAsync();
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.Role))
        {
            var currentRoles = (await _userManager.GetRolesAsync(user)).Where(r => r != "Customer").ToList();
            if (!currentRoles.Contains(model.Role))
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, model.Role);
            }
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeUpdated",
            Details = $"Updated profile for '{user.UserName}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was updated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Employees/Deactivate/{id} - soft-deactivate a staff account (never a hard delete,
    // consistent with Product Control's "deactivate without permanently deleting" rule).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = false;
        user.EmploymentStatus = "Terminated";
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeDeactivated",
            Details = $"Deactivated staff account '{user.UserName}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was deactivated.");
        return RedirectToAction(nameof(Index));
    }

    // POST: /Employees/Reactivate/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public async Task<IActionResult> Reactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = true;
        user.EmploymentStatus = "Active";
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "EmployeeReactivated",
            Details = $"Reactivated staff account '{user.UserName}'.",
        });
        await _context.SaveChangesAsync();

        this.ToastSuccess($"'{user.UserName}' was reactivated.");
        return RedirectToAction(nameof(Index));
    }


    // GET: /Employees/AuditLogs - admin-only audit trail (NFR-11): search, period, category chips, paging.
    [Authorize(Policy = Permissions.AuditLogsView)]
    [HttpGet]
    public async Task<IActionResult> AuditLogs(string? q, string? category, DateTime? from, DateTime? to, int page = 1)
    {
        const int pageSize = 25;
        category = string.IsNullOrWhiteSpace(category) ? "all" : category;

        var filtered = BuildAuditQuery(q, category, from, to);
        var totalFiltered = await filtered.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalFiltered / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var logs = await filtered
            .Include(a => a.User)
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.AuditLogId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        // Role badge for each actor on this page (one query).
        var userIds = logs.Where(l => l.UserId != null).Select(l => l.UserId!).Distinct().ToList();
        var roleRows = await (from ur in _context.UserRoles
                              join r in _context.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId)
                              select new { ur.UserId, r.Name }).ToListAsync();
        string[] rolePriority = { "Administrator", "Owner", "Manager", "Employee", "Customer" };
        string RoleFor(string? uid)
        {
            var names = roleRows.Where(x => x.UserId == uid).Select(x => x.Name ?? "").ToList();
            return rolePriority.FirstOrDefault(names.Contains) ?? names.FirstOrDefault() ?? "";
        }

        var rows = logs.Select(l =>
        {
            var cat = AuditCategories.For(l.Action);
            var sast = AuditTime.ToSast(l.Timestamp);
            var hasUser = l.User is not null;
            var name = hasUser ? l.User!.FullName
                : (l.Action.StartsWith("Login", StringComparison.Ordinal) ? "Unknown visitor" : "System process");
            return new AuditRowViewModel
            {
                Id = l.AuditLogId,
                Date = sast.ToString("yyyy/MM/dd"),
                Time = sast.ToString("HH:mm:ss"),
                ActorName = name,
                ActorInitials = hasUser ? string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0]))) : "SYS",
                ActorRole = hasUser ? RoleFor(l.UserId) : "Automated",
                ActorIsSystem = !hasUser,
                Action = l.Action,
                CategoryLabel = cat.Label,
                CategoryCss = cat.Css,
                Details = l.Details ?? string.Empty,
                IpAddress = l.IpAddress,
                Hash = l.Hash
            };
        }).ToList();

        // One grouped query feeds every stat card and chip count.
        var actionCounts = await _context.AuditLogs.AsNoTracking()
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync();

        var categoryCounts = new Dictionary<string, int> { ["all"] = actionCounts.Sum(x => x.Count) };
        foreach (var c in AuditCategories.All.Append(AuditCategories.Other))
            categoryCounts[c.Key] = 0;
        foreach (var x in actionCounts)
            categoryCounts[AuditCategories.For(x.Action).Key] += x.Count;

        var sealedCount = await _context.AuditLogs.CountAsync(a => a.Hash != null);
        var since = DateTime.UtcNow.AddHours(-24);
        var head = await _context.AuditLogs.AsNoTracking()
            .Where(a => a.Hash != null).OrderByDescending(a => a.AuditLogId).Select(a => a.Hash).FirstOrDefaultAsync();

        var model = new AuditLogsPageViewModel
        {
            Rows = rows,
            Q = q,
            Category = category,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
            TotalFiltered = totalFiltered,
            CategoryCounts = categoryCounts,
            Stats = new AuditStatsViewModel
            {
                Total = categoryCounts["all"],
                Last24Hours = await _context.AuditLogs.CountAsync(a => a.Timestamp >= since),
                Logistics = categoryCounts["logistics"],
                AutoBooked = actionCounts.Where(x => x.Action.StartsWith("AutoBooked", StringComparison.Ordinal)).Sum(x => x.Count),
                AuthEvents = categoryCounts["auth"],
                FailedSignIns = actionCounts.Where(x => x.Action == "LoginFailed" || x.Action == "LoginLockedOut").Sum(x => x.Count),
                Sealed = sealedCount,
                Legacy = categoryCounts["all"] - sealedCount,
                HeadHash = head
            }
        };

        return View(model);
    }

    // GET: /Employees/ExportAuditLogs - CSV of whatever the current filters show (capped at 50,000 rows).
    [Authorize(Policy = Permissions.AuditLogsView)]
    [HttpGet]
    public async Task<IActionResult> ExportAuditLogs(string? q, string? category, DateTime? from, DateTime? to)
    {
        const int cap = 50_000;
        var logs = await BuildAuditQuery(q, string.IsNullOrWhiteSpace(category) ? "all" : category, from, to)
            .Include(a => a.User)
            .OrderBy(a => a.AuditLogId)
            .Take(cap)
            .ToListAsync();

        // Cells starting with = + - @ are prefixed so Excel can't run them as formulas.
        static string Csv(string? v)
        {
            v ??= string.Empty;
            if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0) v = "'" + v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Id,Timestamp (SAST),Actor,Username,Action,Category,Details,IP,PreviousHash,Hash");
        foreach (var l in logs)
        {
            sb.AppendLine(string.Join(',',
                l.AuditLogId,
                Csv(AuditTime.ToSast(l.Timestamp).ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(l.User?.FullName),
                Csv(l.User?.UserName),
                Csv(l.Action),
                Csv(AuditCategories.For(l.Action).Label),
                Csv(l.Details),
                Csv(l.IpAddress),
                Csv(l.PreviousHash),
                Csv(l.Hash)));
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _userManager.GetUserId(User),
            Action = "AuditExported",
            Details = $"Exported {logs.Count} audit row(s) to CSV."
        });
        await _context.SaveChangesAsync();

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"fashionfix-audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    // GET: /Employees/VerifyAuditLedger - recomputes the SHA-256 chain; read-only, returns JSON for the page.
    [Authorize(Policy = Permissions.AuditLogsView)]
    [HttpGet]
    public async Task<IActionResult> VerifyAuditLedger([FromServices] AuditLedgerService ledger)
    {
        var r = await ledger.VerifyAsync();
        return Json(new
        {
            valid = r.IsValid,
            sealedCount = r.SealedCount,
            legacyCount = r.LegacyCount,
            validCount = r.ValidCount,
            head = r.HeadHash,
            issues = r.Issues.Select(i => new { id = i.AuditLogId, problem = i.Problem }),
            verifiedAt = AuditTime.ToSast(r.VerifiedAtUtc).ToString("yyyy/MM/dd HH:mm:ss")
        });
    }

    private IQueryable<AuditLog> BuildAuditQuery(string? q, string? category, DateTime? from, DateTime? to)
    {
        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(a =>
                a.Action.Contains(q) ||
                (a.Details != null && a.Details.Contains(q)) ||
                (a.IpAddress != null && a.IpAddress.Contains(q)) ||
                (a.User != null && (a.User.FullName.Contains(q) || (a.User.UserName != null && a.User.UserName.Contains(q)))));
        }

        query = AuditCategories.Filter(query, category);

        if (from.HasValue)
        {
            var fromUtc = AuditTime.SastDayStartToUtc(from.Value);
            query = query.Where(a => a.Timestamp >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = AuditTime.SastDayStartToUtc(to.Value).AddDays(1); // inclusive of the whole "to" day
            query = query.Where(a => a.Timestamp < toUtc);
        }

        return query;
    }

}
