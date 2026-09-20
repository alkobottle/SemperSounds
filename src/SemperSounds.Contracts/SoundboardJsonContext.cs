using System.Text.Json.Serialization;

namespace SemperSounds.Contracts;

/// <summary>
/// Source-generated serialization for everything that crosses the hub.
/// </summary>
/// <remarks>
/// <para>
/// The desktop client is published trimmed, and trimming turns off reflection-based
/// serialization outright: <c>System.Text.Json</c> throws
/// "Reflection-based serialization has been disabled for this application" the first time it
/// is asked to look at a type it has no metadata for. Generating that metadata at build time
/// is what keeps these types serializable once the reflection path is gone.
/// </para>
/// <para>
/// It lives in Contracts because both ends need it: the server writes these shapes and the
/// client reads them, and a type added to one side without the other is a runtime failure
/// rather than a build one.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BotState))]
[JsonSerializable(typeof(PlayResult))]
[JsonSerializable(typeof(SoundSummary))]
[JsonSerializable(typeof(IReadOnlyList<SoundSummary>))]
[JsonSerializable(typeof(List<SoundSummary>))]
[JsonSerializable(typeof(NowPlaying))]
[JsonSerializable(typeof(IReadOnlyList<NowPlaying>))]
[JsonSerializable(typeof(List<NowPlaying>))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DeviceTokenRequest))]
[JsonSerializable(typeof(DeviceTokenResponse))]
[JsonSerializable(typeof(DeviceTokenError))]
public sealed partial class SoundboardJsonContext : JsonSerializerContext;
