using System.ComponentModel.DataAnnotations;
using FashionFix.Web.Security;

namespace FashionFix.Web.Models.ViewModels;

public class RoleListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PermissionCount { get; set; }
    public int MemberCount { get; set; }
    public bool IsProtected { get; set; }
}

/// <summary>Backs the Create/Edit Role form - a name plus a checklist of permissions.</summary>
public class RoleEditViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Role name is required")]
    [MaxLength(256)]
    [Display(Name = "Role Name")]
    public string Name { get; set; } = string.Empty;

    public bool IsProtected { get; set; }

    public List<string> SelectedPermissions { get; set; } = new();

    /// <summary>The full permission catalog, for rendering the checklist.</summary>
    public IReadOnlyDictionary<string, string> AllPermissions => Permissions.All;
}

/// <summary>One role's summary card + column in the permission matrix.</summary>
public class RoleMatrixColumnViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsProtected { get; set; }
    public int MemberCount { get; set; }
    public HashSet<string> GrantedPermissions { get; set; } = new();
}

/// <summary>Backs Roles/Index - the full all-roles x all-permissions matrix, edited and saved
/// as one unit instead of one role at a time.</summary>
public class PermissionMatrixViewModel
{
    public List<RoleMatrixColumnViewModel> Roles { get; set; } = new();

    /// <summary>The full permission catalog (key -> display label), in stable order for rows.</summary>
    public IReadOnlyDictionary<string, string> AllPermissions => Permissions.All;
}
