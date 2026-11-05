namespace EventEase;

/// <summary>App-wide settings decided at startup.</summary>
public sealed class AppOptions
{
    /// <summary>Allows /events?count=N to generate mock events for performance testing. Development only.</summary>
    public bool EnableMockDataGenerator { get; init; }
}
