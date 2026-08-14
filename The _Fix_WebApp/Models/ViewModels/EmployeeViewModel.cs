using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

/// <summary>
/// Backs the "Add Employee" form (US-15). Roles offered exclude "Customer" -
/// this form only ever creates staff accounts.
/// </summary>
public class EmployeeViewModel
{
    [Required(ErrorMessage = "Full name is required")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Job position is required")]
    [Display(Name = "Job Position")]
    public string JobPosition { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role is required")]
    [Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Temporary password is required")]
    [DataType(DataType.Password)]
    [Display(Name = "Temporary Password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Roles selectable for staff accounts (Customer is excluded - that's self-registration only).</summary>
    public static readonly string[] AssignableRoles = { "Administrator", "Manager", "Employee", "Owner" };
}

/// <summary>Backs the "Edit Employee" form - updates profile fields without touching login credentials.</summary>
public class EmployeeEditViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Job Position")]
    public string? JobPosition { get; set; }

    [Display(Name = "Employment Status")]
    public string? EmploymentStatus { get; set; }

    [Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public static readonly string[] EmploymentStatuses = { "Active", "On Leave", "Terminated" };
}
