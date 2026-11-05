using Bunit;
using EventEase.Components;
using EventEase.Pages;
using EventEase.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EventEase.Tests;

/// <summary>Pages that rely on shared state: session continuity across components, attendance tracking.</summary>
public class PageTests : BunitContext
{
    private readonly FakeBrowserStorage storage;

    public PageTests() => storage = this.AddEventEase();

    private AttendanceService Attendance => Services.GetRequiredService<AttendanceService>();
    private UserSessionState Session => Services.GetRequiredService<UserSessionState>();

    [Fact]
    public async Task Session_status_follows_sign_in_and_out_live()
    {
        var cut = Render<SessionStatus>();
        Assert.Contains("Sign in", cut.Markup);

        await cut.InvokeAsync(() => Session.SignInAsync("Test User", "test@example.com", true));
        Assert.Contains("Signed in as", cut.Markup);
        Assert.Contains("Test User", cut.Markup);

        cut.Find("button").Click(); // Sign out
        cut.WaitForAssertion(() => Assert.Contains("Sign in", cut.Markup));
    }

    [Fact]
    public async Task Session_restored_from_storage_is_shown_after_reload()
    {
        // Simulate a previous visit that saved a session.
        var (_, previous, _) = TestServices.Create(storage);
        await previous.SignInAsync("Returning User", "back@example.com", true);

        await Session.InitializeAsync(); // what Program.cs does at startup
        var cut = Render<SessionStatus>();

        Assert.Contains("Returning User", cut.Markup);
    }

    [Fact]
    public async Task Register_page_remembers_existing_registration_across_pages()
    {
        await Session.SignInAsync("Test User", "test@example.com", true);
        await Attendance.RegisterAsync(TestServices.ValidRegistration(2, tickets: 2));

        var register = Render<Register>(p => p.Add(c => c.Id, 2));
        Assert.Contains("already registered for this event with 2 ticket(s)", register.Markup);
        Assert.Empty(register.FindAll("form"));

        var details = Render<EventDetails>(p => p.Add(c => c.Id, 2));
        Assert.Contains("You're registered with 2 ticket(s)", details.Markup);
    }

    [Fact]
    public async Task Register_someone_else_shows_an_empty_form()
    {
        await Session.SignInAsync("Test User", "test@example.com", true);
        await Attendance.RegisterAsync(TestServices.ValidRegistration(2));

        var cut = Render<Register>(p => p.Add(c => c.Id, 2));
        cut.FindAll("button").Single(b => b.TextContent.Contains("someone else")).Click();

        Assert.Equal("", cut.Find("#email").GetAttribute("value"));
    }

    [Fact]
    public void Full_registration_flow_on_register_page()
    {
        var cut = Render<Register>(p => p.Add(c => c.Id, 2));
        cut.Find("#fullName").Change("Flow User");
        cut.Find("#email").Change("flow@example.com");
        cut.Find("#acceptTerms").Change(true);
        cut.Find("form").Submit();

        Assert.Contains("You are registered!", cut.Markup);
        Assert.True(Session.IsSignedIn);
    }

    [Fact]
    public void My_registrations_asks_for_details_when_signed_out()
    {
        var cut = Render<MyRegistrations>();
        cut.Find("form").Submit();

        Assert.Contains("Full name is required.", cut.Markup);
        Assert.False(Session.IsSignedIn);

        cut.Find("#signInName").Change("Test User");
        cut.Find("#signInEmail").Change("test@example.com");
        cut.Find("form").Submit();

        Assert.True(Session.IsSignedIn);
        Assert.Contains("You have no registrations yet", cut.Markup);
    }

    [Fact]
    public async Task My_registrations_lists_only_the_current_users_records_and_can_cancel()
    {
        await Attendance.RegisterAsync(TestServices.ValidRegistration(1, "me@example.com"));
        await Attendance.RegisterAsync(TestServices.ValidRegistration(2, "someone@example.com"));
        await Session.SignInAsync("Me", "me@example.com", true);

        var cut = Render<MyRegistrations>();
        Assert.Contains("Tech Leaders Summit", cut.Markup);
        Assert.DoesNotContain("Summer Rooftop Gala", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent == "Cancel").Click();
        cut.FindAll("button").Single(b => b.TextContent == "Yes, cancel").Click();

        cut.WaitForAssertion(() => Assert.Contains("Cancelled", cut.Markup));
        Assert.True(Attendance.GetForEmail("me@example.com")[0].IsCancelled);
    }

    [Fact]
    public async Task Attendance_tracker_checks_people_in_and_updates_stats()
    {
        await Attendance.RegisterAsync(TestServices.ValidRegistration(4, "a@example.com", 2));
        await Attendance.RegisterAsync(TestServices.ValidRegistration(4, "b@example.com", 2));

        var cut = Render<EventAttendance>(p => p.Add(c => c.EventId, 4));
        Assert.Contains("0%", cut.Markup);

        cut.FindAll("button").First(b => b.TextContent == "Check in").Click();

        cut.WaitForAssertion(() => Assert.Contains("50%", cut.Markup));
        Assert.Single(cut.FindAll("button"), b => b.TextContent == "Undo");
    }

    [Fact]
    public async Task Attendance_tracker_filters_by_status_and_search()
    {
        var a = (await Attendance.RegisterAsync(TestServices.ValidRegistration(4, "alice@example.com"))).Record!;
        await Attendance.RegisterAsync(TestServices.ValidRegistration(4, "bob@example.com"));
        await Attendance.CheckInAsync(a.Id);

        var cut = Render<EventAttendance>(p => p.Add(c => c.EventId, 4));

        cut.Find("select").Change("checked-in");
        Assert.Contains("alice@example.com", cut.Markup);
        Assert.DoesNotContain("bob@example.com", cut.Markup);

        cut.Find("select").Change("all");
        cut.Find("#attendeeSearch").Input("bob");
        Assert.Contains("bob@example.com", cut.Markup);
        Assert.DoesNotContain("alice@example.com", cut.Markup);
    }

    [Fact]
    public void Attendance_tracker_for_unknown_event_is_not_found()
    {
        var raised = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => raised = true;

        Render<EventAttendance>(p => p.Add(c => c.EventId, 999));

        Assert.True(raised);
    }

    [Fact]
    public async Task Attendance_overview_shows_totals()
    {
        var a = (await Attendance.RegisterAsync(TestServices.ValidRegistration(1, "a@example.com", 3))).Record!;
        await Attendance.RegisterAsync(TestServices.ValidRegistration(2, "b@example.com", 1));
        await Attendance.CheckInAsync(a.Id);

        var cut = Render<Attendance>();

        Assert.Contains("Tech Leaders Summit", cut.Markup);
        Assert.Contains("Summer Rooftop Gala", cut.Markup);
        Assert.Contains("75%", cut.Markup); // 3 of 4 tickets checked in
    }
}
