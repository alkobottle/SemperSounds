namespace SemperSounds.Contracts;

/// <summary>
/// One clip as the desktop client sees it. A projection of <c>Sound</c>, not the entity:
/// this assembly deliberately references nothing, so the desktop app never drags EF Core
/// and NetCord in behind it.
/// </summary>
/// <param name="EmojiText">
/// Display form, already flattened. <c>Sound.Emoji</c> holds Discord's canonical form, which
/// for a custom emoji is <c>&lt;:name:123&gt;</c> — rendering that raw shows the markup rather
/// than the emoji. The parser lives in Core, so the hub resolves this before it goes on the wire.
/// </param>
/// <param name="EmojiImageUrl">CDN url for a custom emoji, null for a standard one.</param>
public sealed record SoundSummary(
    Guid Id, string Name, string EmojiText, string? EmojiImageUrl, IReadOnlyList<string> Tags, int DurationMs);
