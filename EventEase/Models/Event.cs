using System.ComponentModel.DataAnnotations;
using EventEase.Models.Validation;

namespace EventEase.Models;

/// <summary>
/// An event managed by EventEase (corporate or social).
/// </summary>
public class Event : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Event id must be a positive number.")]
    public int Id { get; set; }

    // [Required] also rejects whitespace-only strings (AllowEmptyStrings = false by default).
    [Required(ErrorMessage = "Event name is required.")]
    [StringLength(80, MinimumLength = 3, ErrorMessage = "Event name must be between 3 and 80 characters.")]
    public string Name { get; set; } = string.Empty;

    [UpcomingDate(MaxYearsAhead = 5)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Location is required.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Location must be between 2 and 120 characters.")]
    public string Location { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [EnumDataType(typeof(EventCategory), ErrorMessage = "Unknown event category.")]
    public EventCategory Category { get; set; } = EventCategory.Corporate;

    [Range(1, 10_000, ErrorMessage = "Capacity must be between 1 and 10,000.")]
    public int Capacity { get; set; } = 50;

    [Range(0, int.MaxValue, ErrorMessage = "Registered count cannot be negative.")]
    public int RegisteredCount { get; set; }

    public int SeatsLeft => Math.Max(0, Capacity - RegisteredCount);

    public bool IsFull => SeatsLeft == 0;

    public bool IsPast => Date < DateTime.Now;

    /// <summary>
    /// Shallow copy used for editing so changes can be cancelled
    /// without mutating the original instance.
    /// </summary>
    public Event Clone() => (Event)MemberwiseClone();

    /// <summary>Trims user-entered text so "  Gala  " and "Gala" are stored the same way.</summary>
    public void Normalize()
    {
        Name = Name?.Trim() ?? string.Empty;
        Location = Location?.Trim() ?? string.Empty;
        Description = Description?.Trim() ?? string.Empty;
    }

    // Rules that involve more than one property.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RegisteredCount > Capacity)
        {
            yield return new ValidationResult(
                "Capacity cannot be lower than the number of people already registered.",
                [nameof(Capacity)]);
        }
    }
}

public enum EventCategory
{
    Corporate,
    Social
}
