using EventEase;
using EventEase.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Logging.SetMinimumLevel(builder.HostEnvironment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning);

// The ?count=N mock-data generator is a testing aid, so it's only enabled in Development.
builder.Services.AddSingleton(new AppOptions { EnableMockDataGenerator = builder.HostEnvironment.IsDevelopment() });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBrowserStorage, BrowserStorage>();

// Shared event catalog. User-specific state is scoped (in WebAssembly a scope lasts as long as the tab).
builder.Services.AddSingleton<EventService>();
builder.Services.AddScoped<UserSessionState>();
builder.Services.AddScoped<AttendanceService>();

var host = builder.Build();

// Restore the user's session and saved registrations *before* the first render,
// so a reload never flashes a signed-out state or wrong seat counts.
await host.Services.GetRequiredService<UserSessionState>().InitializeAsync();
await host.Services.GetRequiredService<AttendanceService>().InitializeAsync();

await host.RunAsync();
