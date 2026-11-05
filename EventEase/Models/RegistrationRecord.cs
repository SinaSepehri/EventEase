namespace EventEase.Models;

/// <summary>
/// A stored registration, which the Attendance Tracker checks in.
/// </summary>
public class RegistrationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int EventId { get; set; }
    public string EventName { get; set; } = string.Empty;   // snapshot, shown even if the event is gone
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int Tickets { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public bool IsCancelled => CancelledAt is not null;
    public bool IsCheckedIn => CheckedInAt is not null;
}

/// <summary>Attendance numbers for one event.</summary>
public sealed record AttendanceSummary(
    int EventId,
    string EventName,
    int Registrations,
    int Tickets,
    int CheckedInRegistrations,
    int CheckedInTickets,
    int Cancelled)
{
    /// <summary>Share of registered tickets that have checked in (0–100).</summary>
    public double AttendanceRate => Tickets == 0 ? 0 : Math.Round(100.0 * CheckedInTickets / Tickets, 1);
}
