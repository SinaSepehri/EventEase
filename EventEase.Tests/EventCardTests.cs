using Bunit;
using EventEase.Components;
using EventEase.Models;
using Microsoft.AspNetCore.Components;

namespace EventEase.Tests;

/// <summary>Data binding and validation edge cases for the Event Card component.</summary>
public class EventCardTests : BunitContext
{
    private static Event SampleEvent() => new()
    {
        Id = 7,
        Name = "Sample Event",
        Date = DateTime.Today.AddDays(5).AddHours(18),
        Location = "Austin",
        Capacity = 50,
        RegisteredCount = 10
    };

    [Fact]
    public void Displays_name_date_and_location()
    {
        var evt = SampleEvent();
        var cut = Render<EventCard>(p => p.Add(c => c.Event, evt));

        Assert.Contains("Sample Event", cut.Find(".card-title").TextContent);
        Assert.Contains("Austin", cut.Markup);
        Assert.Contains(evt.Date.ToString("MMM d, yyyy"), cut.Markup);
        Assert.Contains("40 of 50 seats left", cut.Markup);
    }

    [Fact]
    public void Null_event_renders_placeholder_instead_of_crashing()
    {
        var cut = Render<EventCard>(p => p.Add(c => c.Event, (Event?)null));
        Assert.Contains("Event details are unavailable", cut.Markup);
    }

    [Fact]
    public void Empty_name_shows_error_disables_save_and_does_not_raise_change()
    {
        var raised = false;
        var cut = Render<EventCard>(p => p
            .Add(c => c.Event, SampleEvent())
            .Add(c => c.EventChanged, (Event _) => raised = true));

        cut.Find("button.btn-outline-secondary").Click(); // Edit
        cut.Find("#name-7").Input("");

        Assert.Contains("Event name is required.", cut.Find(".validation-message").TextContent);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));

        cut.Find("form").Submit();
        Assert.False(raised);
    }

    [Fact]
    public void Whitespace_only_location_is_rejected()
    {
        var raised = false;
        var cut = Render<EventCard>(p => p
            .Add(c => c.Event, SampleEvent())
            .Add(c => c.EventChanged, (Event _) => raised = true));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#location-7").Input("    ");
        cut.Find("form").Submit();

        Assert.Contains("Location is required.", cut.Markup);
        Assert.False(raised);
    }

    [Fact]
    public void Unparseable_date_shows_parsing_error()
    {
        var cut = Render<EventCard>(p => p.Add(c => c.Event, SampleEvent()));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#date-7").Change("not-a-date");

        Assert.Contains("Please enter a valid date and time.", cut.Markup);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void Past_date_is_rejected()
    {
        var cut = Render<EventCard>(p => p.Add(c => c.Event, SampleEvent()));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#date-7").Change(DateTime.Today.AddDays(-3).ToString("yyyy-MM-ddTHH:mm"));

        Assert.Contains("The event date cannot be in the past.", cut.Markup);
    }

    [Fact]
    public void Live_preview_updates_while_typing()
    {
        var cut = Render<EventCard>(p => p.Add(c => c.Event, SampleEvent()));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#name-7").Input("Live Name");

        Assert.Contains("Live Name", cut.Find("p.small strong").TextContent);
    }

    [Fact]
    public void Valid_save_raises_trimmed_copy_and_leaves_original_untouched()
    {
        var original = SampleEvent();
        Event? received = null;
        var cut = Render<EventCard>(p => p
            .Add(c => c.Event, original)
            .Add(c => c.EventChanged, (Event e) => received = e));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#name-7").Input("   New Name   ");
        cut.Find("form").Submit();

        Assert.NotNull(received);
        Assert.Equal("New Name", received!.Name);
        Assert.NotSame(original, received);
        Assert.Equal("Sample Event", original.Name); // binding only flows back via EventChanged
    }

    [Fact]
    public void Cancel_discards_changes()
    {
        var raised = false;
        var cut = Render<EventCard>(p => p
            .Add(c => c.Event, SampleEvent())
            .Add(c => c.EventChanged, (Event _) => raised = true));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#name-7").Input("Changed");
        cut.FindAll("button").Single(b => b.TextContent == "Cancel").Click();

        Assert.False(raised);
        Assert.Contains("Sample Event", cut.Find(".card-title").TextContent);
    }

    [Fact]
    public void Two_way_binding_round_trip_updates_parent()
    {
        // Host component that uses @bind-Event and shows the bound name outside the card.
        var host = Render<BindingHost>();

        host.Find("button.btn-outline-secondary").Click();
        host.Find("input[id^=name-]").Input("Bound Name");
        host.Find("form").Submit();

        Assert.Equal("Bound Name", host.Find("#bound-name").TextContent);
    }

    [Fact]
    public void Card_skips_rerender_when_event_is_unchanged()
    {
        var evt = SampleEvent();
        var cut = Render<EventCard>(p => p.Add(c => c.Event, evt));
        var rendersBefore = cut.RenderCount;

        // Parent re-renders and passes the same, unchanged event again.
        cut.Render(p => p.Add(c => c.Event, evt));
        Assert.Equal(rendersBefore, cut.RenderCount);

        // A real change still renders.
        evt.RegisteredCount = 20;
        cut.Render(p => p.Add(c => c.Event, evt));
        Assert.Equal(rendersBefore + 1, cut.RenderCount);
        Assert.Contains("30 of 50 seats left", cut.Markup);
    }

    /// <summary>Minimal parent that uses @bind-Event, like the real pages do.</summary>
    private sealed class BindingHost : ComponentBase
    {
        private Event evt = SampleEvent();

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "id", "bound-name");
            builder.AddContent(2, evt.Name);
            builder.CloseElement();

            builder.OpenComponent<EventCard>(3);
            builder.AddComponentParameter(4, nameof(EventCard.Event), evt);
            builder.AddComponentParameter(5, nameof(EventCard.EventChanged),
                EventCallback.Factory.Create<Event>(this, e => evt = e));
            builder.CloseComponent();
        }
    }
}
