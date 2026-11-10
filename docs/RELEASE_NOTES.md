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

## Known limitations

- **The PIN is access control, not encryption.** The data file is protected by your Windows account; keep your Windows sign-in secure and store backups in a safe place.
- One currency for the whole app (chosen during setup; it can't be changed once accounts exist).
- Clicking a Windows notification only dismisses it.
- Reports use the current budget limits for past months.
- The installer's own screens are in English (the app itself is in Indonesian and English).
- Very large CSV imports (over 20 MB) are refused.

## Third-party components

See `THIRD_PARTY_NOTICES.md` (installed next to the app) and Settings → About. All components are under permissive licences (MIT, Apache-2.0, ISC); PDFsharp/MigraDoc is pinned to 6.2.4, its last MIT-licensed release.
