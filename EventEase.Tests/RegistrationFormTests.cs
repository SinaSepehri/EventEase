using Bunit;
using EventEase.Components;
using EventEase.Models;
using EventEase.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EventEase.Tests;

/// <summary>The Registration Form must catch bad input and handle errors gracefully.</summary>
public class RegistrationFormTests : BunitContext
{
    private readonly FakeBrowserStorage storage;

    public RegistrationFormTests() => storage = this.AddEventEase();

    private EventService Events => Services.GetRequiredService<EventService>();
    private AttendanceService Attendance => Services.GetRequiredService<AttendanceService>();
    private UserSessionState Session => Services.GetRequiredService<UserSessionState>();

    private IRenderedComponent<RegistrationForm> RenderForm(int eventId, Action<RegistrationRecord>? onRegistered = null) =>
        Render<RegistrationForm>(p => p
            .Add(c => c.Event, Events.GetById(eventId)!)
            .Add(c => c.OnRegistered, (RegistrationRecord r) => onRegistered?.Invoke(r)));

    private static void Fill(IRenderedComponent<RegistrationForm> cut, string name, string email, string? phone = null, int tickets = 1, bool terms = true)
    {
        cut.Find("#fullName").Change(name);
        cut.Find("#email").Change(email);
        if (phone is not null) cut.Find("#phone").Change(phone);
        cut.Find("#tickets").Change(tickets.ToString());
        cut.Find("#acceptTerms").Change(terms);
    }

    [Fact]
    public void Empty_submit_shows_all_errors_and_stores_nothing()
    {
        var cut = RenderForm(2);

        cut.Find("form").Submit();

        Assert.Contains("Please fix the following", cut.Markup);
        Assert.Contains("Full name is required.", cut.Markup);
        Assert.Contains("Email is required.", cut.Markup);
        Assert.Contains("You must accept the event terms", cut.Markup);
        Assert.Equal(0, Attendance.Count);
        Assert.False(Session.IsSignedIn);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("@example.com")]
    public void Invalid_email_is_rejected(string email)
    {
        var cut = RenderForm(2);
        Fill(cut, "Test User", email);
        cut.Find("form").Submit();

        Assert.Contains("Please enter a valid email address.", cut.Markup);
        Assert.Equal(0, Attendance.Count);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12")]
    [InlineData("555-CALL-NOW")]
    public void Invalid_phone_is_rejected(string phone)
    {
        var cut = RenderForm(2);
        Fill(cut, "Test User", "test@example.com", phone);
        cut.Find("form").Submit();

        Assert.Contains("Please enter a valid phone number", cut.Markup);
        Assert.Equal(0, Attendance.Count);
    }

    [Fact]
    public void Unchecked_terms_block_registration()
    {
        var cut = RenderForm(2);
        Fill(cut, "Test User", "test@example.com", terms: false);
        cut.Find("form").Submit();

        Assert.Contains("You must accept the event terms", cut.Markup);
        Assert.Equal(0, Attendance.Count);
    }

    [Fact]
    public void Whitespace_name_is_rejected_after_trimming()
    {
        var cut = RenderForm(2);
        Fill(cut, "    ", "test@example.com");
        cut.Find("form").Submit();

        Assert.Contains("Full name is required.", cut.Markup);
    }

    [Fact]
    public void Unparseable_ticket_count_shows_message()
    {
        var cut = RenderForm(2);
        cut.Find("#tickets").Change("two");
        Assert.Contains("Tickets must be a whole number.", cut.Markup);
    }

    [Fact]
    public void More_tickets_than_seats_left_is_reported_on_the_tickets_field()
    {
        Events.GetById(2)!.RegisteredCount = Events.GetById(2)!.Capacity - 2; // 2 seats left
        var cut = RenderForm(2);
        Fill(cut, "Test User", "test@example.com", tickets: 5);
        cut.Find("form").Submit();

        Assert.Contains("Only 2 seat(s) are left", cut.Markup);
        Assert.Equal(0, Attendance.Count);
    }

    [Fact]
    public void Valid_registration_succeeds_and_starts_a_session()
    {
        RegistrationRecord? received = null;
        var cut = RenderForm(2, r => received = r);
        Fill(cut, "  Test User ", "test@example.com", "+1 (555) 123-4567", tickets: 2);

        cut.Find("form").Submit();

        Assert.NotNull(received);
        Assert.Equal("Test User", received!.FullName);
        Assert.Equal("+1 (555) 123-4567", received.Phone);
        Assert.Equal(1, Attendance.Count);
        Assert.True(Session.IsSignedIn);
        Assert.Equal("test@example.com", Session.Email);
        Assert.True(storage.Has(StorageArea.Local, UserSessionState.StorageKey));
    }

    [Fact]
    public void Remember_me_off_keeps_session_in_tab_only()
    {
        var cut = RenderForm(2);
        Fill(cut, "Test User", "test@example.com");
        cut.Find("#rememberMe").Change(false);
        cut.Find("form").Submit();

        Assert.True(storage.Has(StorageArea.Session, UserSessionState.StorageKey));
        Assert.False(storage.Has(StorageArea.Local, UserSessionState.StorageKey));
    }

    [Fact]
    public async Task Duplicate_registration_is_reported_on_the_email_field()
    {
        await Attendance.RegisterAsync(TestServices.ValidRegistration(2));
        var cut = RenderForm(2);
        Fill(cut, "Test User", "TEST@example.com");

        cut.Find("form").Submit();

        Assert.Contains("already registered for this event", cut.Markup);
        Assert.Equal(1, Attendance.Count);
    }

    [Fact]
    public void Service_error_clears_when_user_edits_again()
    {
        Events.GetById(2)!.RegisteredCount = Events.GetById(2)!.Capacity - 1;
        var cut = RenderForm(2);
        Fill(cut, "Test User", "test@example.com", tickets: 3);
        cut.Find("form").Submit();
        Assert.Contains("Only 1 seat(s)", cut.Markup);

        cut.Find("#tickets").Change("1");
        Assert.DoesNotContain("Only 1 seat(s)", cut.Markup);
    }

    [Fact]
    public async Task Form_is_prefilled_from_the_session()
    {
        await Session.SignInAsync("Signed In User", "signed@example.com", true);
        var cut = RenderForm(2);

        Assert.Equal("Signed In User", cut.Find("#fullName").GetAttribute("value"));
        Assert.Equal("signed@example.com", cut.Find("#email").GetAttribute("value"));
    }

    [Fact]
    public void Broken_storage_still_lets_user_register()
    {
        storage.Throw = true;
        RegistrationRecord? received = null;
        var cut = RenderForm(2, r => received = r);
        Fill(cut, "Test User", "test@example.com");

        cut.Find("form").Submit();

        Assert.NotNull(received);
        Assert.True(Session.IsSignedIn);
        Assert.DoesNotContain("Something went wrong", cut.Markup);
    }

    [Fact]
    public void Event_that_disappeared_shows_a_message_instead_of_crashing()
    {
        var ghost = Events.GetById(2)!.Clone();
        ghost.Id = 424242; // not in the service
        var cut = Render<RegistrationForm>(p => p.Add(c => c.Event, ghost));
        Fill(cut, "Test User", "test@example.com");

        cut.Find("form").Submit();

        Assert.Contains("This event does not exist.", cut.Markup);
    }
}
