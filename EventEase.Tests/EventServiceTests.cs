using System.Diagnostics;
using EventEase.Models;
using EventEase.Services;

namespace EventEase.Tests;

/// <summary>The service must reject invalid data even when no form is involved.</summary>
public class EventServiceTests
{
    private static Registration ValidRegistration(int eventId, int tickets = 1) =>
        TestServices.ValidRegistration(eventId, tickets: tickets);

    [Fact]
    public void Negative_tickets_are_rejected_and_seats_unchanged()
    {
        // Bug before the fix: RegisteredCount += -5 gave seats *back*.
        var service = new EventService();
        var before = service.GetById(1)!.SeatsLeft;

        var result = service.Register(ValidRegistration(1, tickets: -5));

        Assert.Equal(RegistrationStatus.InvalidRegistration, result.Status);
        Assert.Equal(before, service.GetById(1)!.SeatsLeft);
    }

    [Fact]
    public void Registering_more_tickets_than_seats_left_fails()
    {
        var service = new EventService();
        var evt = service.GetById(1)!;
        evt.RegisteredCount = evt.Capacity - 2;

        var result = service.Register(ValidRegistration(1, tickets: 3));

        Assert.Equal(RegistrationStatus.NotEnoughSeats, result.Status);
        Assert.Equal(2, evt.SeatsLeft);
    }

    [Fact]
    public void Sold_out_event_rejects_registration()
    {
        var service = new EventService();
        var result = service.Register(ValidRegistration(3)); // seed event 3 is sold out
        Assert.Equal(RegistrationStatus.NotEnoughSeats, result.Status);
    }

    [Fact]
    public void Unknown_event_is_reported()
    {
        var service = new EventService();
        Assert.Equal(RegistrationStatus.EventNotFound, service.Register(ValidRegistration(999)).Status);
    }

    [Fact]
    public void Past_event_rejects_registration()
    {
        var service = new EventService();
        service.GetById(1)!.Date = DateTime.Now.AddDays(-1);
        Assert.Equal(RegistrationStatus.EventInPast, service.Register(ValidRegistration(1)).Status);
    }

    [Fact]
    public void Successful_registration_reduces_seats()
    {
        var service = new EventService();
        var before = service.GetById(2)!.SeatsLeft;

        var result = service.Register(ValidRegistration(2, tickets: 3));

        Assert.True(result.Succeeded);
        Assert.Equal(before - 3, service.GetById(2)!.SeatsLeft);
    }

    [Fact]
    public void Registration_input_is_trimmed()
    {
        var service = new EventService();
        var reg = ValidRegistration(2);
        reg.FullName = "  Test User  ";
        reg.Email = " test@example.com ";

        Assert.True(service.Register(reg).Succeeded);
        Assert.Equal("Test User", reg.FullName);
        Assert.Equal("test@example.com", reg.Email);
    }

    [Fact]
    public void Invalid_update_is_rejected_and_original_kept()
    {
        var service = new EventService();
        var invalid = service.GetById(1)!.Clone();
        invalid.Name = "   ";

        Assert.False(service.Update(invalid));
        Assert.Equal("Tech Leaders Summit", service.GetById(1)!.Name);
    }

    [Fact]
    public void Update_for_unknown_id_is_rejected()
    {
        var service = new EventService();
        var ghost = service.GetById(1)!.Clone();
        ghost.Id = 12345;
        Assert.False(service.Update(ghost));
        Assert.Null(service.GetById(12345));
    }

    [Fact]
    public void Valid_update_is_trimmed_and_stored()
    {
        var service = new EventService();
        var copy = service.GetById(1)!.Clone();
        copy.Name = "  Renamed Summit  ";

        Assert.True(service.Update(copy));
        Assert.Equal("Renamed Summit", service.GetById(1)!.Name);
    }

    [Fact]
    public void EnsureCount_is_capped()
    {
        var service = new EventService();
        service.EnsureCount(10_000_000);
        Assert.Equal(EventService.MaxMockEvents, service.Count);
    }

    [Fact]
    public void Generated_events_are_all_valid()
    {
        var service = new EventService();
        service.EnsureCount(2_000);
        Assert.All(service.GetAll(), e => Assert.True(ModelValidator.TryValidate(e, out _), $"Event {e.Id} is invalid"));
    }

    [Fact]
    public void Lookups_and_sorted_list_are_fast_with_large_data()
    {
        var service = new EventService();
        service.EnsureCount(EventService.MaxMockEvents);
        service.GetAll(); // build the cache once

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            Assert.NotNull(service.GetById(i % EventService.MaxMockEvents + 1));
            Assert.Equal(EventService.MaxMockEvents, service.GetAll().Count); // cached, no re-sort
        }
        sw.Stop();

        // Old version: List scan + OrderBy on every call (~20,000 x 1,000 operations).
        Assert.True(sw.ElapsedMilliseconds < 200, $"Took {sw.ElapsedMilliseconds} ms");
    }
}
