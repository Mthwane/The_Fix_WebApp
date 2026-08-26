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
