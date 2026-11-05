using EventEase.Services;

namespace EventEase.Tests;

/// <summary>State management: the user's session must survive reloads and fail safely.</summary>
public class UserSessionStateTests
{
    private const string Key = UserSessionState.StorageKey;

    [Fact]
    public async Task Remembered_session_is_restored_after_reload()
    {
        var storage = new FakeBrowserStorage();
        var clock = new ManualTimeProvider();

        var (_, first, _) = TestServices.Create(storage, clock);
        await first.SignInAsync("  Test User ", " test@example.com ", rememberMe: true);
        Assert.True(storage.Has(StorageArea.Local, Key));

        // "Reload": brand-new service instances reading the same browser storage.
        clock.Advance(TimeSpan.FromHours(5));
        var (_, second, _) = TestServices.Create(storage, clock);
        await second.InitializeAsync();

        Assert.True(second.IsSignedIn);
        Assert.Equal("Test User", second.FullName);
        Assert.Equal("test@example.com", second.Email);
        Assert.True(second.IsPersistent);
    }

    [Fact]
    public async Task Not_remembered_session_uses_session_storage_only()
    {
        var storage = new FakeBrowserStorage();
        var (_, session, _) = TestServices.Create(storage);

        await session.SignInAsync("Test User", "test@example.com", rememberMe: false);

        Assert.True(storage.Has(StorageArea.Session, Key));
        Assert.False(storage.Has(StorageArea.Local, Key));

        var (_, reloaded, _) = TestServices.Create(storage);
        await reloaded.InitializeAsync();
        Assert.True(reloaded.IsSignedIn); // survives a reload in the same tab
        Assert.False(reloaded.IsPersistent);
    }

    [Fact]
    public async Task Turning_remember_off_removes_the_local_copy()
    {
        var storage = new FakeBrowserStorage();
        var (_, session, _) = TestServices.Create(storage);

        await session.SignInAsync("Test User", "test@example.com", rememberMe: true);
        await session.SignInAsync("Test User", "test@example.com", rememberMe: false);

        Assert.False(storage.Has(StorageArea.Local, Key));
        Assert.True(storage.Has(StorageArea.Session, Key));
    }

    [Fact]
    public async Task Expired_session_is_cleared_on_load()
    {
        var storage = new FakeBrowserStorage();
        var clock = new ManualTimeProvider();
        var (_, session, _) = TestServices.Create(storage, clock);
        await session.SignInAsync("Test User", "test@example.com", rememberMe: true);

        clock.Advance(UserSessionState.SessionLifetime + TimeSpan.FromMinutes(1));
        var (_, reloaded, _) = TestServices.Create(storage, clock);
        await reloaded.InitializeAsync();

        Assert.False(reloaded.IsSignedIn);
        Assert.False(storage.Has(StorageArea.Local, Key));
    }

    [Fact]
    public async Task Visiting_slides_the_expiry_forward()
    {
        var storage = new FakeBrowserStorage();
        var clock = new ManualTimeProvider();
        var (_, session, _) = TestServices.Create(storage, clock);
        await session.SignInAsync("Test User", "test@example.com", rememberMe: true);

        // Visit on day 6, then again on day 12: still signed in because each visit extends the session.
        clock.Advance(TimeSpan.FromDays(6));
        var (_, day6, _) = TestServices.Create(storage, clock);
        await day6.InitializeAsync();
        Assert.True(day6.IsSignedIn);

        clock.Advance(TimeSpan.FromDays(6));
        var (_, day12, _) = TestServices.Create(storage, clock);
        await day12.InitializeAsync();
        Assert.True(day12.IsSignedIn);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("{\"FullName\":\"\",\"Email\":\"\"}")]
    public async Task Corrupt_saved_session_is_ignored_and_cleared(string json)
    {
        var storage = new FakeBrowserStorage();
        storage.Items[(StorageArea.Local, Key)] = json;
        var (_, session, _) = TestServices.Create(storage);

        await session.InitializeAsync(); // must not throw

        Assert.False(session.IsSignedIn);
        Assert.False(storage.Has(StorageArea.Local, Key));
    }

    [Fact]
    public async Task Broken_storage_never_breaks_the_session()
    {
        var storage = new FakeBrowserStorage { Throw = true };
        var (_, session, _) = TestServices.Create(storage);

        await session.InitializeAsync();
        await session.SignInAsync("Test User", "test@example.com", rememberMe: true);

        Assert.True(session.IsSignedIn); // still works in memory

        await session.SignOutAsync();
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task Sign_out_clears_both_storage_areas_and_notifies()
    {
        var storage = new FakeBrowserStorage();
        var (_, session, _) = TestServices.Create(storage);
        await session.SignInAsync("Test User", "test@example.com", rememberMe: true);
        storage.Items[(StorageArea.Session, Key)] = "leftover";

        var notifications = 0;
        session.Changed += () => notifications++;
        await session.SignOutAsync();

        Assert.False(session.IsSignedIn);
        Assert.Empty(storage.Items);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Signing_in_again_as_same_user_keeps_session_start()
    {
        var clock = new ManualTimeProvider();
        var (_, session, _) = TestServices.Create(clock: clock);
        await session.SignInAsync("Test User", "test@example.com", true);
        var started = session.StartedAt;

        clock.Advance(TimeSpan.FromHours(2));
        await session.SignInAsync("Test User", "TEST@example.com", true);

        Assert.Equal(started, session.StartedAt);
    }

    [Fact]
    public async Task IsCurrentUser_is_case_insensitive()
    {
        var (_, session, _) = TestServices.Create();
        await session.SignInAsync("Test User", "Test@Example.com", true);
        Assert.True(session.IsCurrentUser("test@example.com"));
        Assert.False(session.IsCurrentUser("other@example.com"));
    }
}
