using System.Reflection;
using Bunit;
using EventEase.Pages;
using EventEase.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EventEase.Tests;

/// <summary>Route templates, URL helpers, and graceful handling of invalid paths.</summary>
public class RoutingTests : BunitContext
{
    public RoutingTests()
    {
        this.AddEventEase();
    }

    [Fact]
    public void Pages_declare_expected_route_templates()
    {
        var routes = typeof(App).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(r => (Page: t.Name, r.Template)))
            .ToDictionary(r => r.Template, r => r.Page);

        Assert.Equal(nameof(Home), routes["/"]);
        Assert.Equal(nameof(EventList), routes["/events"]);
        Assert.Equal(nameof(EventDetails), routes["/events/{Id:int}"]);
        Assert.Equal(nameof(Register), routes["/events/{Id:int}/register"]);
        Assert.Equal(nameof(NotFound), routes["/not-found"]);
        Assert.Equal(nameof(MyRegistrations), routes["/my-registrations"]);
        Assert.Equal(nameof(Attendance), routes["/attendance"]);
        Assert.Equal(nameof(EventAttendance), routes["/attendance/{EventId:int}"]);
        Assert.Equal(8, routes.Count); // no stray or duplicate routes
    }

    [Fact]
    public void Route_helpers_are_relative_and_match_templates()
    {
        Assert.Equal("", AppRoutes.Home);
        Assert.Equal("events", AppRoutes.Events);
        Assert.Equal("events/42", AppRoutes.EventDetails(42));
        Assert.Equal("events/42/register", AppRoutes.Register(42));
        Assert.DoesNotContain(new[] { AppRoutes.Events, AppRoutes.EventDetails(1), AppRoutes.Register(1) }, p => p.StartsWith('/'));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void Details_with_unknown_id_triggers_not_found(int id)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var notFoundRaised = false;
        nav.OnNotFound += (_, _) => notFoundRaised = true;

        var cut = Render<EventDetails>(p => p.Add(c => c.Id, id));

        Assert.True(notFoundRaised);
        Assert.Empty(cut.FindAll("h1")); // page renders nothing itself; the Router shows NotFoundPage
    }

    [Fact]
    public void Register_with_unknown_id_triggers_not_found()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var notFoundRaised = false;
        nav.OnNotFound += (_, _) => notFoundRaised = true;

        Render<Register>(p => p.Add(c => c.Id, 999));

        Assert.True(notFoundRaised);
    }

    [Fact]
    public void Details_with_valid_id_renders_event()
    {
        var cut = Render<EventDetails>(p => p.Add(c => c.Id, 1));
        Assert.Equal("Tech Leaders Summit", cut.Find("h1").TextContent);
    }

    [Fact]
    public void Register_for_sold_out_event_shows_message_instead_of_form()
    {
        var cut = Render<Register>(p => p.Add(c => c.Id, 3)); // seed event 3 is sold out
        Assert.Contains("sold out", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Register_for_past_event_is_closed()
    {
        Services.GetRequiredService<EventService>().GetById(2)!.Date = DateTime.Now.AddDays(-2);
        var cut = Render<Register>(p => p.Add(c => c.Id, 2));
        Assert.Contains("Registration is closed", cut.Markup);
        Assert.Empty(cut.FindAll("form"));
    }

    [Theory]
    [InlineData("events?count=abc")]
    [InlineData("events?count=-10")]
    [InlineData("events?count=99999999999999")]
    public void Event_list_ignores_invalid_query_values(string url)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        var cut = Render<EventList>();
        Assert.Contains("Showing 4 of 4", cut.Markup);
    }

    [Fact]
    public void Not_found_page_shows_requested_path()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("events/abc");
        var cut = Render<NotFound>();
        Assert.Contains("/events/abc", cut.Markup);
    }

    [Fact]
    public void Not_found_page_updates_when_reused_for_another_bad_url()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("events/abc");
        var cut = Render<NotFound>();

        nav.NavigateTo("totally/unknown");

        cut.WaitForAssertion(() => Assert.Contains("/totally/unknown", cut.Markup));
        Assert.DoesNotContain("/events/abc", cut.Markup);
    }

    [Fact]
    public void Search_is_debounced_and_filters()
    {
        var cut = Render<EventList>();

        cut.Find("#search").Input("gala");
        // Debounced: right after typing, nothing has been filtered yet.
        Assert.Contains("Showing 4 of 4", cut.Markup);

        cut.WaitForAssertion(() => Assert.Contains("Showing 1 of 4", cut.Markup), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Category_filter_applies_immediately()
    {
        var cut = Render<EventList>();
        cut.Find("select").Change("Social");
        Assert.Contains("Showing 2 of 4", cut.Markup);
    }
}
