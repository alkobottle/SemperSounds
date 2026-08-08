# Sound emoji — design

## Why

The whole point of SemperSounds is escaping Discord's cap on soundboard sounds. That
success creates its own problem: a wall of a few hundred near-identically-shaped tiles is
slow to scan. Discord's own soundboard puts an emoji on every sound, and the emoji is what
you actually recognise. This adds the same.

## Decisions

| Question | Choice |
|---|---|
| Emoji sources | SemperBase's custom emoji **and** standard emoji |
| Tile placement | Large, to the left of the name |
| Setting one on existing sounds | Edit dialog on each tile |
| Required? | **Yes.** Existing rows are backfilled with 🙂 |

## Storage

One column, `Sound.Emoji`, NOT NULL, max 64 chars, holding **Discord's own canonical
format**:

- standard emoji → `🔥`
- custom server emoji → `<:kekw:1234567890>`, animated → `<a:party:1234567890>`

That one format covers both cases and is self-describing: the ID yields the CDN URL, the
`a` prefix means GIF, and the name survives the emoji being deleted from the server, so a
tile can still fall back to showing `:kekw:`.

`SoundEmoji` in `Core/Sounds` owns parsing and URL building. No I/O, so it is testable in
isolation like `PcmMixer`.

**Required is enforced as an invariant, not just in the form.** `AddAsync` and
`UpdateAsync` normalize blank to 🙂, so no caller can produce a sound without one, and the
column stays NOT NULL. The picker has no "clear" action and starts pre-filled.

## Components

- **`GuildEmojiProvider`** (Web, singleton) — exposes SemperBase's emoji from the gateway
  cache NetCord already maintains (`Guild.Emojis`). No API calls.
- **`EmojiPicker.razor`** — server emoji from the CDN, a curated set of ~60 common standard
  emoji, and a paste field for anything else. Shipping an emoji database is not warranted
  when the OS picker (`Win`+`.`) already covers the long tail.
- **`SoundEditDialog.razor`** — emoji, name and tags. `SoundLibrary` already had an unused
  update method with no UI; this closes that gap. Editing follows the delete rule: anyone
  in the server.

## Search

Matches name, tags, the emoji character, **and the custom emoji's name**. The last one
matters most: a custom emoji cannot easily be typed into a search box, so `kekw` has to
find the sound wearing `:kekw:`. Without it, custom emoji would be decorative rather than
findable, which is the point of the feature.

## Testing

- `SoundEmoji`: standard passes through; `<:name:id>` and `<a:name:id>` parse; animated
  yields `.gif`; malformed input is rejected rather than stored.
- Blank emoji in, default emoji stored — the invariant, from the service's side.
- Search matches on emoji character and on custom emoji name.

## Out of scope

Grouping or sorting the board by emoji. Search already delivers findability, and grouping
is a much larger change to the grid.
