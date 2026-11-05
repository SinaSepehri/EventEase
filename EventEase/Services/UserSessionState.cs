using System.Text.Json;
using EventEase.Models;

namespace EventEase.Services;

/// <summary>
/// Tracks the current user's session and keeps it across page reloads.
///
/// - "Remember me" on  -> localStorage   (kept for <see cref="SessionLifetime"/>, sliding)
/// - "Remember me" off -> sessionStorage (cleared when the tab is closed)
///
/// Components subscribe to <see cref="Changed"/> to update when the user signs in or out.
/// Storage failures (private mode, quota, disabled storage) are logged and the session keeps
/// working in memory, so the app never breaks because storage is unavailable.
/// </summary>
public sealed class UserSessionState(IBrowserStorage storage, TimeProvider clock, ILogger<UserSessionState> logger)
{
    public const string StorageKey = "eventease.session.v1";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    private SessionData? current;

    public event Action? Changed;

    public bool IsSignedIn => current is not null;
    public string? FullName => current?.FullName;
    public string? Email => current?.Email;
    public bool IsPersistent => current?.Persistent ?? false;
    public DateTimeOffset? StartedAt => current?.StartedAt;
    public DateTimeOffset? ExpiresAt => current?.ExpiresAt;

    /// <summary>Restores a saved session (call once at startup).</summary>
    public async Task InitializeAsync()
    {
        current = await LoadAsync(StorageArea.Session) ?? await LoadAsync(StorageArea.Local);
        if (current is not null)
        {
            await TouchAsync(); // slide the expiry window on each visit
        }
    }

    public async Task SignInAsync(string fullName, string email, bool rememberMe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var now = clock.GetUtcNow();
        var sameUser = current is not null && string.Equals(current.Email, email.Trim(), StringComparison.OrdinalIgnoreCase);

        // Switching between remember / don't remember: clear the other storage area.
        await TryRemoveAsync(rememberMe ? StorageArea.Session : StorageArea.Local);

        current = new SessionData(
            FullName: fullName.Trim(),
            Email: email.Trim(),
            Persistent: rememberMe,
            StartedAt: sameUser ? current!.StartedAt : now,
            ExpiresAt: now + SessionLifetime);

        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        current = null;
        await TryRemoveAsync(StorageArea.Local);
        await TryRemoveAsync(StorageArea.Session);
        Changed?.Invoke();
    }

    /// <summary>Extends the session expiry (sliding expiration).</summary>
    public async Task TouchAsync()
    {
        if (current is null) return;
        current = current with { ExpiresAt = clock.GetUtcNow() + SessionLifetime };
        await SaveAsync();
    }

    public bool IsCurrentUser(string? email) =>
        current is not null && string.Equals(current.Email, email, StringComparison.OrdinalIgnoreCase);

    private async Task<SessionData?> LoadAsync(StorageArea area)
    {
        try
        {
            var json = await storage.GetAsync(area, StorageKey);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var data = JsonSerializer.Deserialize(json, EventEaseJsonContext.Default.SessionData);
            if (data is null || string.IsNullOrWhiteSpace(data.Email) || string.IsNullOrWhiteSpace(data.FullName))
            {
                await TryRemoveAsync(area); // damaged data
                return null;
            }

            if (data.ExpiresAt <= clock.GetUtcNow())
            {
                logger.LogInformation("Saved session expired at {ExpiresAt}; signing out.", data.ExpiresAt);
                await TryRemoveAsync(area);
                return null;
            }

            return data;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Saved session was corrupt and has been cleared.");
            await TryRemoveAsync(area);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the saved session from {Area} storage.", area);
            return null;
        }
    }

    private async Task SaveAsync()
    {
        if (current is null) return;
        try
        {
            await storage.SetAsync(current.Persistent ? StorageArea.Local : StorageArea.Session,
                StorageKey, JsonSerializer.Serialize(current, EventEaseJsonContext.Default.SessionData));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not save the session; it will last until the page is reloaded.");
        }
    }

    private async Task TryRemoveAsync(StorageArea area)
    {
        try
        {
            await storage.RemoveAsync(area, StorageKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not clear the session from {Area} storage.", area);
        }
    }

    internal sealed record SessionData(string FullName, string Email, bool Persistent, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt);
}
