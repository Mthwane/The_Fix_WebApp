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
    /// <summary>
    /// Accepts "100.70", "100,70", "1 234,50", "1,234.50" and "1.234,50". The previous
    /// implementation used NumberStyles.Number with the invariant culture, which treats a comma as a
    /// THOUSANDS separator - so a user typing "100,70" got 10070. Rule: when both separators appear,
    /// the LAST one is the decimal mark; when only a comma appears it is a decimal mark unless it is
    /// followed by exactly three digits and nothing else after a leading group (e.g. "1,234").
    /// </summary>
    public static bool TryParseFlexible(string raw, out decimal result)
    {
        result = 0;
        var v = raw.Trim().Replace("\u00A0", "").Replace(" ", "").Replace("R", "").Replace("r", "");
        if (v.Length == 0) return false;

        var lastDot = v.LastIndexOf('.');
        var lastComma = v.LastIndexOf(',');

        if (lastDot >= 0 && lastComma >= 0)
        {
            var decimalMark = lastDot > lastComma ? '.' : ',';
            var groupMark = decimalMark == '.' ? ',' : '.';
            v = v.Replace(groupMark.ToString(), "").Replace(decimalMark, '.');
        }
        else if (lastComma >= 0)
        {
            var commaCount = v.Count(c => c == ',');
            var digitsAfter = v.Length - lastComma - 1;
            // "1,234" or "1,234,567" -> thousands; "100,7" / "100,70" -> decimal.
            var looksLikeThousands = commaCount > 1 || (digitsAfter == 3 && lastComma > 0 && lastComma <= 3);
            v = looksLikeThousands ? v.Replace(",", "") : v.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            var dotCount = v.Count(c => c == '.');
            if (dotCount > 1) v = v.Replace(".", "");
        }

        return decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out result);
    }

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

        if (TryParseFlexible(value, out var parsed))
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
