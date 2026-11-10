# Astra Finance

A local-first, offline personal finance app for Windows (WPF, .NET 10). Developed by **Aprotoos**.

> Status: **version 1.0.0** — all ten roadmap phases complete. See [docs/RELEASE_NOTES.md](docs/RELEASE_NOTES.md) and [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md).

Astra Finance keeps your data on your device. It does not connect to or control any bank account.

## Install

Run `AstraFinance-Setup-1.0.0.exe`. No administrator rights are needed — it installs for the current Windows user only (`%LOCALAPPDATA%\Programs\Astra Finance`), adds a Start Menu shortcut (Desktop shortcut optional) and registers a normal Windows uninstaller (Settings → Apps).

Uninstalling **keeps your data** unless you explicitly choose to delete it (two confirmations; the default answer is No). Silent uninstalls keep data unless `/DELETEUSERDATA` is passed.

The installer is not code-signed yet, so SmartScreen may ask for confirmation.

## Requirements

- Windows 10 (2004+) or Windows 11, 64-bit
- .NET 10 SDK to build; Inno Setup 6 to build the installer

## Build, test, run

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/AstraFinance.App
```

Build the release (tests → self-contained publish → installer in `artifacts\installer`):

```bash
powershell -ExecutionPolicy Bypass -File installer/build-release.ps1
```

## Where data lives

`%LOCALAPPDATA%\AstraFinance`:

- `astra.db` — SQLite database with all financial data
- `Backups\` — automatic and manual backups, plus safety copies taken before database upgrades (the folder can be changed during setup)
- `preferences.json` — UI preferences (language, theme, accent, sidebar)
- `Logs\` — diagnostic log (no financial data, 14-day retention)

**Security note:** the PIN controls access to the app; it does **not** encrypt the database. Version 1 relies on Windows user-profile permissions to protect the data folder.

## Project layout

```
src/AstraFinance.App             WPF views, view models, themes, shell
src/AstraFinance.Core            Domain, interfaces, rules, localization (.resx)
src/AstraFinance.Infrastructure  Persistence, logging, file system
src/AstraFinance.Tests           xUnit tests
assets/branding                  Icon source and generator
docs/                            Architecture notes and implementation status
```

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+K / Ctrl+F | Focus global search |
| Ctrl+B | Expand/collapse sidebar |
| Ctrl+N | Quick Add transaction |
| Ctrl+L | Lock the app |
| Esc | Close search / overlay sidebar / dialog |

## Licenses

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
