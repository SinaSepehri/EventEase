using System.Text.Json.Serialization;
using EventEase.Models;

namespace EventEase.Services;

/// <summary>
/// Source-generated JSON serialization. Faster than reflection-based serialization
/// (noticeably so in WebAssembly) and safe when the build trims unused code.
/// </summary>
[JsonSerializable(typeof(List<RegistrationRecord>))]
[JsonSerializable(typeof(UserSessionState.SessionData))]
internal sealed partial class EventEaseJsonContext : JsonSerializerContext;
