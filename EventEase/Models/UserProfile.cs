using System.ComponentModel.DataAnnotations;

namespace EventEase.Models;

/// <summary>Who is using the app in this browser session. Used by the sign-in form.</summary>
public class UserProfile
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 60 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [RegularExpression(ValidationPatterns.Email, ErrorMessage = "Please enter a valid email address.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = true;

    public void Normalize()
    {
        FullName = FullName?.Trim() ?? string.Empty;
        Email = Email?.Trim() ?? string.Empty;
    }
}

/// <summary>Shared validation patterns, so every form applies the same rules.</summary>
public static class ValidationPatterns
{
    // [EmailAddress] only checks for an '@'; this also requires a domain with a dot.
    public const string Email = @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$";

    // Optional phone: digits, spaces, +, -, (, ), and 7-15 digits in total.
    public const string Phone = @"^\+?[0-9\s\-()]{7,20}$";
}
