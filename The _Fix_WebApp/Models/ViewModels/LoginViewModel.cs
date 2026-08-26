using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Username is required")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    /// <summary>Distinguishes the "Login" vs "Login as Employee" buttons shown on the page.</summary>
    public bool IsEmployeeLogin { get; set; }

    public string? ReturnUrl { get; set; }
}
