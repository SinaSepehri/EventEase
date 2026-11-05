using System.Text.Json;
using EventEase.Models;

namespace EventEase.Services;

/// <summary>Result of a registration attempt, including the stored record on success.</summary>
public sealed record RegistrationOutcome(RegistrationStatus Status, string Message, RegistrationRecord? Record = null)
{
    public bool Succeeded => Status == RegistrationStatus.Success;
}

/// <summary>
/// Stores registrations and tracks attendance (check-in) per event.
/// Data is saved to localStorage so registrations and check-ins survive a reload.
/// Seat counts in <see cref="EventService"/> are kept in sync (register, cancel, restore).
/// </summary>
public sealed class AttendanceService(EventService events, IBrowserStorage storage, TimeProvider clock, ILogger<AttendanceService> logger)
{
    public const string StorageKey = "eventease.registrations.v1";

    private readonly Dictionary<Guid, RegistrationRecord> byId = new();
    private readonly Dictionary<int, List<RegistrationRecord>> byEvent = new();   // O(1) per-event lookups

    /// <summary>Raised after any change, so open pages can refresh.</summary>
    public event Action? Changed;

    public int Count => byId.Count;

    /// <summary>Loads saved registrations and re-applies their seats (call once at startup).</summary>
    public async Task InitializeAsync()
    {
        List<RegistrationRecord>? saved = null;
        try
        {
            var json = await storage.GetAsync(StorageArea.Local, StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                saved = JsonSerializer.Deserialize(json, EventEaseJsonContext.Default.ListRegistrationRecord);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Saved registrations were corrupt and have been ignored.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read saved registrations.");
        }

        foreach (var record in saved ?? [])
        {
            if (record.Tickets <= 0 || byId.ContainsKey(record.Id)) continue; // skip damaged/duplicate entries

            Add(record);
            if (!record.IsCancelled && !events.RestoreSeats(record.EventId, record.Tickets))
            {
                // E.g. a generated mock event that doesn't exist after reload. Keep the record so it isn't lost.
                logger.LogInformation("Registration {Id} refers to unknown event {EventId}.", record.Id, record.EventId);
            }
        }
    }

    /// <summary>
    /// Validates and stores a registration. Rejects a second active registration
    /// with the same email for the same event.
    /// </summary>
    public async Task<RegistrationOutcome> RegisterAsync(Registration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Normalize();

        var existing = FindActive(registration.EventId, registration.Email);
        if (existing is not null)
        {
            return new(RegistrationStatus.AlreadyRegistered,
                $"{registration.Email} is already registered for this event ({existing.Tickets} ticket(s)).", existing);
        }

        // Validation + seat check live in EventService so all rules are in one place.
        var result = events.Register(registration);
        if (!result.Succeeded)
        {
            return new(result.Status, result.Message);
        }

        var record = new RegistrationRecord
        {
            EventId = registration.EventId,
            EventName = events.GetById(registration.EventId)?.Name ?? string.Empty,
            FullName = registration.FullName,
            Email = registration.Email,
            Phone = registration.Phone,
            Tickets = registration.Tickets,
            RegisteredAt = clock.GetUtcNow()
        };
        Add(record);

        await SaveAndNotifyAsync();
        return new(RegistrationStatus.Success, result.Message, record);
    }

    public async Task<bool> CancelAsync(Guid id)
    {
        if (!byId.TryGetValue(id, out var record) || record.IsCancelled) return false;

        record.CancelledAt = clock.GetUtcNow();
        record.CheckedInAt = null;
        events.ReleaseSeats(record.EventId, record.Tickets);

        await SaveAndNotifyAsync();
        return true;
    }

    public async Task<bool> CheckInAsync(Guid id)
    {
        if (!byId.TryGetValue(id, out var record) || record.IsCancelled || record.IsCheckedIn) return false;

        record.CheckedInAt = clock.GetUtcNow();
        await SaveAndNotifyAsync();
        return true;
    }

    public async Task<bool> UndoCheckInAsync(Guid id)
    {
        if (!byId.TryGetValue(id, out var record) || !record.IsCheckedIn) return false;

        record.CheckedInAt = null;
        await SaveAndNotifyAsync();
        return true;
    }

    public RegistrationRecord? GetById(Guid id) => byId.GetValueOrDefault(id);

    public IReadOnlyList<RegistrationRecord> GetForEvent(int eventId) =>
        byEvent.TryGetValue(eventId, out var list) ? list : [];

    public IReadOnlyList<RegistrationRecord> GetForEmail(string? email) =>
        string.IsNullOrWhiteSpace(email)
            ? []
            : byId.Values
                .Where(r => string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.RegisteredAt)
                .ToList();

    public RegistrationRecord? FindActive(int eventId, string? email) =>
        GetForEvent(eventId).FirstOrDefault(r =>
            !r.IsCancelled && string.Equals(r.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase));

    public AttendanceSummary GetSummary(int eventId)
    {
        int registrations = 0, tickets = 0, checkedIn = 0, checkedInTickets = 0, cancelled = 0;
        foreach (var r in GetForEvent(eventId))
        {
            if (r.IsCancelled) { cancelled++; continue; }
            registrations++;
            tickets += r.Tickets;
            if (r.IsCheckedIn)
            {
                checkedIn++;
                checkedInTickets += r.Tickets;
            }
        }

        var name = events.GetById(eventId)?.Name
                   ?? GetForEvent(eventId).FirstOrDefault()?.EventName
                   ?? $"Event #{eventId}";
        return new AttendanceSummary(eventId, name, registrations, tickets, checkedIn, checkedInTickets, cancelled);
    }

    /// <summary>Summaries for every event that has at least one registration.</summary>
    public IReadOnlyList<AttendanceSummary> GetSummaries() =>
        byEvent.Keys.Select(GetSummary).OrderByDescending(s => s.Tickets).ToList();

    private void Add(RegistrationRecord record)
    {
        byId[record.Id] = record;
        if (!byEvent.TryGetValue(record.EventId, out var list))
        {
            byEvent[record.EventId] = list = [];
        }
        list.Add(record);
    }

    // --- Persistence -----------------------------------------------------------------------
    // localStorage holds one JSON value, so every save writes *all* registrations. Saving on
    // each click made rapid check-ins slow with thousands of records. Instead, the UI updates
    // immediately and saves are batched: clicks within SaveDelay share one write.

    /// <summary>How long to wait so quick changes in a row share one write. Tests set this to zero.</summary>
    internal TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    private Task? pendingSave;
    private bool dirty;

    /// <summary>Waits until all pending changes are written to storage.</summary>
    public Task FlushAsync() => pendingSave ?? Task.CompletedTask;

    private Task SaveAndNotifyAsync()
    {
        dirty = true;
        pendingSave ??= SaveSoonAsync();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private async Task SaveSoonAsync()
    {
        try
        {
            if (SaveDelay > TimeSpan.Zero)
            {
                await Task.Delay(SaveDelay);
            }

            while (dirty)
            {
                dirty = false;
                var json = JsonSerializer.Serialize(byId.Values.ToList(), EventEaseJsonContext.Default.ListRegistrationRecord);
                await storage.SetAsync(StorageArea.Local, StorageKey, json);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep working in memory; the change just won't survive a reload.
            logger.LogWarning(ex, "Could not save registrations to browser storage.");
        }
        finally
        {
            pendingSave = null;
        }
    }
}
