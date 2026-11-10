# Astra Finance

A local-first, offline personal finance app for Windows (WPF, .NET 10). Developed by **Aprotoos**.

> Status: in development — Phases 1–9 (foundation, database, onboarding & accounts, transactions, dashboard, budgets & savings goals, bills & subscriptions, analytics & reports, settings & data protection) complete. See [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md).

Astra Finance keeps your data on your device. It does not connect to or control any bank account.

## Requirements

- Windows 10 (2004+) or Windows 11
- .NET 10 SDK to build

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
