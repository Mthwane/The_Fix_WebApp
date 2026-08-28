using Microsoft.AspNetCore.Mvc;

namespace FashionFix.Web.Controllers;

/// <summary>
/// Every state-changing action (create/edit/deactivate/checkout/etc.) calls this so the user
/// always gets an on-screen confirmation - success or failure - instead of a silent redirect.
/// Rendered as a Bootstrap toast in Views/Shared/_Layout.cshtml.
/// </summary>
public static class ControllerToastExtensions
{
    public static void ToastSuccess(this Controller controller, string message)
        => SetToast(controller, message, "success");

    public static void ToastError(this Controller controller, string message)
        => SetToast(controller, message, "danger");

    public static void ToastWarning(this Controller controller, string message)
        => SetToast(controller, message, "warning");

    private static void SetToast(Controller controller, string message, string type)
    {
        // TempData persists across the redirect that follows most POST actions.
        controller.TempData["ToastMessage"] = message;
        controller.TempData["ToastType"] = type;
    }
}
