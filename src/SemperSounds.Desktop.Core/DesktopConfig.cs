using System.Text.Json;
using System.Text.Json.Serialization;

namespace SemperSounds.Desktop.Core;

/// <summary>One key mapped to one clip.</summary>
/// <param name="CachedName">
/// The sound's name as of the last time the library was read. Kept so the window can draw
/// before the server answers, and so a binding whose sound has been deleted can say which one
/// it was rather than showing a bare identifier.
/// </param>
public sealed class HotkeyBinding
{
    public Guid SoundId { get; set; }

    [JsonIgnore]
    public HotkeyChord Chord { get; set; } = HotkeyChord.None;

    /// <summary>The chord as text, which is the only form that goes to disk.</summary>
    [JsonPropertyName("chord")]
    public string ChordText
    {
        get => Chord.ToString();
        set => Chord = HotkeyChord.Parse(value);
    }

    public string CachedName { get; set; } = string.Empty;
}

/// <summary>
/// Everything the desktop app remembers between runs.
/// </summary>
/// <remarks>
/// Local rather than on the server, unlike board preferences: a hotkey layout belongs to a
/// keyboard, and the same person on two machines rarely wants the same one.
/// </remarks>
public sealed class DesktopConfig
{
    public string ServerUrl { get; set; } = string.Empty;

    public List<HotkeyBinding> Bindings { get; set; } = [];

    [JsonIgnore]
    public HotkeyChord StopAllChord { get; set; } = HotkeyChord.None;

    [JsonIgnore]
    public HotkeyChord SummonChord { get; set; } = HotkeyChord.None;

    [JsonIgnore]
    public HotkeyChord MuteChord { get; set; } = HotkeyChord.None;

    [JsonPropertyName("stopAllChord")]
    public string StopAllChordText
    {
        get => StopAllChord.ToString();
        set => StopAllChord = HotkeyChord.Parse(value);
    }

    [JsonPropertyName("summonChord")]
    public string SummonChordText
    {
        get => SummonChord.ToString();
        set => SummonChord = HotkeyChord.Parse(value);
    }

    [JsonPropertyName("muteChord")]
    public string MuteChordText
    {
        get => MuteChord.ToString();
        set => MuteChord = HotkeyChord.Parse(value);
    }

    /// <summary>Summon the bot automatically when a press finds it elsewhere.</summary>
    public bool AutoSummon { get; set; } = true;

    /// <summary>
    /// Both off by default. The overlay cannot draw over an exclusive-fullscreen game and a
    /// window that takes focus would minimise one, so it is opt-in until the user has seen it
    /// behave against their own game.
    /// </summary>
    public bool ShowOverlay { get; set; }

    public bool PlayAudioCue { get; set; }

    public bool RunAtStartup { get; set; }

    public bool StartMinimised { get; set; } = true;

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // The default encoder rewrites '+' into a unicode escape, so a chord would reach disk
        // with the separator spelled out as an escape sequence rather than a plus sign. That
        // still round-trips, but this file is meant to be read and edited by hand. The relaxed
        // encoder is safe here: it is a local file, never embedded in HTML or a script.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reads a config, falling back to defaults for anything unreadable.
    /// </summary>
    /// <remarks>
    /// Never throws, for the same reason <c>BoardPreferencesJson.Deserialize</c> does not: the
    /// file is one a person may reasonably open and edit, and a half-written or hand-mangled
    /// one must cost them their settings, not the app's ability to start.
    /// </remarks>
    public static DesktopConfig Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DesktopConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<DesktopConfig>(json, SerializerOptions) ?? new DesktopConfig();
        }
        catch (JsonException)
        {
            return new DesktopConfig();
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);
}
