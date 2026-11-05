using Microsoft.JSInterop;

namespace EventEase.Services;

/// <summary>Which browser storage area to use.</summary>
public enum StorageArea
{
    /// <summary>localStorage: kept across browser restarts ("remember me").</summary>
    Local,

    /// <summary>sessionStorage: kept across reloads, cleared when the tab is closed.</summary>
    Session
}

/// <summary>
/// Small key/value abstraction over browser storage. Services depend on this interface
/// instead of IJSRuntime, so they can be unit tested with an in-memory fake.
/// </summary>
public interface IBrowserStorage
{
    ValueTask<string?> GetAsync(StorageArea area, string key);
    ValueTask SetAsync(StorageArea area, string key, string value);
    ValueTask RemoveAsync(StorageArea area, string key);
}

/// <summary>
/// Browser storage via JS interop. Calls the built-in Storage API directly
/// (no extra JS file or NuGet package needed).
/// </summary>
public sealed class BrowserStorage(IJSRuntime js) : IBrowserStorage
{
    private static string Area(StorageArea area) => area == StorageArea.Local ? "localStorage" : "sessionStorage";

    public ValueTask<string?> GetAsync(StorageArea area, string key) =>
        js.InvokeAsync<string?>($"{Area(area)}.getItem", key);

    public ValueTask SetAsync(StorageArea area, string key, string value) =>
        js.InvokeVoidAsync($"{Area(area)}.setItem", key, value);

    public ValueTask RemoveAsync(StorageArea area, string key) =>
        js.InvokeVoidAsync($"{Area(area)}.removeItem", key);
}
