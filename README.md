# AegisMouse

Universal mouse macro engine — works with any brand of mouse. Input is synthesized at the OS level (SendInput + low-level hooks), so no vendor driver is required.

**Current version: 1.4.1**

## Changelog

### 1.4.1 — Aim tab scroll fix
- The Aim tab had no scrollbar: new 1.4.0 rows overflowed the window and the triggerbot card collapsed to zero height (invisible). Tab now scrolls, all rows Auto.

### 1.4.0 — Second color range + triggerbot
- Second target color band (alternate outfits) — OR-ed into detection with the same shape/density gates, full sliders in the Aim tab, compact tuning in the debug window. Off by default.
- Triggerbot — fires one shot per lock acquisition after the aim point holds on the crosshair for the set delay, with 2x re-arm band so edge jitter can't spray. Own toggle + delay + tolerance. Off by default.

### 1.3.9 — Aim stability trio
- Velocity prediction — lock history extrapolates 2 frames ahead (capped), so strafing enemies are led instead of trailed
- IoU hybrid lock matching — box overlap rides through occlusion split/merge and sudden box growth that used to drop the lock
- Detection allocation pooling — row buffers reused across scanlines, no more GC-pulse pull unevenness

### 1.3.8 — Rapid-fire timing fix (SMGs felt slow/semi-auto)
- Fire loops now use the 1 ms high-resolution timer (previously only hold-macros did — every rapid/burst/recoil wait overshot on the ~15.6 ms coarse timer, worst at high SMG RPM)
- Hold time no longer stacks on top of the shot interval — a 600 RPM setting now delivers 600 RPM instead of ~555
- Tip: set each weapon binding to its native rate (SMGs ~850–1000, rifles ~550–650)

### 1.3.7 — Settings & dropdown wiring audit
- Fixed dead "Armed while" dropdown (wrong value paths — rewired to shared static list + SelectedItem wrapper, same pattern as 1.3.4)
- Fixed pull strength slider (10–95 range vs 0.05–0.99 value forced max pull + "1000%" display — now 0.05–0.99 in both windows)
- Fixed Macros empty-state (watched the Recoil grid with copy-pasted text)
- Disabled blank-grid-row entry on all 3 grids (rows added that way skipped auto-save wiring — use the + Add buttons)
- Wired the advertised F9 global hotkey to recoil calibration (UI-thread marshaled, ignored while typing)
- Added missing Aim tab sliders: max turn, deadzone (new setting), aim point, capture radius

### 1.3.6 — Aim assist tracking overhaul (Knives Out)
- Fixed false locks on grass/objects — new validation gates: max target size (scenery), humanoid aspect ratio (0.8–5.0 h/w), blob density (≥25% fill rejects sparse speckles)
- Fixed wild aim rotation — new sticky `TargetLock`: 3-frame debounce before pull starts, lock follows the same blob, challengers steal it only with a 30 px margin (no more 60 Hz A/B flicker)
- Smoothed output — EMA filter on pull deltas, lock and smoothing reset on trigger release
- Status-bar version now reads the shipped assembly version (was hardcoded v1.0.1)
- 9 new tests (42 passing)

### 1.3.5 — Security hardening
- Fixed aim-vision hang — union-find state moved to a dedicated label→parent map (the old per-run parent scheme could cycle forever on real screen content)
- Untrusted DB values are now sanitized on load, not just on UI save (Repeat/Dx/Vk/strength clamps + collection caps — same-user DB tampering can no longer cause input floods or wild mouse jumps)
- Macro key codes clamped to 0..255 (larger ints were silently truncated to the wrong key)
- Wheel-tick dispatches capped at 8 per event (synthetic wheel spam can no longer spawn unbounded threads)

### 1.3.4 — Aim trigger dropdown fix
- "Hold-to-aim mouse button" ComboBox was unclickable (WPF bug: null-key item in `SelectedValue`/`KeyValuePair` binding) — rewired to a wrapper class + `SelectedItem` binding; Left/Right/Middle/Side1/Side2 now selectable

### 1.3.3 — Steady debug frame + hotkey fixes
- Debug preview is now frozen at the position where you opened it — moving the mouse no longer shifts the frame (cyan box shows your live cursor instead)
- R re-centers the capture region on your cursor; F3 (was F9, conflicts with in-game training binds) saves the frame, Ctrl+S still works

### 1.3.2 — Aim debug save fixes
- Save button was clipped at compact window size — side panel is now scrollable, button always reachable
- F9 (or Ctrl+S) saves the current frame + HSV settings without moving the mouse off the target — no more lost frames from closing the window

