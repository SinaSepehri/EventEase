using System.ComponentModel.DataAnnotations;

namespace EventEase.Models;

/// <summary>
/// The data a user enters in the Registration Form.
/// </summary>
public class Registration
{
    public const int MaxTicketsPerRegistration = 10;

    [Range(1, int.MaxValue, ErrorMessage = "A valid event is required.")]
    public int EventId { get; set; }

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 60 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [RegularExpression(ValidationPatterns.Email, ErrorMessage = "Please enter a valid email address.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional. Validated only when filled in.</summary>
    [RegularExpression(ValidationPatterns.Phone, ErrorMessage = "Please enter a valid phone number (7–15 digits).")]
    public string? Phone { get; set; }

    [Range(1, MaxTicketsPerRegistration, ErrorMessage = "You can register between 1 and 10 tickets.")]
    public int Tickets { get; set; } = 1;

    // [Range] on a bool is the standard DataAnnotations way to say "must be checked".
    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the event terms to register.")]
    public bool AcceptTerms { get; set; }

    /// <summary>Keep the user signed in on this device after registering.</summary>
    public bool RememberMe { get; set; } = true;

    public void Normalize()
    {
        FullName = FullName?.Trim() ?? string.Empty;
        Email = Email?.Trim() ?? string.Empty;
        Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();
    }
}
