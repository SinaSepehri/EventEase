using EventEase.Models;

namespace EventEase.Services;

public enum RegistrationStatus
{
    Success,
    EventNotFound,
    InvalidRegistration,
    EventInPast,
    NotEnoughSeats,
    AlreadyRegistered
}

public sealed record RegistrationResult(RegistrationStatus Status, string Message)
{
    public bool Succeeded => Status == RegistrationStatus.Success;
}

/// <summary>
/// In-memory event store backed by mock data.
/// Registered as a singleton so state survives page navigation.
/// </summary>
public class EventService
{
    /// <summary>Upper limit for generated mock data, so a URL like ?count=99999999 can't freeze the browser.</summary>
    public const int MaxMockEvents = 20_000;

    // Dictionary gives O(1) lookups by id (the list scan it replaces was O(n) on every details/register page).
    private readonly Dictionary<int, Event> _events = new();

    // Sorted snapshot, cached and rebuilt only when the data changes (not on every GetAll call).
    private IReadOnlyList<Event>? _sortedCache;

    public EventService()
    {
        Event[] seed =
        [
            new()
            {
                Id = 1, Name = "Tech Leaders Summit", Date = DateTime.Today.AddDays(14).AddHours(9),
                Location = "Seattle Convention Center, Seattle", Category = EventCategory.Corporate, Capacity = 300, RegisteredCount = 212,
                Description = "A full-day summit where engineering leaders share lessons on scaling teams and products."
            },
            new()
            {
                Id = 2, Name = "Summer Rooftop Gala", Date = DateTime.Today.AddDays(30).AddHours(19),
                Location = "Skyline Terrace, New York", Category = EventCategory.Social, Capacity = 120, RegisteredCount = 87,
                Description = "An evening of live music, food and networking under the city lights."
            },
            new()
            {
                Id = 3, Name = "Quarterly Sales Kickoff", Date = DateTime.Today.AddDays(7).AddHours(10),
                Location = "Grand Hyatt, Chicago", Category = EventCategory.Corporate, Capacity = 80, RegisteredCount = 80,
                Description = "Align on targets, celebrate wins, and plan the quarter ahead."
            },
            new()
            {
                Id = 4, Name = "Charity Fun Run", Date = DateTime.Today.AddDays(45).AddHours(8),
                Location = "Golden Gate Park, San Francisco", Category = EventCategory.Social, Capacity = 500, RegisteredCount = 143,
                Description = "A 5K run raising funds for local schools. All fitness levels welcome."
            },
        ];

        foreach (var evt in seed)
        {
            _events[evt.Id] = evt;
        }
    }

    public int Count => _events.Count;

    /// <summary>
    /// Adds generated mock events until the store holds <paramref name="total"/> events
    /// (capped at <see cref="MaxMockEvents"/>). Used to test the list with large data sets,
    /// e.g. /events?count=5000.
    /// </summary>
    public void EnsureCount(int total)
    {
        total = Math.Clamp(total, 0, MaxMockEvents);
        if (total <= _events.Count)
        {
            return;
        }

        string[] names = ["Product Launch", "Networking Mixer", "Team Offsite", "Wine Tasting", "Hackathon", "Gala Dinner", "Workshop", "Book Club", "Charity Auction", "Board Meeting"];
        string[] cities = ["Seattle", "New York", "Chicago", "Austin", "Boston", "Denver", "Miami", "Portland", "Atlanta", "San Diego"];
        var random = new Random(42); // fixed seed => repeatable data
        for (var id = _events.Count + 1; id <= total; id++)
        {
            var capacity = random.Next(20, 500);
            _events[id] = new Event
            {
                Id = id,
                Name = $"{names[id % names.Length]} #{id}",
                Date = DateTime.Today.AddDays(random.Next(1, 365)).AddHours(random.Next(8, 21)),
                Location = $"Venue {random.Next(1, 99)}, {cities[random.Next(cities.Length)]}",
                Category = id % 2 == 0 ? EventCategory.Corporate : EventCategory.Social,
                Capacity = capacity,
                RegisteredCount = random.Next(0, capacity + 1),
                Description = "Generated mock event used for performance testing."
            };
        }

        _sortedCache = null;
    }

    public IReadOnlyList<Event> GetAll() =>
        _sortedCache ??= _events.Values.OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();

    public Event? GetById(int id) => _events.GetValueOrDefault(id);

    /// <summary>
    /// Replaces an existing event. Returns false (and stores nothing) if the event
    /// does not exist or fails validation.
    /// </summary>
    public bool Update(Event updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        if (!_events.ContainsKey(updated.Id))
        {
            return false;
        }

        updated.Normalize();
        if (!ModelValidator.TryValidate(updated, out _))
        {
            return false;
        }

        _events[updated.Id] = updated;
        _sortedCache = null;
        return true;
    }

    /// <summary>
    /// Registers tickets for an event after validating the registration and the event state.
    /// </summary>
    public RegistrationResult Register(Registration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Normalize();
        if (!ModelValidator.TryValidate(registration, out var errors))
        {
            // Without this check a negative ticket count would *add* seats back.
            return new(RegistrationStatus.InvalidRegistration, errors[0].ErrorMessage ?? "The registration is not valid.");
        }

        var evt = GetById(registration.EventId);
        if (evt is null)
        {
            return new(RegistrationStatus.EventNotFound, "This event does not exist.");
        }

        if (evt.IsPast)
        {
            return new(RegistrationStatus.EventInPast, "Registration is closed because this event has already taken place.");
        }

        if (registration.Tickets > evt.SeatsLeft)
        {
            return new(RegistrationStatus.NotEnoughSeats,
                evt.SeatsLeft == 0 ? "This event is sold out." : $"Only {evt.SeatsLeft} seat(s) are left for this event.");
        }

        evt.RegisteredCount += registration.Tickets;
        return new(RegistrationStatus.Success, "Registration confirmed.");
    }

    /// <summary>Gives seats back when a registration is cancelled.</summary>
    public void ReleaseSeats(int eventId, int tickets)
    {
        if (tickets > 0 && GetById(eventId) is { } evt)
        {
            evt.RegisteredCount = Math.Max(0, evt.RegisteredCount - tickets);
        }
    }

    /// <summary>
    /// Re-applies seats for registrations restored from storage after a reload.
    /// Returns false for unknown events. Capacity isn't enforced here: the seats were already granted.
    /// </summary>
    public bool RestoreSeats(int eventId, int tickets)
    {
        if (tickets <= 0 || GetById(eventId) is not { } evt) return false;
        evt.RegisteredCount += tickets;
        return true;
    }
}
