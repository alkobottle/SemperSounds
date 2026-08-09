# Per-user favourites with keyboard shortcuts — design

## Why

Hotkeys already existed, but they were bound positionally: `OnHotkey` indexed into the
*filtered* grid, so `1` played whatever happened to be first. Typing in the search box, or
anyone uploading a sound that sorts earlier, silently changed what every key did. That is
useless for muscle memory, which is the only reason to have a shortcut at all.

Favourites fix this by giving each user a small, stable set of sounds whose keys never move.

## Decisions

| Question | Choice |
|---|---|
| Key assignment | Automatic slots 1–9, in favourite order |
| Existing positional hotkeys | Replaced — one key now means one sound, permanently |
| Placement | Pinned row above the grid, unaffected by search and tag filters |

## Data model

`Favorite` — `UserId`, `SoundId`, `Slot`, `CreatedAt`. This is the **first per-user state in
the application**; the library, the play log and playback are all shared, so it is the first
table keyed by who you are.

Two constraints live in the schema rather than only in code: unique on `(UserId, SoundId)`
so a sound cannot be starred twice, and unique on `(UserId, Slot)` so a slot holds one sound.

It carries a **real foreign key to `Sound` with cascade delete**, deliberately unlike
`PlayLogEntry`, which has none. The distinction is meaningful: a play that happened is
history and has to survive the sound being deleted, whereas a favourite pointing at a
deleted sound is a dangling pointer. Anyone may delete a sound, and doing so should quietly
drop it from everybody's favourites.

## Slot behaviour

Starring takes the **lowest free slot**. Un-starring **compacts** the remaining favourites
upward, because gaps would leave keys such as 1, 3, 7 with no way to reach 2.

Starring a tenth is **refused with a message** rather than silently evicting an existing
favourite, since the whole value of the feature is that the keys stay where you left them.

## Keyboard

`OnHotkey` looks the favourite up by slot instead of indexing the filtered grid. The
existing JS is reused unchanged — it already ignores keystrokes aimed at inputs, so typing
in the search box does not fire sounds.

Authorization is untouched: keys go through `PlaybackService.PlayAsync`, so being in the
bot's current voice channel is still required. The shortcut is a convenience, not a bypass.

## Structure

Favourites live in their own scoped `FavoriteLibrary`. `SoundLibrary` already owns upload,
deletion, tags, the play log and PCM reading; favourites are an unrelated concern and adding
them there would make a large file larger.

## Tests

Lowest free slot on star; compaction on unstar; the tenth refused; reordering swaps slots;
**one user's favourites never appear in another's**; deleting a sound removes it from
everyone's favourites.

## Out of scope

Sharing or copying favourite sets between users, and per-user ordering of the main grid.
