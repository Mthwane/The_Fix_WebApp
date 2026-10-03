namespace FashionFix.Web.Infrastructure;

/// <summary>Shared validation patterns so every form enforces the same rules.</summary>
public static class InputRules
{
    /// <summary>
    /// Letters (Latin incl. accented), spaces, apostrophes, hyphens and full stops. 2-100 chars. No digits.
    /// NOTE: written with explicit \u ranges instead of \p{L} on purpose - this same string is emitted to the
    /// browser for jQuery validation, and JavaScript regexes without the "u" flag don't understand \p{L},
    /// which made valid names like "Samuel Jackson" fail client-side.
    /// </summary>
    public const string NamePattern = @"^[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF\u0100-\u017F][A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF\u0100-\u017F '.\-]{1,99}$";
    public const string NameMessage = "Use letters only (spaces, ' - . allowed), 2-100 characters.";

    /// <summary>Job titles: letters, spaces and . ' & / - ; 2-60 chars (e.g. "Sales Associate", "Buyer & Merchandiser").</summary>
    public const string JobPositionPattern = @"^[A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF][A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF '.&/\-]{1,59}$";
    public const string JobPositionMessage = "Use letters only (spaces and ' . & / - allowed), 2-60 characters.";

    /// <summary>Optional leading +, then 9-15 digits (spaces allowed between).</summary>
    public const string PhonePattern = @"^\+?[0-9][0-9 ]{7,18}[0-9]$";
    public const string PhoneMessage = "Enter a valid phone number (digits only, e.g. 0821234567 or +27821234567).";

    /// <summary>Usernames: letters, digits, dot, underscore, hyphen; 3-30 chars.</summary>
    public const string UsernamePattern = @"^[A-Za-z0-9._\-]{3,30}$";
    public const string UsernameMessage = "3-30 characters: letters, numbers, . _ - only.";
}
