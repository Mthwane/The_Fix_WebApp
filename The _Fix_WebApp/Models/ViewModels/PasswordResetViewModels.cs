using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter the email address on your account")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required] public string UserId { get; set; } = string.Empty;
    [Required] public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
