# AegisMouse

Universal mouse macro engine — works with any brand of mouse. Input is synthesized at the OS level (SendInput + low-level hooks), so no vendor driver is required.

**Current version: 1.1.0**

## Changelog

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