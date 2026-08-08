using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SemperSounds.Core.Sounds;

/// <summary>
/// The emoji shown on a sound tile, stored in Discord's own canonical format so one
/// column covers both standard emoji and the server's custom ones.
/// </summary>
/// <remarks>
/// Standard emoji are stored as the character itself ("🔥"). Custom emoji use Discord's
/// form: <c>&lt;:kekw:1234567890&gt;</c>, or <c>&lt;a:party:123&gt;</c> when animated.
/// That form is self-describing — the ID gives the CDN URL, the "a" marks a GIF, and the
/// name survives the emoji being deleted from the server, so the tile can still show
/// <c>:kekw:</c> rather than going blank.
/// </remarks>
public readonly partial record struct SoundEmoji
{
    /// <summary>Used whenever no emoji was supplied. Every sound has one.</summary>
    public const string DefaultEmoji = "🙂";

    /// <summary>Longest value accepted, matching the database column.</summary>
    public const int MaxLength = 64;

    private SoundEmoji(string raw, bool isCustom, string? name, ulong id, bool isAnimated)
    {
        Raw = raw;
        IsCustom = isCustom;
        Name = name;
        Id = id;
        IsAnimated = isAnimated;
    }

    /// <summary>The stored representation.</summary>
    public string Raw { get; }

    public bool IsCustom { get; }

    /// <summary>Custom emoji name, null for standard emoji.</summary>
    public string? Name { get; }

    public ulong Id { get; }

    public bool IsAnimated { get; }

    /// <summary>CDN image for a custom emoji, null for standard emoji.</summary>
    public string? ImageUrl => IsCustom
        ? $"https://cdn.discordapp.com/emojis/{Id}.{(IsAnimated ? "gif" : "png")}"
        : null;

    /// <summary>
    /// Text to show when no image can be rendered: the character itself for standard
    /// emoji, or <c>:name:</c> for a custom one whose image is unavailable.
    /// </summary>
    public string Display => IsCustom ? $":{Name}:" : Raw;

    /// <summary>
    /// What the search box matches against. Includes the custom emoji's name, without
    /// which custom emoji could not be searched for at all — they cannot be typed.
    /// </summary>
    public string SearchText => IsCustom ? Name! : Raw;

    [GeneratedRegex(@"^<(?<animated>a)?:(?<name>\w{2,32}):(?<id>\d{1,20})>$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomEmojiPattern { get; }

    public static bool TryParse(string? value, out SoundEmoji emoji)
    {
        emoji = default;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        var trimmed = value.Trim();

        var match = CustomEmojiPattern.Match(trimmed);
        if (match.Success)
        {
            if (!ulong.TryParse(match.Groups["id"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return false;
            }

            emoji = new SoundEmoji(
                trimmed,
                isCustom: true,
                name: match.Groups["name"].Value,
                id: id,
                isAnimated: match.Groups["animated"].Success);

            return true;
        }

        if (!LooksLikeStandardEmoji(trimmed))
        {
            return false;
        }

        emoji = new SoundEmoji(trimmed, isCustom: false, name: null, id: 0, isAnimated: false);
        return true;
    }

    public static SoundEmoji Parse(string value) =>
        TryParse(value, out var emoji)
            ? emoji
            : throw new FormatException($"'{value}' is not a usable emoji.");

    /// <summary>
    /// Coerces any input into something storable, falling back to <see cref="DefaultEmoji"/>.
    /// Keeps "every sound has an emoji" true regardless of which caller is involved,
    /// rather than relying on the upload form to enforce it.
    /// </summary>
    public static string Normalize(string? value) =>
        TryParse(value, out var emoji) ? emoji.Raw : DefaultEmoji;

    /// <summary>
    /// Accepts a short run of characters that is not plain text. Emoji are built from
    /// pictographic runes plus joiners, modifiers and variation selectors, and this
    /// checks for at least one pictographic rune while rejecting ordinary words.
    /// </summary>
    private static bool LooksLikeStandardEmoji(string value)
    {
        // Longest common sequences (flags, families with joiners and skin tones) stay
        // well inside this; anything longer is prose, not an emoji.
        if (value.Length > 16)
        {
            return false;
        }

        var hasPictographic = false;

        foreach (var rune in value.EnumerateRunes())
        {
            if (IsPictographic(rune))
            {
                hasPictographic = true;
                continue;
            }

            if (!IsEmojiModifier(rune))
            {
                return false;
            }
        }

        return hasPictographic;
    }

    private static bool IsPictographic(System.Text.Rune rune) => rune.Value switch
    {
        // Covers emoticons, transport, symbols and pictographs. Regional indicators
        // (flags, 0x1F1E6..0x1F1FF) fall inside this range too.
        >= 0x1F000 and <= 0x1FAFF => true,
        >= 0x2600 and <= 0x27BF => true,    // misc symbols and dingbats
        0x203C or 0x2049 => true,           // ‼ ⁉
        >= 0x2190 and <= 0x21FF => true,    // arrows
        >= 0x2B00 and <= 0x2BFF => true,    // additional symbols
        _ => false,
    };

    private static bool IsEmojiModifier(System.Text.Rune rune) => rune.Value switch
    {
        0x200D => true,                     // zero-width joiner
        0xFE0E or 0xFE0F => true,           // variation selectors
        >= 0x1F3FB and <= 0x1F3FF => true,  // skin tone modifiers
        >= 0x20E0 and <= 0x20FF => true,    // combining marks (keycaps)
        >= 0x0030 and <= 0x0039 => true,    // digits, for keycap sequences like 1️⃣
        0x0023 or 0x002A => true,           // # and *, likewise keycaps
        _ => false,
    };
}
