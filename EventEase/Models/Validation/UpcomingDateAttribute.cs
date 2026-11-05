using System.ComponentModel.DataAnnotations;

namespace EventEase.Models.Validation;

/// <summary>
/// Requires a date that is today or later, and no more than <see cref="MaxYearsAhead"/> years away.
/// [Required] alone does nothing for a non-nullable DateTime, because default(DateTime)
/// (01/01/0001) still counts as "present". This attribute rejects that value and past dates.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class UpcomingDateAttribute : ValidationAttribute
{
    public int MaxYearsAhead { get; set; } = 5;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not DateTime date)
        {
            return new ValidationResult("Please enter a valid date.", MemberNames(validationContext));
        }

        if (date.Date < DateTime.Today)
        {
            return new ValidationResult(ErrorMessage ?? "The event date cannot be in the past.", MemberNames(validationContext));
        }

        if (date > DateTime.Today.AddYears(MaxYearsAhead))
        {
            return new ValidationResult($"The event date must be within {MaxYearsAhead} years.", MemberNames(validationContext));
        }

        return ValidationResult.Success;
    }

    private static string[] MemberNames(ValidationContext context) =>
        context.MemberName is null ? [] : [context.MemberName];
}
