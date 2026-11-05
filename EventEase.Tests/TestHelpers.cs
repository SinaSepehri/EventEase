using Bunit;
using EventEase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventEase.Tests;

/// <summary>In-memory stand-in for localStorage/sessionStorage. Can simulate a broken storage.</summary>
public sealed class FakeBrowserStorage : IBrowserStorage
{
    public Dictionary<(StorageArea, string), string> Items { get; } = new();
    public bool Throw { get; set; }

    public ValueTask<string?> GetAsync(StorageArea area, string key)
    {
        ThrowIfBroken();
        return ValueTask.FromResult(Items.GetValueOrDefault((area, key)));
    }

    public int WriteCount { get; private set; }

    public ValueTask SetAsync(StorageArea area, string key, string value)
    {
        ThrowIfBroken();
        WriteCount++;
        Items[(area, key)] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(StorageArea area, string key)
    {
        ThrowIfBroken();
        Items.Remove((area, key));
        return ValueTask.CompletedTask;
    }

    public bool Has(StorageArea area, string key) => Items.ContainsKey((area, key));

    private void ThrowIfBroken()
    {
        if (Throw) throw new InvalidOperationException("Storage is unavailable (simulated).");
    }
}

/// <summary>A clock the tests can move forward.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public ManualTimeProvider() : this(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)) { }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

public static class TestServices
{
    /// <summary>Creates the services the way Program.cs wires them, with fakes for browser APIs.</summary>
    public static (EventService Events, UserSessionState Session, AttendanceService Attendance) Create(
        FakeBrowserStorage? storage = null, TimeProvider? clock = null, EventService? events = null)
    {
        storage ??= new FakeBrowserStorage();
        clock ??= TimeProvider.System;
        events ??= new EventService();
        var session = new UserSessionState(storage, clock, NullLogger<UserSessionState>.Instance);
        var attendance = new AttendanceService(events, storage, clock, NullLogger<AttendanceService>.Instance);
        return (events, session, attendance);
    }

    /// <summary>Registers all EventEase services in a bUnit context.</summary>
    public static FakeBrowserStorage AddEventEase(this BunitContext ctx, FakeBrowserStorage? storage = null)
    {
        storage ??= new FakeBrowserStorage();
        ctx.Services.AddSingleton(new AppOptions { EnableMockDataGenerator = true });
        ctx.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        ctx.Services.AddSingleton<IBrowserStorage>(storage);
        ctx.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        ctx.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        ctx.Services.AddSingleton<EventService>();
        ctx.Services.AddSingleton<UserSessionState>();
        ctx.Services.AddSingleton<AttendanceService>();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose; // Virtualize uses JS interop
        return storage;
    }

    public static EventEase.Models.Registration ValidRegistration(int eventId, string email = "test@example.com", int tickets = 1) => new()
    {
        EventId = eventId,
        FullName = "Test User",
        Email = email,
        Tickets = tickets,
        AcceptTerms = true
    };
}
