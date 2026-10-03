namespace FashionFix.Web.Infrastructure;

/// <summary>Shared validation patterns so every form enforces the same rules.</summary>
public static class InputRules
{
    /// <summary>Letters (any language), spaces, apostrophes, hyphens and full stops. 2-100 chars. No digits.</summary>
    public const string NamePattern = @"^\p{L}[\p{L} '.\-]{1,99}$";
    public const string NameMessage = "Use letters only (spaces, ' - . allowed), 2-100 characters.";

    /// <summary>Optional leading +, then 9-15 digits (spaces allowed between).</summary>
    public const string PhonePattern = @"^\+?[0-9][0-9 ]{7,18}[0-9]$";
    public const string PhoneMessage = "Enter a valid phone number (digits only, e.g. 0821234567 or +27821234567).";

    /// <summary>Usernames: letters, digits, dot, underscore, hyphen; 3-30 chars.</summary>
    public const string UsernamePattern = @"^[A-Za-z0-9._\-]{3,30}$";
    public const string UsernameMessage = "3-30 characters: letters, numbers, . _ - only.";
}
