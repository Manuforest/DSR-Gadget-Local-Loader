# DSR QuickWarp

QuickWarp is a lightweight offline companion mod for **Dark Souls: Remastered**.

## v0.2

v0.2 keeps the tested PropertyHook position/warp code from v0.1, but replaces the external WinForms menu with an injected DirectX 11 / Dear ImGui overlay.

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

Saved points remain in `quickwarp.json` beside the executable, so v0.1 saves continue to work.

## Safety boundary

v0.2 intentionally allows position warps only when the current `AreaID` matches the saved point. Cross-map loading is a separate feature and is not attempted by this build.

This project modifies live game process state and is intended for offline use.

## Architecture

- C# host: PropertyHook, player coordinates, persistence, injection, named-pipe server.
- Native x64 overlay: DirectX 11 Present/ResizeBuffers hooks, Dear ImGui rendering, keyboard input.
- IPC: `\\.\pipe\DSRQuickWarp` with a small line-based protocol.

The native overlay uses Dear ImGui and MinHook at build time; source dependencies are pinned by the build script / GitHub Actions rather than committed into this repository.
