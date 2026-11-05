using System.Diagnostics;
using System.Text.Json;
using EventEase.Models;
using EventEase.Services;
using Xunit.Abstractions;

namespace EventEase.Tests;

/// <summary>Registration storage, check-in, seat accounting and persistence.</summary>
public class AttendanceServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Register_stores_record_and_reduces_seats()
    {
        var (events, _, attendance) = TestServices.Create();
        var before = events.GetById(2)!.SeatsLeft;

        var outcome = await attendance.RegisterAsync(TestServices.ValidRegistration(2, tickets: 3));

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Record);
        Assert.Equal("Summer Rooftop Gala", outcome.Record!.EventName);
        Assert.Equal(before - 3, events.GetById(2)!.SeatsLeft);
        Assert.Single(attendance.GetForEvent(2));
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("TEST@example.com")]
    [InlineData("  test@example.com  ")]
    public async Task Duplicate_registration_is_rejected(string secondEmail)
    {
        var (events, _, attendance) = TestServices.Create();
        await attendance.RegisterAsync(TestServices.ValidRegistration(2));
        var seats = events.GetById(2)!.SeatsLeft;

        var outcome = await attendance.RegisterAsync(TestServices.ValidRegistration(2, secondEmail));

        Assert.Equal(RegistrationStatus.AlreadyRegistered, outcome.Status);
        Assert.Equal(seats, events.GetById(2)!.SeatsLeft);
        Assert.Single(attendance.GetForEvent(2));
    }

    [Fact]
    public async Task Same_email_can_register_for_different_events()
    {
        var (_, _, attendance) = TestServices.Create();
        Assert.True((await attendance.RegisterAsync(TestServices.ValidRegistration(1))).Succeeded);
        Assert.True((await attendance.RegisterAsync(TestServices.ValidRegistration(2))).Succeeded);
        Assert.Equal(2, attendance.GetForEmail("test@example.com").Count);
    }

    [Fact]
    public async Task Invalid_registration_is_not_stored()
    {
        var (_, _, attendance) = TestServices.Create();
        var reg = TestServices.ValidRegistration(2);
        reg.AcceptTerms = false;

        var outcome = await attendance.RegisterAsync(reg);

        Assert.Equal(RegistrationStatus.InvalidRegistration, outcome.Status);
        Assert.Equal(0, attendance.Count);
    }

    [Fact]
    public async Task Cancel_releases_seats_and_allows_registering_again()
    {
        var (events, _, attendance) = TestServices.Create();
        var before = events.GetById(2)!.SeatsLeft;
        var record = (await attendance.RegisterAsync(TestServices.ValidRegistration(2, tickets: 4))).Record!;

        Assert.True(await attendance.CancelAsync(record.Id));
        Assert.False(await attendance.CancelAsync(record.Id)); // already cancelled
        Assert.Equal(before, events.GetById(2)!.SeatsLeft);

        Assert.True((await attendance.RegisterAsync(TestServices.ValidRegistration(2))).Succeeded);
    }

    [Fact]
    public async Task Check_in_and_undo()
    {
        var (_, _, attendance) = TestServices.Create();
        var record = (await attendance.RegisterAsync(TestServices.ValidRegistration(2, tickets: 2))).Record!;

        Assert.True(await attendance.CheckInAsync(record.Id));
        Assert.False(await attendance.CheckInAsync(record.Id)); // no double check-in
        Assert.True(attendance.GetById(record.Id)!.IsCheckedIn);

        Assert.True(await attendance.UndoCheckInAsync(record.Id));
        Assert.False(attendance.GetById(record.Id)!.IsCheckedIn);
        Assert.False(await attendance.UndoCheckInAsync(record.Id));
    }

    [Fact]
    public async Task Cancelled_registration_cannot_check_in()
    {
        var (_, _, attendance) = TestServices.Create();
        var record = (await attendance.RegisterAsync(TestServices.ValidRegistration(2))).Record!;
        await attendance.CancelAsync(record.Id);

        Assert.False(await attendance.CheckInAsync(record.Id));
    }

    [Fact]
    public async Task Unknown_ids_are_handled()
    {
        var (_, _, attendance) = TestServices.Create();
        Assert.False(await attendance.CheckInAsync(Guid.NewGuid()));
        Assert.False(await attendance.CancelAsync(Guid.NewGuid()));
        Assert.Empty(attendance.GetForEvent(999));
        Assert.Empty(attendance.GetForEmail(null));
    }

    [Fact]
    public async Task Summary_counts_tickets_check_ins_and_cancellations()
    {
        var (_, _, attendance) = TestServices.Create();
        var a = (await attendance.RegisterAsync(TestServices.ValidRegistration(4, "a@example.com", 2))).Record!;
        var b = (await attendance.RegisterAsync(TestServices.ValidRegistration(4, "b@example.com", 3))).Record!;
        var c = (await attendance.RegisterAsync(TestServices.ValidRegistration(4, "c@example.com", 5))).Record!;
        await attendance.CheckInAsync(a.Id);
        await attendance.CheckInAsync(b.Id);
        await attendance.CancelAsync(c.Id);

        var s = attendance.GetSummary(4);

        Assert.Equal(2, s.Registrations);
        Assert.Equal(5, s.Tickets);
        Assert.Equal(2, s.CheckedInRegistrations);
        Assert.Equal(5, s.CheckedInTickets);
        Assert.Equal(1, s.Cancelled);
        Assert.Equal(100, s.AttendanceRate);
        Assert.Single(attendance.GetSummaries());
    }

    [Fact]
    public void Attendance_rate_is_zero_without_tickets() =>
        Assert.Equal(0, new AttendanceSummary(1, "x", 0, 0, 0, 0, 0).AttendanceRate);

    [Fact]
    public async Task Changed_is_raised_on_every_change()
    {
        var (_, _, attendance) = TestServices.Create();
        var count = 0;
        attendance.Changed += () => count++;

        var record = (await attendance.RegisterAsync(TestServices.ValidRegistration(2))).Record!;
        await attendance.CheckInAsync(record.Id);
        await attendance.UndoCheckInAsync(record.Id);
        await attendance.CancelAsync(record.Id);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Registrations_check_ins_and_seats_survive_a_reload()
    {
        var storage = new FakeBrowserStorage();
        var (events1, _, first) = TestServices.Create(storage);
        var original = events1.GetById(2)!.SeatsLeft;
        var kept = (await first.RegisterAsync(TestServices.ValidRegistration(2, "a@example.com", 3))).Record!;
        var gone = (await first.RegisterAsync(TestServices.ValidRegistration(2, "b@example.com", 2))).Record!;
        await first.CheckInAsync(kept.Id);
        await first.CancelAsync(gone.Id);
        await first.FlushAsync();

        // Reload: fresh EventService (seed data) + fresh AttendanceService, same storage.
        var (events2, _, second) = TestServices.Create(storage);
        await second.InitializeAsync();

        Assert.Equal(2, second.Count);
        Assert.True(second.GetById(kept.Id)!.IsCheckedIn);
        Assert.True(second.GetById(gone.Id)!.IsCancelled);
        Assert.Equal(original - 3, events2.GetById(2)!.SeatsLeft); // only active tickets re-applied
    }

    [Fact]
    public async Task Rapid_changes_are_batched_into_one_write()
    {
        var storage = new FakeBrowserStorage();
        var (_, _, attendance) = TestServices.Create(storage);
        var record = (await attendance.RegisterAsync(TestServices.ValidRegistration(2))).Record!;
        await attendance.FlushAsync();
        var writesBefore = storage.WriteCount;

        // 20 quick clicks at the check-in desk.
        for (var i = 0; i < 10; i++)
        {
            await attendance.CheckInAsync(record.Id);
            await attendance.UndoCheckInAsync(record.Id);
        }
        Assert.Equal(writesBefore, storage.WriteCount); // nothing written yet: the UI didn't wait on storage

        await attendance.FlushAsync();
        Assert.Equal(writesBefore + 1, storage.WriteCount);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Corrupt_or_empty_storage_starts_clean(string json)
    {
        var storage = new FakeBrowserStorage();
        storage.Items[(StorageArea.Local, AttendanceService.StorageKey)] = json;
        var (_, _, attendance) = TestServices.Create(storage);

        await attendance.InitializeAsync();

        Assert.Equal(0, attendance.Count);
    }

    [Fact]
    public async Task Damaged_and_orphan_records_are_handled_on_load()
    {
        var storage = new FakeBrowserStorage();
        var records = new[]
        {
            new RegistrationRecord { EventId = 2, FullName = "A", Email = "a@example.com", Tickets = 0 },        // damaged
            new RegistrationRecord { EventId = 9999, EventName = "Old event", FullName = "B", Email = "b@example.com", Tickets = 2 } // orphan
        };
        storage.Items[(StorageArea.Local, AttendanceService.StorageKey)] = JsonSerializer.Serialize(records);
        var (events, _, attendance) = TestServices.Create(storage);
        var seats = events.GetById(2)!.SeatsLeft;

        await attendance.InitializeAsync();

        Assert.Equal(1, attendance.Count);                         // damaged skipped, orphan kept
        Assert.Equal("Old event", attendance.GetSummary(9999).EventName);
        Assert.Equal(seats, events.GetById(2)!.SeatsLeft);
    }

    [Fact]
    public async Task Storage_failure_does_not_block_registration()
    {
        var storage = new FakeBrowserStorage { Throw = true };
        var (_, _, attendance) = TestServices.Create(storage);

        await attendance.InitializeAsync();
        var outcome = await attendance.RegisterAsync(TestServices.ValidRegistration(2));

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, attendance.Count);
    }

    [Fact]
    public async Task Stays_responsive_with_thousands_of_registrations()
    {
        var storage = new FakeBrowserStorage();
        var events = new EventService();
        events.EnsureCount(2_000);
        var (_, _, attendance) = TestServices.Create(storage, events: events);

        var sw = Stopwatch.StartNew();
        var ids = new List<Guid>();
        for (var i = 0; i < 2_000; i++)
        {
            var eventId = i % 500 + 5; // generated events (all upcoming)
            var outcome = await attendance.RegisterAsync(TestServices.ValidRegistration(eventId, $"user{i}@example.com"));
            if (outcome.Succeeded) ids.Add(outcome.Record!.Id);
        }
        var registerMs = sw.ElapsedMilliseconds;

        sw.Restart();
        foreach (var id in ids.Take(500)) await attendance.CheckInAsync(id);
        var checkInMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var summaries = attendance.GetSummaries();
        var summaryMs = sw.ElapsedMilliseconds;

        sw.Restart();
        await attendance.FlushAsync();
        var flushMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var (_, _, reloaded) = TestServices.Create(storage, events: new EventService());
        await reloaded.InitializeAsync();
        var reloadMs = sw.ElapsedMilliseconds;

        output.WriteLine($"{ids.Count} registrations: register {registerMs} ms total, 500 check-ins {checkInMs} ms, " +
                         $"{summaries.Count} summaries {summaryMs} ms, final save {flushMs} ms, reload {reloadMs} ms");
        Assert.Equal(ids.Count, reloaded.Count);

        Assert.True(ids.Count > 1_900);
        Assert.True(summaryMs < 100, $"Summaries took {summaryMs} ms");
        Assert.True(reloadMs < 1_000, $"Reload took {reloadMs} ms");
        Assert.True(checkInMs / 500.0 < 1, $"Check-in averaged {checkInMs / 500.0:0.0} ms");
    }
}
