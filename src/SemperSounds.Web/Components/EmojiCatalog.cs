namespace SemperSounds.Web.Components;

/// <param name="Keywords">Lowercase, space separated. The only thing the picker's search
/// looks at, so an emoji is findable by every word somebody might reach for it with.</param>
public sealed record EmojiEntry(string Emoji, string Keywords);

public sealed record EmojiCategory(string Name, IReadOnlyList<EmojiEntry> Entries);

/// <summary>
/// The emoji offered by <c>EmojiPicker</c>, grouped and searchable.
/// </summary>
/// <remarks>
/// Still curated rather than the full Unicode set — a complete database would need its own
/// name data and virtualised scrolling to stay responsive, which is a different feature.
/// What changed is the ceiling: forty flat swatches meant most sounds got one of the same
/// half-dozen faces. The list lives out here rather than in the component so a test can walk
/// it, which matters because clicking a swatch does no validation of its own — an emoji the
/// parser does not accept would be stored and then quietly render as the default face.
/// </remarks>
public static class EmojiCatalog
{
    public static IReadOnlyList<EmojiCategory> Categories { get; } =
    [
        new("Reactions",
        [
            new("🔥", "fire lit hot flame burn"),
            new("💀", "skull dead rip died dying"),
            new("😂", "laugh crying joy lol funny"),
            new("💯", "hundred perfect score full"),
            new("👏", "clap applause slow clapping"),
            new("🎉", "party tada celebrate confetti"),
            new("💥", "boom explosion impact"),
            new("🚨", "alarm siren alert police emergency"),
            new("📣", "megaphone announce loud shout cheer"),
            new("⚡", "zap lightning fast power"),
            new("✅", "check yes correct tick done"),
            new("❌", "cross no wrong nope"),
            new("❓", "question what confused huh"),
            new("❗", "exclamation warning important"),
            new("⭐", "star favourite favorite rating"),
            new("🏆", "trophy win victory champion"),
            new("🎯", "target bullseye hit accurate"),
            new("🪦", "grave tombstone rip dead buried"),
            new("🤡", "clown joker fool bozo"),
            new("👀", "eyes looking watching sus staring"),
        ]),
        new("Faces",
        [
            new("🙂", "slight smile neutral default polite"),
            new("🙃", "upside down irony sarcasm flipped"),
            new("😭", "sob crying bawling tears sad"),
            new("😱", "scream shock horror fear terrified"),
            new("🤬", "swearing angry cursing rage censored"),
            new("😤", "huff triumph angry steam nostrils"),
            new("🤔", "thinking hmm think ponder"),
            new("🥴", "woozy drunk dizzy confused"),
            new("😴", "sleeping asleep zzz bored snore"),
            new("🫠", "melting embarrassed dissolve awkward"),
            new("🫡", "salute respect yes sir"),
            new("😎", "cool sunglasses deal with it"),
            new("🤯", "mind blown exploding head shocked"),
            new("😬", "grimace awkward yikes cringe"),
            new("🥺", "pleading puppy eyes beg sad"),
            new("😈", "devil evil mischief horns"),
            new("🤢", "nauseated sick gross disgusted"),
            new("🥱", "yawn bored tired sleepy"),
            new("😏", "smirk smug sly knowing"),
            new("🤐", "zipper quiet shush secret silence"),
            new("😐", "neutral blank deadpan expressionless"),
            new("🫥", "dotted invisible gone hidden"),
            new("🤨", "raised eyebrow doubt suspicious skeptical"),
            new("😮", "open mouth surprise wow gasp"),
            new("🧠", "brain smart big think galaxy"),
        ]),
        new("Gestures",
        [
            new("🙏", "pray thanks please folded hands"),
            new("🫵", "pointing you blame accuse"),
            new("👍", "thumbs up yes good approve"),
            new("👎", "thumbs down no bad disapprove"),
            new("🤝", "handshake deal agree truce"),
            new("💪", "muscle strong flex biceps"),
            new("🤷", "shrug dunno whatever idk"),
            new("🤦", "facepalm disappointed ugh"),
            new("👋", "wave hello goodbye bye hi"),
            new("🫶", "heart hands love wholesome"),
            new("🖐️", "hand stop five palm"),
            new("🤌", "pinched fingers italian chef"),
        ]),
        new("Gaming",
        [
            new("🎮", "game controller gaming gamepad"),
            new("🕹️", "joystick arcade retro"),
            new("🎲", "dice random luck roll"),
            new("♟️", "chess pawn strategy"),
            new("🗡️", "sword blade attack dagger"),
            new("🛡️", "shield defend block armour armor"),
            new("💣", "bomb explosive plant defuse"),
            new("🔫", "gun pistol shoot water"),
            new("🎰", "slot machine gamble jackpot"),
            new("🏁", "checkered flag finish race end"),
            new("🥇", "gold medal first win place"),
            new("💎", "gem diamond rank shiny loot"),
            new("👾", "alien monster invader space"),
            new("🤖", "robot bot machine"),
        ]),
        new("Sound",
        [
            new("🎵", "note music tune single"),
            new("🎶", "notes music song melody"),
            new("🎺", "trumpet horn brass fanfare"),
            new("🥁", "drum drums beat percussion"),
            new("🎸", "guitar rock riff"),
            new("🎹", "piano keyboard keys"),
            new("🎤", "microphone mic sing karaoke"),
            new("🎧", "headphones listen audio"),
            new("🔔", "bell ding ring chime"),
            new("📢", "loudspeaker announce loud public"),
            new("🔊", "speaker volume loud sound"),
            new("🔇", "mute silent muted quiet"),
            new("📻", "radio broadcast station"),
            new("🎷", "saxophone sax jazz"),
            new("🪗", "accordion squeezebox polka"),
            new("📯", "postal horn bugle call"),
        ]),
        new("Animals",
        [
            new("🐸", "frog pepe toad ribbit"),
            new("🦆", "duck quack bird"),
            new("🐄", "cow moo cattle"),
            new("🐶", "dog puppy woof bark"),
            new("🐱", "cat kitten meow"),
            new("🐵", "monkey ape"),
            new("🦍", "gorilla ape strong"),
            new("🐐", "goat greatest goated"),
            new("🦀", "crab crabby rave"),
            new("🐙", "octopus tentacle"),
            new("🦖", "dinosaur trex rawr"),
            new("🐔", "chicken cluck hen"),
            new("🦅", "eagle bird screech"),
            new("🐍", "snake hiss sneaky"),
            new("🦇", "bat vampire night"),
            new("🐝", "bee buzz wasp"),
        ]),
        new("Food",
        [
            new("🍆", "aubergine eggplant"),
            new("🍕", "pizza slice"),
            new("🍔", "burger hamburger"),
            new("🌮", "taco tuesday"),
            new("🍺", "beer pint drink cheers"),
            new("🍷", "wine glass drink red"),
            new("☕", "coffee tea hot drink"),
            new("🍿", "popcorn movie watching drama"),
            new("🧀", "cheese cheesy"),
            new("🍌", "banana peel"),
            new("🧃", "juice box drink"),
            new("🎂", "cake birthday celebrate"),
        ]),
        new("Objects",
        [
            new("🚀", "rocket launch space fast"),
            new("💸", "money cash flying broke spent"),
            new("💰", "money bag rich profit"),
            new("📉", "chart down loss decline bad"),
            new("📈", "chart up win growth good"),
            new("⏰", "alarm clock time wake late"),
            new("⌛", "hourglass time waiting patience"),
            new("🔨", "hammer build fix nail"),
            new("🧯", "fire extinguisher stop calm"),
            new("🚪", "door leave exit entry sensor"),
            new("📦", "box package delivery"),
            new("🔑", "key unlock access"),
            new("🛎️", "bell service ding desk"),
            new("📸", "camera photo snap picture"),
            new("🧨", "firecracker explosive dynamite"),
            new("🪃", "boomerang comeback return"),
        ]),
        new("Symbols",
        [
            new("❤️", "heart love red like"),
            new("💔", "broken heart sad breakup"),
            new("♻️", "recycle repeat loop reuse"),
            new("🔁", "repeat loop again cycle"),
            new("⚠️", "warning caution danger"),
            new("🆘", "sos help emergency mayday"),
            new("🔞", "no under eighteen adult nsfw"),
            new("💤", "sleep zzz snore asleep"),
            new("🌀", "dizzy cyclone spiral confused"),
            new("✨", "sparkles shiny magic clean"),
            new("🌈", "rainbow pride colours colors"),
            new("🕳️", "hole void gone disappear"),
        ]),
    ];

    public static IEnumerable<EmojiEntry> All => Categories.SelectMany(category => category.Entries);

    /// <summary>Filters the catalog by keyword, dropping categories left with nothing.</summary>
    /// <remarks>A blank term returns everything rather than nothing, so an untouched search
    /// box is the whole picker rather than an empty panel.</remarks>
    public static IReadOnlyList<EmojiCategory> Search(string? term)
    {
        if (string.IsNullOrWhiteSpace(term)) return Categories;

        var needle = term.Trim();

        return
        [
            .. Categories
                .Select(category => category with
                {
                    // Matching the emoji itself as well as its keywords means pasting one in
                    // finds it, which is how somebody checks whether it is already offered.
                    Entries = [.. category.Entries.Where(entry =>
                        entry.Keywords.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || entry.Emoji.Contains(needle, StringComparison.Ordinal))],
                })
                .Where(category => category.Entries.Count > 0)
        ];
    }
}
