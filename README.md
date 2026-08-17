# AegisMouse

Universal mouse macro engine — works with any brand of mouse. Input is synthesized at the OS level (SendInput + low-level hooks), so no vendor driver is required.

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