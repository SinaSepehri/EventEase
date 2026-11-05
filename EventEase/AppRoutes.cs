namespace EventEase;

/// <summary>
/// Central place for route URLs so pages and links never hard-code paths.
/// Paths are relative (no leading slash) so they resolve against &lt;base href&gt;
/// and keep working when hosted under a sub-path such as GitHub Pages.
/// Route templates in @page directives must stay in sync with these:
///   /                      -> Home
///   /events                -> EventList
///   /events/{Id:int}       -> EventDetails
///   /events/{Id:int}/register -> Register
/// </summary>
public static class AppRoutes
{
    public const string Home = "";
    public const string Events = "events";

    public const string MyRegistrations = "my-registrations";
    public const string Attendance = "attendance";

    public static string EventDetails(int id) => $"events/{id}";
    public static string Register(int id) => $"events/{id}/register";
    public static string EventAttendance(int id) => $"attendance/{id}";
}
