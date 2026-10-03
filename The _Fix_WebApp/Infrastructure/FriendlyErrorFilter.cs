using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Infrastructure;

/// <summary>
/// Safety net for POST actions. Before this, a database error while saving (two staff editing the
/// same record, a value that doesn't fit a column, a stale row) bubbled up as an unhandled exception
/// and the user saw a crash page even though nothing was wrong with their input. Now the failure is
/// logged and the user is sent back to the page they came from with a toast that says what to do.
/// Only handles known recoverable database exceptions for non-AJAX POSTs; anything else still goes to
/// the normal error page so real bugs are never hidden.
/// </summary>
public class FriendlyErrorFilter : IExceptionFilter
{
    private readonly ILogger<FriendlyErrorFilter> _logger;
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public FriendlyErrorFilter(ILogger<FriendlyErrorFilter> logger, ITempDataDictionaryFactory tempDataFactory)
    {
        _logger = logger;
        _tempDataFactory = tempDataFactory;
    }

    public void OnException(ExceptionContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method)) return;
        if (request.Headers.XRequestedWith == "XMLHttpRequest") return; // let AJAX callers see the real status

        string? message = context.Exception switch
        {
            DbUpdateConcurrencyException =>
                "Someone else changed this record while you were editing it. Nothing was lost - please reload the page and try again.",
            DbUpdateException =>
                "The change could not be saved. A value may be too long, duplicated, or still in use elsewhere. Please check it and try again.",
            _ => null
        };
        if (message is null) return;

        _logger.LogError(context.Exception, "Recoverable database error on {Method} {Path}", request.Method, request.Path);

        var tempData = _tempDataFactory.GetTempData(context.HttpContext);
        tempData["ToastMessage"] = message;
        tempData["ToastType"] = "danger";

        var referer = request.Headers.Referer.ToString();
        var back = !string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri)
                   && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            ? uri.PathAndQuery
            : "/";

        context.Result = new LocalRedirectResult(back);
        context.ExceptionHandled = true;
    }
}
