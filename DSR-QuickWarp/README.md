# DSR QuickWarp v0.1

A deliberately small companion for **Dark Souls: Remastered** built from the position/warp logic already present in DSR Gadget Local Loader.

## What v0.1 does

- **F6**: save one quick return point.
- **F7**: warp to the quick return point.
- **F8**: open/close a small borderless overlay.
- In the overlay: **Insert** saves the current position, **Enter** warps, **Delete** removes a point, **Esc** closes.
- **Ctrl+F8**: exit QuickWarp.
- Saved points persist to `quickwarp.json` beside the executable.

## Safety boundary in v0.1

Cross-area warp is intentionally blocked. A point may only be restored while the player is in the same `AreaID`. This avoids trying to write coordinates into a map that is not currently loaded.

Use this offline. DSR Gadget itself recommends offline use, and QuickWarp performs the same kind of runtime process-memory access for position warping.

## Why this is an EXE first

This first version is a lightweight external process with a borderless top-most in-game overlay. It does **not** inject a DirectX DLL. That keeps the first testable version small and lets it reuse PropertyHook directly. If the position workflow proves stable, the UI can later be moved to an injected ImGui overlay without changing the saved-point model.

## Build

Clone with submodules:

```
git clone --recursive <your-fork-url>
```

Build x64 Release:

```
msbuild DSR-QuickWarp\DSR-QuickWarp.csproj /p:Configuration=Release /p:Platform=x64
```

Output:

```
DSR-QuickWarp\bin\x64\Release\DSR QuickWarp.exe
```

Start Dark Souls Remastered, stay offline, then run `DSR QuickWarp.exe`.
