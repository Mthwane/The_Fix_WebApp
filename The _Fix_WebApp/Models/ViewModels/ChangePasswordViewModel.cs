using System.ComponentModel.DataAnnotations;

namespace FashionFix.Web.Models.ViewModels;

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password")]
    [DataType(DataType.Password)]
    [Display(Name = "Current Password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New Password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation don't match.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
