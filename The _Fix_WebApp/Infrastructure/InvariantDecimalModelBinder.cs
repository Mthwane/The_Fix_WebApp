using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FashionFix.Web.Infrastructure;

/// <summary>
/// Parses posted decimal/decimal? values with InvariantCulture, always - regardless of the
/// server's actual current culture.
///
/// Why this exists: HTML5 &lt;input type="number"&gt; and every bit of JS in this app that
/// formats a money value (toFixed(2), template strings, etc.) always produce a period as the
/// decimal separator - that's the HTML spec, not locale-dependent. ASP.NET Core's DEFAULT
/// decimal model binder, though, parses incoming form values using CultureInfo.CurrentCulture.
/// On a server whose locale uses a comma as the decimal separator (en-ZA does - see the
/// correctly-localized "R0,00" display elsewhere in the app), a posted value like "640.99"
/// then genuinely fails to parse ("The value '640.99' is not valid for X"), because a period
/// isn't a valid decimal separator in that culture.
///
/// This affects every decimal-bound form field in the app, not just one screen - POS checkout
/// (UnitPrice/DiscountTotal), Product Create/Edit (CostPrice/SellingPrice/PriceOverride),
/// Purchase Orders/Restock Bundles (UnitCost), Pricing (MarkupPercentage), shift floats, and
/// so on - so the fix is registered once, globally, in Program.cs, rather than patched field
/// by field. DISPLAY formatting (ToString("C") etc.) is untouched and still correctly localized
/// - only the parsing of values coming back FROM a submitted form changes.
/// </summary>
public class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            // Let [Required]/nullable-handling behave the same as the framework default would.
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) != null)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            return Task.CompletedTask;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(modelName,
                $"The value '{value}' is not a valid number for {bindingContext.ModelMetadata.GetDisplayName()}.");
        }

        return Task.CompletedTask;
    }
}

public class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Metadata.ModelType == typeof(decimal) || context.Metadata.ModelType == typeof(decimal?))
        {
            return new InvariantDecimalModelBinder();
        }

        return null;
    }
}
