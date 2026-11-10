# Astra Finance 1.0.0 — Release Notes

Release date: 2026-11-11 · Developer: Aprotoos · Windows 10 (2004) or later, 64-bit

Astra Finance is a personal finance manager for Windows. It works completely offline and keeps your data on your own computer.

## What's in 1.0.0

- **Accounts** — cash, bank, e-wallet, savings and custom accounts; opening balances; documented balance adjustments; deactivate without losing history.
- **Transactions** — income, expenses and transfers (with optional transfer fees); Quick Add (Ctrl+N) and full form; search, filters, sorting, paging; delete and restore; CSV export.
- **Dashboard** — total balance, this month's income and expenses, six-month cash flow, recent transactions, budget summary.
- **Budgets** — monthly limits per expense category with warning thresholds; only expenses count.
- **Savings goals** — allocate money that is already in your accounts (never changes balances); contribution history; complete and archive.
- **Bills & subscriptions** — daily/weekly/monthly/yearly; confirm payments (one expense each, no duplicates), skip, pause; reminders.
- **Analytics** — income vs expenses, cash flow, expense breakdown, income sources, monthly trends, spending habits; weekly/monthly/yearly/custom periods.
- **Reports** — income, expense, cash flow, account balance, budget and savings-goal reports as PDF, Excel or CSV.
- **Settings** — language (Indonesian/English), number and date formats, light/dark/follow-system theme and accent colour, categories, notifications (in-app and Windows), PIN and wrong-PIN cooldown, backups (automatic and manual), safe restore, full JSON export, CSV import of transactions.
- **Security & privacy** — PIN lock (PBKDF2 hash, never stored in plain text), no account, no cloud, no network access.

## Installing

Run `AstraFinance-Setup-1.0.0.exe`. No administrator rights are needed: Astra Finance installs for your Windows user only (`%LOCALAPPDATA%\Programs\Astra Finance`). A Start Menu shortcut is created; a Desktop shortcut is optional.

The installer is not code-signed yet, so Windows SmartScreen may ask for confirmation ("More info" → "Run anyway").

## Your data

All data is stored in `%LOCALAPPDATA%\AstraFinance` (database, settings, backups, logs) — never in the installation folder.

- **Uninstalling keeps your data** by default. The uninstaller asks whether you also want to delete it; the default answer is **No**, and deleting requires a second confirmation. Backups saved in other folders you chose are never deleted.
- Reinstalling (or installing a newer version) picks up the existing data. Before a newer version changes the database, it saves a safety copy in the Backups folder.
- For silent/IT uninstalls: data is kept unless `/DELETEUSERDATA` is passed.

## Release verification (2026-11-11)

Verified on Windows 11 (125 % scaling) with the installer built from `master` at commit `a735ed6`:

| Check | Result |
|---|---|
| Release build (`installer/build-release.ps1`) | 321/321 tests passed (Release); self-contained publish; installer built — **AstraFinance-Setup-1.0.0.exe**, 56.8 MB, SHA-256 `20F193E6F5A33B4A9FBA4B66B1B573152AC1FC97F6B8D4B0D4001C1C813E978F` (the checksum is specific to this build) |
| Dependency audit | No vulnerable packages |
| Clean installation | Per-user, no administrator rights; program in `%LOCALAPPDATA%\Programs\Astra Finance`; Apps entry "Astra Finance 1.0.0 — Aprotoos" |
| Start Menu shortcut | Present, starts the installed app (window in ≈2.6 s), carries the notification ID `Aprotoos.AstraFinance` |
| Desktop shortcut (optional) | Present when chosen; starts the app; a second launch focuses the open window (one process) |
| First run | Setup wizard on an empty data folder → Dashboard with the opening balance |
| Main features (installed app) | Accounts, Quick Add, budget, savings goal allocation, bill payment, Analytics, PDF report export, manual + automatic backup, global search, PIN lock on restart — all figures consistent (total balance Rp4.930.000, month expenses Rp320.000) |
| Uninstall, default answers | Program, shortcuts and Apps entry removed; database, settings, backups and logs **unchanged byte for byte** |
| Reinstall after uninstall | Kept data opens with every record intact; integrity check passed |
| Silent uninstall | Data kept |
| Logs | No errors or warnings |

## Known limitations

- **The PIN is access control, not encryption.** The data file is protected by your Windows account; keep your Windows sign-in secure and store backups in a safe place.
- One currency for the whole app (chosen during setup; it can't be changed once accounts exist).
- Clicking a Windows notification only dismisses it.
- Reports use the current budget limits for past months.
- The installer's own screens are in English (the app itself is in Indonesian and English).
- Very large CSV imports (over 20 MB) are refused.
- Transactions entered with Quick Add have no description; reports show an empty description for them (the app's lists show the category name instead).
- Category shares in reports are rounded down, so they can add up to slightly less than 100 %.

## Third-party components

See `THIRD_PARTY_NOTICES.md` (installed next to the app) and Settings → About. All components are under permissive licences (MIT, Apache-2.0, ISC); PDFsharp/MigraDoc is pinned to 6.2.4, its last MIT-licensed release.
