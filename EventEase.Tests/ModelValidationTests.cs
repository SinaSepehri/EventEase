using EventEase.Models;
using EventEase.Services;

namespace EventEase.Tests;

/// <summary>Edge cases for the validation rules on Event and Registration.</summary>
public class ModelValidationTests
{
    private static Event ValidEvent() => new()
    {
        Id = 1,
        Name = "Valid Event",
        Date = DateTime.Today.AddDays(10),
        Location = "Seattle",
        Capacity = 100,
        RegisteredCount = 10
    };

    private static List<string> ErrorsFor(object model)
    {
        ModelValidator.TryValidate(model, out var errors);
        return errors.SelectMany(e => e.MemberNames).ToList();
    }

    [Fact]
    public void Valid_event_passes() => Assert.Empty(ErrorsFor(ValidEvent()));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]          // shorter than 3
    [InlineData(null)]
    public void Invalid_name_fails(string? name)
    {
        var evt = ValidEvent();
        evt.Name = name!;
        Assert.Contains(nameof(Event.Name), ErrorsFor(evt));
    }

    [Fact]
    public void Name_longer_than_80_fails()
    {
        var evt = ValidEvent();
        evt.Name = new string('x', 81);
        Assert.Contains(nameof(Event.Name), ErrorsFor(evt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Empty_location_fails(string? location)
    {
        var evt = ValidEvent();
        evt.Location = location!;
        Assert.Contains(nameof(Event.Location), ErrorsFor(evt));
    }

    [Fact]
    public void Default_date_fails()
    {
        // Before the fix, [Required] accepted 01/01/0001 because DateTime is never null.
        var evt = ValidEvent();
        evt.Date = default;
        Assert.Contains(nameof(Event.Date), ErrorsFor(evt));
    }

    [Fact]
    public void Past_date_fails()
    {
        var evt = ValidEvent();
        evt.Date = DateTime.Today.AddDays(-1);
        Assert.Contains(nameof(Event.Date), ErrorsFor(evt));
    }

    [Fact]
    public void Date_more_than_five_years_ahead_fails()
    {
        var evt = ValidEvent();
        evt.Date = DateTime.Today.AddYears(6);
        Assert.Contains(nameof(Event.Date), ErrorsFor(evt));
    }

    [Fact]
    public void Today_is_a_valid_date()
    {
        var evt = ValidEvent();
        evt.Date = DateTime.Today.AddHours(23);
        Assert.Empty(ErrorsFor(evt));
    }

    [Fact]
    public void Capacity_below_registered_count_fails()
    {
        var evt = ValidEvent();
        evt.Capacity = 5;
        evt.RegisteredCount = 10;
        Assert.Contains(nameof(Event.Capacity), ErrorsFor(evt));
    }

    [Fact]
    public void Normalize_trims_text()
    {
        var evt = ValidEvent();
        evt.Name = "  Gala  ";
        evt.Location = "\tNYC ";
        evt.Normalize();
        Assert.Equal("Gala", evt.Name);
        Assert.Equal("NYC", evt.Location);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(11)]
    public void Invalid_ticket_count_fails(int tickets)
    {
        var reg = new Registration { EventId = 1, FullName = "Test User", Email = "test@example.com", Tickets = tickets };
        Assert.Contains(nameof(Registration.Tickets), ErrorsFor(reg));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]                 // passed [EmailAddress] alone
    [InlineData("two@@example.com")]
    [InlineData("spaces in@example.com")]
    public void Invalid_email_fails(string email)
    {
        var reg = new Registration { EventId = 1, FullName = "Test User", Email = email };
        Assert.Contains(nameof(Registration.Email), ErrorsFor(reg));
    }

    [Fact]
    public void Valid_registration_passes()
    {
        var reg = new Registration { EventId = 1, FullName = "Test User", Email = "test@example.com", Tickets = 2, AcceptTerms = true };
        Assert.Empty(ErrorsFor(reg));
    }
}