### 1.3.1 — Aim debug usability
- Opening the vision debug window now minimizes AegisMouse to the tray so the game screen is fully visible; closing it restores the app
- Debug window compacted and docked to the corner of the monitor the mouse is on (draggable/resizable) — no longer covers the crosshair
- Click-to-pick now shows the picked color swatch + R/G/B → H/S/V values so you can see what was locked
- Crosshair cursor + hint pill on the preview frame

### 1.3.0 — Soft aim assist
- New "Aim Assist" tab — optional, per-profile, off by default (existing macro/recoil functions untouched)
- Color-vision target detection (pure C#, HSV + scanline connected components — no native dependencies)
- Hold-to-arm trigger (mouse button or keyboard key), FOV radius, pull strength (fractional pull per frame — no hard lock), max turn rate, aim-point offset, min target size
- Vision debug window — live frame, FOV/target overlays, click-to-pick enemy color, save frame + settings for analysis (`%ProgramData%\AegisMouse\aimdebug\`)

### 1.2.0 — Premium UI + SaaS polish
- Tab icons on every section, license badge, accent line under the header
- Recoil pattern visualizer — live trajectory canvas (cumulative Dx/Dy path with shot dots)
- Delete-confirm popovers for profiles, bindings, macro actions, and recoil rows (no accidental deletes)
- Empty states for the Macros and Recoil grids
- Enhanced status bar and sidebar indicator
- F9 hint for calibration
- Fixed TabItem nesting bug and recoil-visualizer API errors introduced during the redesign

### 1.1.0 — Arch terminal UI redesign
- Redesigned header — redundant "AM" logo icon removed, title/subtitle left-aligned
- True solid-black canvas (Linux/terminal aesthetic) — Mica/Acrylic translucent backdrops disabled, dark title bar applied first
- Arch-style tab styling — stronger accent tint on selected tabs with bottom accent underline
- Fixed header overlap and invisible dropdown/tab text (explicit white text in ComboBox items)
- Fixed encoding bug in the profile summary (mojibake `Â·` → `·`)

### 1.0.1 — Security hardening
- Fixed loop-cancellation race — a re-pressed trigger always stops the correct macro loop (no orphaned/overrun macros)
- Keyboard hooks now use the correct injected-input flag (`LLKHF_INJECTED`), preventing future feedback loops
- Hook callbacks exception-guarded — a failed dispatch cannot crash the process
- Thread-safe humanizer RNG and save pipeline (no cross-thread collection access)
- Value clamps on save (RPM, burst, repeat, movement, DPI, jitter)
- Storage ACL lockdown — `%ProgramData%\AegisMouse` write-restricted to the current user, Administrators, and SYSTEM

## Features

- **Rapid fire** — up to 1000+ RPM click automation on any button
- **Recoil compensation** — per-shot Dx/Dy table with strength scaling and in-app calibration
- **Hold macros** — multi-action sequences (clicks, keys, mouse moves) with per-action delay/hold/repeat
- **Combined mode** — rapid fire + recoil in one trigger
- **Humanization** — timing jitter, move jitter, and step-skip to avoid detection patterns
- **Profiles** — multiple named profiles, each with its own bindings, recoil table, and settings
- **SQLite persistence** — all settings stored in `%ProgramData%\AegisMouse\data\aegismouse.db`
- **Tray integration** — minimizes to system tray, optional start with Windows
- **1 ms precision** — high-resolution timer execution

## Security

- Storage in `%ProgramData%\AegisMouse` is ACL-locked at startup — only the current user, Administrators, and SYSTEM have write access; standard users get read-only
- Low-level hooks ignore injected input (mouse `LLMHF_INJECTED`, keyboard `LLKHF_INJECTED`), preventing feedback loops
- Hook callbacks are exception-guarded so a failed dispatch cannot crash the process
- Loop cancellation is race-safe — a re-pressed trigger always stops the correct macro loop
- All persisted values are clamped on save (RPM, burst count, repeat, movement deltas, DPI, jitter) and SQL is fully parameterized
- Single-instance mutex, no network surface, no hardcoded secrets, signed-less (supply-chain hardening planned)

## Tech stack

- .NET 8 (net8.0-windows), WPF, MVVM
- C# projects: `App` (UI), `Core` (models), `Database` (SQLite), `Engine` (hooks + input)
- xUnit tests (27 passing)

## Build

```powershell
dotnet build AegisMouse.sln -c Release
dotnet test tests/Tests/AegisMouse.Tests.csproj -c Release
dotnet publish src/App/AegisMouse.App.csproj -c Release -r win-x64 --self-contained true
```

The app requires administrator privileges to inject input into elevated (e.g., anti-cheat-protected) games.

## Legal note

Automated input may violate the terms of service of some games. Use responsibly — only on games and servers that allow macros, never for cheating in competitive play. This project is for educational and accessibility purposes.

## License

MIT — see [LICENSE](LICENSE).