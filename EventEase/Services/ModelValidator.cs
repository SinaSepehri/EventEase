using System.ComponentModel.DataAnnotations;

namespace EventEase.Services;

/// <summary>
/// Runs the same DataAnnotations rules the forms use, so the service never stores invalid
/// data, even when it's called directly (not through an EditForm).
/// </summary>
public static class ModelValidator
{
    public static bool TryValidate(object model, out List<ValidationResult> errors)
    {
        errors = [];
        return Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
    }
}
