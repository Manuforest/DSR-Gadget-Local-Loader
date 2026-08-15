# DSR QuickWarp

QuickWarp is a lightweight offline companion mod for **Dark Souls: Remastered**.

## v0.2

v0.2 keeps the tested PropertyHook position/warp code from v0.1, replaces the external WinForms menu with an injected DirectX 11 / Dear ImGui overlay, and adds cross-map saved-point warping.

### Controls

- `F6` — save the quick point
- `F7` — warp to the quick point
- `F8` — open/close the in-game QuickWarp panel
- `Up / Down` — select a saved point
- `Enter` — warp to the selected point
- `Insert` — save the current position as a permanent point
- `Delete` — delete the selected point
- `Esc` — close the panel

### Installation / use

Keep these three files together in any writable folder:

- `DSR QuickWarp.exe`
- `PropertyHook.dll`
- `DSR QuickWarp Overlay.dll`

1. Start Dark Souls: Remastered in offline mode.
2. Run `DSR QuickWarp.exe`.
3. The host attaches to the game and injects the overlay automatically.
4. Press `F8` in game.

Saved points remain in `quickwarp.json` beside the executable. Existing v0.1/v0.2 point files remain compatible; missing map-group data is inferred from the stored AreaID.

QuickWarp exits automatically after the Dark Souls: Remastered process it attached to closes.

## Cross-map warp

When the saved point belongs to the currently loaded map group, QuickWarp uses the same direct position warp proven stable in v0.1.

When the saved point belongs to another map group, QuickWarp:

1. Chooses a known safe bonfire in the destination map as a temporary loading anchor.
2. Temporarily points the game's `LastBonfire` field at that anchor.
3. Calls the game's own bonfire-warp function to load the destination map.
4. Immediately restores the player's original `LastBonfire`, so normal death/respawn behavior is not intentionally changed.
5. Waits for the destination map/player state to become available and remain stable for several frames.
6. Applies the saved X/Y/Z/angle as the final precise destination.

A cross-map warp times out rather than forcing coordinates if the target map does not become ready.

Known loading anchors currently cover the standard map groups for Depths, Undead Burg/Parish, Firelink, Painted World, Darkroot, Oolacile, Catacombs, Tomb of the Giants, Ash Lake/Great Hollow, Blighttown, Demon Ruins/Lost Izalith, Sen's Fortress, Anor Londo, New Londo, Duke's Archives/Crystal Cave, Kiln, and Northern Undead Asylum.

### Cross-map test checklist

For the first runtime pass, use two ordinary non-boss locations in clearly different map groups, for example Firelink Shrine and Anor Londo or Undead Burg and Blighttown.

Expected sequence:

1. Save point A.
2. Travel normally to a different map and save point B.
3. Select point A and warp.
4. The game should enter its normal loading transition.
5. QuickWarp should show `Loading target map...` and then `Warped`.
6. The player should land at the exact saved coordinates rather than remain at the temporary bonfire anchor.
7. Repeating the warp in the other direction should behave the same way.
8. After closing Dark Souls: Remastered, `DSR QuickWarp.exe` should terminate automatically.

Avoid using boss-room points for the first cross-map validation pass because several boss AreaIDs are negative/special-case IDs and should be validated separately after the standard world map groups are confirmed.

## Safety boundary

This project modifies live game process state and is intended for offline use. Cross-map warp uses the game's own map-loading path before performing the final coordinate warp; it does not directly force coordinates into an unloaded map.

## Architecture

- C# host: PropertyHook, player coordinates, persistence, map-loading state machine, injection, named-pipe server.
- Native x64 overlay: DirectX 11 Present/ResizeBuffers hooks, Dear ImGui rendering, keyboard input.
- IPC: `\\.\pipe\DSRQuickWarp` with a small line-based protocol. The overlay polls lightweight host state while idle so asynchronous map-warp completion and timeout status can be shown in game.

The native overlay uses Dear ImGui and MinHook at build time; source dependencies are pinned by the build script / GitHub Actions rather than committed into this repository.
