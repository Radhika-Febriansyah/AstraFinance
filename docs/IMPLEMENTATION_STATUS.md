# Astra Finance — Implementation Status

Last updated: 2026-11-10 (Phase 7)

| Phase | Name | Status |
|---|---|---|
| 1 | Foundation | ✅ Complete (built, tested, run-verified) |
| 2 | Database | ✅ Complete (built, tested, run-verified) |
| 3 | Onboarding and Accounts | ✅ Complete (built, tested, run-verified) |
| 4 | Transactions | ✅ Complete (built, tested, run-verified) |
| 5 | Dashboard | ✅ Complete (built, tested, run-verified) |
| 6 | Budgets and Savings Goals | ✅ Complete (built, tested, run-verified) |
| 7 | Bills and Subscriptions | ✅ Complete (built, tested, run-verified) |
| 8 | Analytics and Reports | ⏳ Next |
| 9 | Settings and Data Protection | Partially started (language, theme, accent, notifications, About) |
| 10 | QA and Release | Not started |

## Environment

- .NET SDK 10.0.403 (Windows 11, 10.0.26200)
- Solution: `AstraFinance.sln` (classic .sln format, 4 projects)
- Central package management: `Directory.Packages.props`
- Local tool: `dotnet-ef` 10.0.14 — run `dotnet tool restore` once
- Shared build settings: `Directory.Build.props` — nullable enabled, **warnings treated as errors**, .NET analyzers at `latest-recommended`

---

## Phase 1 — Foundation

### Completed tasks

**Workspace & solution**
- Inspected workspace (only `CLAUDE.md` existed) and the installed SDK.
- Created `src/AstraFinance.App` (WPF, `net10.0-windows10.0.19041.0`, assembly `AstraFinance.exe`), `src/AstraFinance.Core` (`net10.0`), `src/AstraFinance.Infrastructure` (`net10.0`), `src/AstraFinance.Tests` (xUnit).
- Dependency direction: App → Infrastructure → Core. Core has no package dependencies.
- Packages: CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.14, Microsoft.Extensions.Logging 10.0.14 (all MIT). Test: xUnit 2.9.3.
- `app.manifest`: Per-Monitor-V2 DPI awareness, Windows 10/11 compatibility, `asInvoker`.
- Application icon generated from a script (`assets/branding/generate-icon.ps1`) plus `assets/branding/astra-mark.svg`.

**Dependency injection & MVVM**
- `CompositionRoot` configures all services; the provider is built with `ValidateOnBuild` and `ValidateScopes`.
- ViewModel-first navigation: `PageRegistry` maps each `AppPage` to a factory; `NavigationService` creates a fresh page view model per navigation and disposes the previous one. `PageTemplates.xaml` maps view models to views.
- `PageCatalog` (Core) is the single source of truth for the 9 navigation pages, their order, icons and planned phase. Startup page is always Dashboard.

**Design system & theming**
- Tokens (`Themes/Tokens.xaml`): Segoe UI Variable typography scale, 4px spacing scale, restrained radii (4/6/10/12), control sizes, motion durations, text styles.
- Palettes (`Colors.Dark.xaml`, `Colors.Light.xaml`): charcoal/navy dark theme and white/soft-gray light theme, semantic colors (success/warning/danger/info), themed popup/card shadows. A unit test enforces identical keys in both.
- Accent system (`AccentPalette`, Core): Coral (brand default), Amber, Teal, Blue, Violet — each with fill/hover/pressed/subtle/text/foreground values per theme. A unit test checks **WCAG AA (≥ 4.5:1)** for accent text on background, surface and selected rows, and for text on accent fills.
- `ThemeService` swaps color/accent dictionaries at runtime (no restart). **Follow System** reads the Windows app-mode registry value and reacts to `SystemEvents.UserPreferenceChanged`.
- Native title bar is tinted per theme through DWM (`DwmTitleBar`), keeping Snap Layouts and system behaviour.
- Lucide-style 24×24 stroke icon set (`Themes/Icons.xaml`) rendered by the `AstraIcon` control; a test verifies every referenced icon key exists.
- Styled controls (no visibly default WPF chrome): Button (default, Primary, Ghost, Link, Danger, Icon with badge), ToggleButton, CheckBox, Switch, RadioButton, choice-card RadioButton, TextBox (placeholder, leading icon, validation template), PasswordBox, ComboBox + items, ContextMenu, MenuItem (icons, checks, submenus), Separator, ToolTip, ScrollBar/ScrollViewer, ListBox/ListBoxItem, sidebar nav item, ProgressBar, cards, badges, loading spinner (`Assist.IsBusy` on buttons). States: hover, pressed, keyboard focus ring, disabled, busy, validation error.

**Application shell**
- `MainWindow`: collapsible sidebar (240 ↔ 68 px, animated), brand mark, 8 primary items + Settings in the footer, keyboard-navigable list, tooltips when collapsed.
- Top bar: breadcrumb/page title (ellipsizes), global search, Quick Add, notifications bell with unread badge, application menu (Appearance, Language, Settings, About).
- Responsive layout: < 1000 px the sidebar collapses and expanding it overlays content with a scrim (Esc/click closes); < 760 px compact top bar (icon-only Quick Add, shorter breadcrumb, reduced page padding). Search box flexes with available width.
- Subtle page entrance animation (fade + 8 px rise).
- Global search: `IGlobalSearchService` fans out to `ISearchProvider`s with debounce and cancellation; categorized results; Up/Down/Enter/Esc; Ctrl+K / Ctrl+F focus. Phase 1 registers the page provider (localized + English names, diacritic-insensitive). Each later module adds its own provider.
- Notifications: `NotificationCenter` (Core, bounded session history, unread count) + toasts (auto-dismiss, max 3) + bell flyout with empty state.
- Astra-styled in-window modal dialogs (`DialogService`: confirm/alert, destructive variant, Esc to cancel, focus moves into dialog, background disabled).
- Keyboard shortcuts: Ctrl+B (sidebar), Ctrl+K/Ctrl+F (search), Ctrl+N (Quick Add — bound, disabled until Phase 4).
- Single instance: second launch signals the running instance, which restores and focuses its window, then exits.
- Global exception handling: dispatcher, AppDomain and unobserved-task exceptions are logged; UI exceptions show an Astra error dialog pointing to the log folder.
- Local diagnostic log (`%LOCALAPPDATA%\AstraFinance\Logs`, 14-day retention). Policy: no PINs or financial content in logs.

**Localization foundation**
- `.resx` resources in Core: English (neutral/fallback) and Indonesian (`id` satellite), 95 keys.
- `LocalizationService` with runtime language switching; XAML markup extension `{loc:Loc Key}` updates live; view models refresh computed text on `LanguageChanged`.
- First run follows the Windows display language (until onboarding chooses it in Phase 3).
- Tests enforce: identical key sets, matching `{n}` placeholders, every `{loc:Loc …}` key used in XAML exists, every page/category/accent has strings.

**Preferences**
- UI preferences (language, theme, accent, sidebar state) stored in `%LOCALAPPDATA%\AstraFinance\preferences.json`.
- Atomic writes (temp file + replace). Corrupt or invalid files are renamed aside (never deleted), defaults restored, and the user is notified. Save failures keep the change for the session and show an error notification.

**Pages in Phase 1**
- Dashboard: real greeting (time-of-day, localized) and current date (refreshes every minute). No financial figures are shown; a status card states the overview arrives in Phase 5.
- Settings: General → language; Appearance → theme mode + accent; About → version, developer (Aprotoos), data location + "Open data folder", privacy statement, third-party components.
- Other modules show an honest "Not available yet — Phase N" status page with no numbers and no non-functional buttons.

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors** (warnings are errors; analyzers on).

### Test status

`dotnet test` — **62 passed, 0 failed**.

Coverage: localization parity/placeholders/usage, theme resolution, palette key parity, dynamic-resource and icon references, WCAG contrast of all accents, color math, greeting periods, page catalog order, search scoring/grouping/localized page search, notification center, preferences service (apply/persist/failure), JSON preferences store (round trip, first run, corrupt/invalid files, atomic overwrite), DI container validation, navigation (startup page, selection sync, disposal, no-op re-navigation), responsive sidebar states, Quick Add gating, theme/language commands, search view model (grouping, highlight wrap, open, no-results), dialog service, notification badge, dashboard greeting/date in both languages, settings choices.

### Manual verification (run-verified on Windows 11, 125% DPI)

- Launch → Dashboard with greeting/date; native title bar tinted dark.
- Settings → Light theme applied instantly and persisted; language switched to Bahasa Indonesia via the ComboBox, whole shell re-rendered in Indonesian and persisted.
- Search "angg" → "Halaman" group, Anggaran highlighted → Enter navigated to Budgets and synced the sidebar; nonsense query shows the no-results message.
- 900 px window → sidebar auto-collapsed, compact top bar; Ctrl+B expanded the overlay sidebar with scrim; Esc closed it.
- Application menu → About opened the Astra modal dialog with focus on OK.
- Second launch exited and the original window remained (one process).
- Diagnostic log contained only the startup entry.

### Remaining tasks / not in Phase 1

- **Quick Add (Ctrl+N)** is wired but disabled with an explanatory tooltip until the transaction service exists (Phase 4).
- **DatePicker and DataGrid styles** — not yet needed; to be added in the phases that first use them (Phase 3/4) so no default control appears unstyled.
- **Regional formats** (currency, number, date format) — Phase 2/3 (money formatting depends on the money model).
- Remaining Settings sections (Categories, Notifications, Security, Data Management) — their phases.
- Windows toast notifications (Phase 7); in-app notifications exist now.
- Installer (Phase 10).

### Known issues

- Unpackaged dev builds can't be driven by the desktop computer-use tool; manual checks were done with UI Automation + window capture scripts.
- At small window heights the collapsed sidebar list scrolls (scrollbar sits in the list's right gutter). Acceptable; revisit in Phase 10 small-window QA.
- `Segoe UI Variable` falls back to `Segoe UI` on Windows 10 (intended).

### Important decisions

See `docs/ARCHITECTURE.md` for details.

1. **Native title bar tinted via DWM** instead of a custom chrome — preserves Snap Layouts, accessibility and OS behaviour while matching the theme.
2. **UI preferences in a JSON file, not the database** — theme and language must be available before the database is opened (e.g. on the PIN screen). Financial and regional settings will live in the database.
3. **Unimplemented modules show an honest status page** instead of fake data or dead buttons.
4. **In-window modal dialogs** (overlay with scrim) instead of separate windows — consistent styling, no OS dialogs.
5. **Lucide icon geometry (ISC)** embedded as XAML geometries — no icon-font dependency; acknowledged in `THIRD_PARTY_NOTICES.md`.
6. **Warnings as errors + .NET analyzers** to keep quality high from the start.

---

## Phase 2 — Database

### Completed tasks

**Packages:** Microsoft.EntityFrameworkCore.Sqlite 10.0.14 and Microsoft.EntityFrameworkCore.Design 10.0.14 (both MIT, maintained by Microsoft, same release train as the other Microsoft.Extensions packages). Design is build-time only (`PrivateAssets=all`). Local tool `dotnet-ef` 10.0.14 (`dotnet-tools.json`).

**Entities** (`Core/Entities`): Account, Category, Transaction, BalanceAdjustment, Budget, SavingsGoal, GoalAllocation, Bill, BillPayment, AppSetting. Entities for later modules exist now so the first schema is complete; their services arrive in their phases.

**Money** (`Core/Money`)
- All amounts are `long` minor units of the single base currency (IDR = whole Rupiah). No float/double anywhere in money code.
- `CurrencyInfo` (IDR default; USD, EUR, SGD, MYR, JPY defined) carries the minor-digit count, so fractional currencies work later without schema changes.
- `MoneyMath`: overflow-checked add/subtract/sum (overflow throws, never wraps); integer percentage.
- `MoneyFormatter` / `MoneyParser`: exact formatting and parsing with regional separators (`1.234.567` / `1,234,567.89` / space-grouped); handles `long.MinValue`; rejects too many decimals, signs and out-of-range values. `RegionalFormat` also holds the date format (dd/MM/yyyy, MM/dd/yyyy, yyyy-MM-dd).

**Financial rules** (`Core/Rules`, pure and unit-tested)
- `BalanceCalculator`: Opening + Income − Expense + Transfers In − Transfers Out + Adjustments — the single balance definition.
- `BudgetEvaluator`: limit/spent/remaining/usage %/status (Normal, Warning, Exceeded) with a configurable warning threshold (default 80 %), compared exactly in integers.
- `AllocationRules`: unallocated balance, allocation and release limits for savings goals.
- `DateRange` month helpers (leap-year safe).

**Database** (`Infrastructure/Database`)
- SQLite file `%LOCALAPPDATA%\AstraFinance\astra.db` (outside the install folder). Schema via EF Core migrations (`InitialCreate`).
- Connection pragmas: `foreign_keys=ON`, `journal_mode=WAL`, `synchronous=FULL`, `busy_timeout=5000`.
- Constraints enforced by the database as well as by the services: positive amounts; transfer shape (destination ≠ source, no category) vs income/expense shape (category required, no destination); non-zero adjustments with a non-empty reason; positive budget/goal/bill amounts; budget threshold range; unique bill payment per (bill, due date); unique built-in category keys. All foreign keys are `RESTRICT`, so financial history can't be deleted implicitly.
- Soft-deleted transactions and adjustments are hidden by global query filters, so balances, budget totals and reports exclude them automatically.
- `DatabaseInitializer` at startup: refuses a database written by a newer app version (without modifying it); takes a **verified safety copy** (SQLite online backup API + integrity check) before migrating an existing database; migrates; runs `quick_check`; seeds the 15 built-in categories idempotently.
- `SqliteBackup`: consistent online copy including WAL contents, converted to a standalone file and verified. Foundation for Phase 9 backup/restore.

**Services** (`Infrastructure/Services`, Core interfaces)
- `AccountService`: create (opening balance may be zero or negative), edit (never the opening balance), activate/deactivate (history kept), balances with components, total balance, **documented balance adjustments** (mandatory reason, stored as a delta with the balance before; the opening balance is never overwritten).
- `CategoryService`: built-in categories localized by key, custom categories (case-insensitive unique per kind), restyle/rename, deactivate.
- `TransactionService` — the only write path for income/expense/transfer:
  - validation that reports all errors at once (amount, category kind, account/category existence and active state, transfer rules, field lengths);
  - **atomic** writes in one SQLite transaction;
  - **a transfer is one row** (never double counted); an optional **transfer fee** is a separate linked Expense from the source account (default "Fees & Charges" category);
  - **duplicate submission protection**: the caller-generated draft id makes repeated submits idempotent;
  - edit (balances recalculate; the fee is added/updated/removed with its transfer; fees can't be edited or deleted directly);
  - soft delete and restore (a transfer's fee follows it; a fee removed earlier by an edit is not brought back);
  - query with search (LIKE-escaped), date range, category, account (either side of a transfer), type, sorting, paging, and a deleted-items view;
  - income/expense totals and per-category expense totals (budgets count Expense only).
- `SettingsStore`: database key/value settings and the regional format (all three values written atomically).

**App integration**
- Services registered in `CompositionRoot`; the database opens before the main window. Failures show a localized message and the app exits without touching data (newer-version, integrity, generic). A notification confirms when an upgrade ran after a safety copy.
- EF Core logging limited to warnings: no SQL in the diagnostic log.
- 46 new localized strings (validation messages, category names, database errors) in English and Indonesian; 14 new Lucide-style category icons.

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**. EF-generated migration files are marked as generated code (`Database/Migrations/.editorconfig`) so analyzers skip them.

### Test status

`dotnet test` — **141 passed, 0 failed** (62 from Phase 1 + 79 new).

New coverage — integration tests run against a real, migrated SQLite file per test:
- Opening balances (zero, positive, negative); income; expenses; non-positive amounts; category-kind mismatch; missing references (all errors reported); duplicate submission saved once.
- Transfers (balances move, total unchanged, not income/expense, not in budgets); transfer fees (linked expense, Fees category, charged to source); invalid transfers; fee only on transfers; a rejected transfer writes nothing.
- Balance adjustments (delta, balance-before, opening preserved, not income/expense, reason required, unchanged rejected).
- Editing (amount/type change, moving a transfer between accounts with a fee change, fee removal, fee not directly editable).
- Delete/restore (balances, totals, budgets, deleted-items query; transfer and fee together; stale fee not brought back).
- Inactive accounts/categories (history and balance kept, no new activity, existing records still correctable).
- Budget totals (Expense only, period only, deleted excluded) with the evaluator.
- Date-only storage and UTC timestamps; querying/search/paging/sorting.
- Database: first-run creation and seeding, idempotent init, verified pre-migration safety copy of an existing database, refusal of a newer database, CHECK and foreign-key constraints proven with raw SQL, accounts with history can't be hard-deleted, online backup includes WAL data, regional settings round trip.
- Pure tests: money formatting/parsing/round-trips/overflow, balance formula, budget statuses and thresholds, allocation limits, month ranges, date formats.
- Guards: every validation code and built-in category has strings in both languages; every category icon exists.

### Manual verification

- First launch created `astra.db` (WAL), applied `InitialCreate`, seeded categories and started normally.
- App force-killed (simulated crash) → relaunch reopened the same database, integrity check passed, no re-migration, no duplicate seeding.
- Graceful exit checkpointed the WAL into `astra.db`.
- Diagnostic log after the change: only startup and database-open lines, no SQL.

### Bugs found and fixed during this phase

- **Upgrade crash:** EF Core checks whether the database exists using a read-only connection; the pragma interceptor tried to change the journal mode on it and failed. Every user upgrading an existing database would have been unable to start the app. The journal mode is now only set on writable connections (covered by the pre-migration test).
- **Backup copies inherited WAL mode** and couldn't be opened read-only; backups are now converted to standalone files before verification.

### Remaining tasks / not in Phase 2

- UI for accounts, transactions, budgets, goals and bills (their phases). Budget, savings-goal and bill services (CRUD, allocation persistence, payment confirmation) — Phases 6–7; the rules and schema are in place.
- Search providers for accounts/transactions/categories — added with their pages so results can open real detail views.
- Backup/restore UI, scheduling and retention — Phase 9 (`SqliteBackup` foundation exists).
- Explicit tightening of Windows ACLs on the data folder — Phase 9 (the folder is already inside the per-user profile).

### Known issues

- Changing the base currency after data exists is not yet guarded (no UI exposes it yet; Phase 3 onboarding / Phase 9 settings must block or warn).
- Inactive accounts' balances are included in the total balance by design (money never disappears from the total). Revisit if users expect otherwise.
- The Microsoft Store build of Python on this machine virtualizes `%LOCALAPPDATA%`, so it can't inspect the real database; verification was done through the app, its log and the tests.

### Important decisions

1. **A transfer is one row** with source and destination; a fee is a separate linked Expense. Double counting is structurally impossible and fees stay visible in budgets and reports.
2. **Balance adjustments have their own table** (signed delta + reason + balance before), not a fourth transaction type, so they never appear as income or expense.
3. **Savings allocations are earmarks** (`GoalAllocation`, signed), never transactions, so balances are unaffected by design.
4. **Bill payment uniqueness is enforced by the database** (unique bill + due date).
5. **Client-generated transaction ids** make saves idempotent (double-click / double Enter safe).
6. **The opening balance is immutable after creation**; corrections go through documented adjustments.
7. **Newly referenced accounts/categories must be active; existing references may be inactive**, so history stays editable after deactivation.
8. **Total balance includes inactive accounts.**
9. **WAL + synchronous=FULL** for durability; backups always use the SQLite backup API.
10. **The app refuses a database from a newer version** and takes a verified safety copy before every migration.

---

## Phase 3 — Onboarding and Accounts

### Completed tasks

**First-launch wizard** (6 steps, `OnboardingViewModel` + `OnboardingView`)
1. Welcome — language choice (Bahasa Indonesia / English), switches the whole UI live; privacy statement.
2. Regional — primary currency (default IDR), number format, date format, live preview. Currency is locked once an account exists (prevents stored amounts changing meaning).
3. First account — mandatory; name, type, opening balance (zero or negative allowed), icon, color. Reuses the same form as the Accounts page.
4. Security PIN — 4–8 digits, entered twice; explains that the PIN is access control, not encryption, and can't be recovered.
5. Backups — folder (native folder picker), automatic schedule (Off/Daily/Weekly/Monthly), explanation of manual backup.
6. Completion — summary, then the Dashboard.
- Progress is persisted after every step (`OnboardingService`, database settings). An interrupted setup resumes at the saved step; returning to the account step edits the existing account instead of creating a duplicate; a resume past the account step without an account falls back to the account step.
- The shell is fully gated: sidebar, top bar, navigation and shortcuts are unavailable until setup completes, so the Dashboard can't be reached early.

**PIN security** (`PinHasher`, `PinService`)
- PBKDF2-HMAC-SHA256, 600,000 iterations, 16-byte random salt, constant-time comparison; format `pbkdf2-sha256$iterations$salt$hash` so the work factor can be raised later. Built into .NET — no third-party crypto.
- The PIN is never stored, logged or kept beyond the input field; hashing runs off the UI thread.
- Failed-attempt cooldown: 5 free attempts, then 30 s doubling up to 15 min. The counter and cooldown end are persisted, so restarting the app doesn't reset them; the correct PIN is refused during a cooldown.
- Lock screen at every launch after setup and on demand (application menu → "Lock Astra Finance", Ctrl+L). Live countdown during cooldown. States plainly that the PIN doesn't encrypt data.
- `ChangePinAsync` (verifies the current PIN) exists for the Settings → Security UI in Phase 9.

**Backups** (`BackupService`)
- Manual backup: application menu → "Back up now" (toast with the file path).
- Automatic backup: runs after unlock when due per the schedule; keeps the newest N automatic backups (default 10); manual backups are never deleted automatically.
- Every backup uses the SQLite online backup API and is integrity-checked; folder writability is checked when saving settings.

**Accounts page** (`AccountsViewModel`, `AccountsPage`)
- Account cards with type, balance (negative in red), inactive badge; total balance (all accounts, incl. inactive); "Show inactive accounts" switch; empty state.
- Create/edit dialog (name, type, opening balance on create only, icon and color pickers with type-based icon suggestion).
- Details: balance breakdown (opening, income, expenses, transfers in/out, adjustments, current), balance-adjustment history, transaction history (paged, signed from the account's perspective, transfer direction labelled).
- Balance adjustment dialog: enter the actual balance, live preview of the recorded difference, mandatory reason, date (new Astra DatePicker).
- Deactivate (destructive confirmation explaining history is kept) / reactivate.
- Global search finds accounts (with balance) and opens their details.

**Design-system additions**
- DatePicker + Calendar fully styled (day/month/year views, today ring, selection, keyboard focus); shows dates in the user's configured format.
- Form dialogs (`IFormDialog`): any form view model renders inside the Astra dialog with validation, busy state, Enter to submit and Esc to cancel.
- `ValidatableViewModel` (INotifyDataErrorInfo) drives the existing validation template.
- PIN box behaviour (digits only, paste filtered, bindable), icon and color pickers, field labels; 18 new icons.

**Localization**: 117 new strings in English and Indonesian (258 total), including all validation messages.

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **196 passed, 0 failed** (141 previous + 55 new).

New coverage: PIN hashing (salt, verification, malformed hashes, work factor, format rules), cooldown policy, PIN service (hash-only storage, cooldown persisted across restarts and blocking the correct PIN, expiry, reset on success, change PIN), onboarding persistence/resume/completion requirements, currency lock, signed amount parsing, backup defaults, unwritable folder rejection, verified manual backup, schedule due rules, retention (keeps manual backups), account search; view models: full wizard run in Indonesian, resume after restart without duplicate account, account editor validation and negative opening balance, icon suggestion, Accounts list/total/details/search navigation, adjustment dialog validation and recording, deactivate confirm/cancel, Back up now, gating (no Dashboard before setup, PIN required at startup, manual lock).

### Manual verification (Windows 11, 125 % DPI, real app)

- Fresh start → onboarding; regional preview; account with Rp1.500.000.
- **App force-killed after the account step → relaunch resumed at the PIN step with exactly one account.**
- Empty PIN shows the validation message; PIN set; backup step; summary; Dashboard.
- First automatic backup created on reaching the Dashboard (weekly schedule, none before).
- Accounts page, details, adjustment dialog with live difference, calendar popup; adjustment recorded (−Rp25.000, opening balance unchanged).
- Relaunch → lock screen; wrong PIN shows remaining attempts; correct PIN opens the Dashboard.
- Back up now → manual backup file + success toast.
- Diagnostic log contains no PIN and no errors.
- Test data was moved to `%LOCALAPPDATA%\AstraFinance.phase3-test-data` afterwards so the next launch starts a clean setup.

### Bugs found and fixed during this phase

- **Esc in an open calendar also cancelled the whole dialog** (and Enter would have submitted it). Dialog keys now ignore keys meant for an open dropdown/calendar.
- **Text inputs were indented twice**: WPF applies `TextBox.Padding` internally and the template added it again.
- **Toast and notification icons never rendered** (Phase 1 bug): their style replaced the icon control's template instead of extending it.
- **Language preference and UI language could disagree** outside the app's startup code; `PreferencesService` now keeps them in sync everywhere.
- Breadcrumb truncated at ~1000 px; sign characters were inconsistent (`−Rp0` vs `-Rp25.000`).

### Remaining tasks / not in Phase 3

- Transfer between accounts from the Accounts page → Phase 4 (with the transaction forms; the transfer service already exists).
- Settings → Security (change PIN UI, cooldown configuration) and Data Management (backup history, restore) → Phase 9.
- DataGrid styling → Phase 4 (first table).

### Known issues / open questions

- **Forgotten PIN — decided (2026-11-09): no reset.** A forgotten PIN can't be recovered or reset in the app. This is stated during PIN setup. (Data files remain in the user's profile; the PIN is access control, not encryption.)
- Account icon colours are shown on a tinted circle in both themes; very light custom colours could be low-contrast (only curated colours are offered).
- Popups (calendar, menus) couldn't be screenshotted with PrintWindow; they were checked with screen captures.

### Important decisions

1. **Onboarding and the lock screen are in-window gates** (not separate windows), so single-instance activation, title-bar theming and accessibility work the same everywhere.
2. **PIN hash in the database** (`Settings` table), PBKDF2-SHA256 600k; cooldown state persisted.
3. **If a PIN exists, it's always required first** — even when an interrupted onboarding is resumed.
4. **Opening balance is fixed after creation, also in the wizard**; corrections use documented adjustments.
5. **Currency locks after the first account.**
6. **The DatePicker has no free-text entry** — avoids day/month ambiguity; dates always display in the configured format.
7. **Scheduled backups run after unlock** (at most once per due period); retention only affects automatic backups.

---

## Phase 4 — Transactions

### Completed tasks

**Transactions page** (`TransactionsViewModel`, `TransactionsPage`)
- Styled, virtualized DataGrid (first table in the app): Date, Description, Category, Account, Amount, Type. Amounts are signed and coloured (income green, expense red/neutral, transfers neutral); date and amount columns size to their content so money is never truncated.
- Filters: search, period (all time, this month, last month, last 3 months, this year, custom range with DatePickers), type chips, category, account, "Show deleted". Clear filters.
- Search matches description, notes, **localized category names**, account names and exact amounts (e.g. "120.000").
- Sorting by Date and Amount is done in the database across all pages (not just the visible page).
- Paging (100 per page) with a summary of all matches: count, income, expenses (transfers are counted but never added to income or expense).
- Details panel (overlays the table on narrower windows): amount, type, date, category, account, transfer fee, notes, recorded time; Edit, Delete (destructive confirmation), "Open the transfer" for fee rows; Restore in the deleted view.
- Empty states: no transactions yet (with Add action) and no matches.

**Quick Add and full form** (`TransactionFormViewModel`, `TransactionForm`)
- Quick Add (Ctrl+N or the top-bar button, from any page): type, amount, category, account, date; "More details" expands to the full form in place.
- Full form adds description, notes and transfer fee; transfers choose from/to accounts. Editing a fee row opens its transfer.
- Both use the same view model, validation and `TransactionService`. The draft id is fixed when the form opens, so double submits save once. Service errors are shown on the matching fields.
- Inactive accounts/categories are hidden from new entries but still shown when editing records that use them.

**Export**: "Export CSV" writes **every** transaction matching the current filters (read in 500-row batches, never just the visible page) through the native Save dialog. RFC 4180 quoting, UTF-8 with BOM (Excel), configured date format, plain signed amounts (`-45000`, `8000000`), currency column, fee rows marked, and spreadsheet-formula injection protection for text cells.

**Search & navigation**: global search providers for transactions (description, notes, amount → opens details) and categories (→ Transactions filtered by that category). Accounts page: Transfer action, clickable history rows (→ transaction details), live refresh after changes anywhere (`DataChangeNotifier`).

**Service additions**: `SummarizeAsync`, `GetFeeAsync`, category/account/amount search inputs (single shared filter implementation for list, summary and export); `MoneyFormatter.FormatPlain`; `CsvWriter`.

**Design system**: DataGrid, column headers (sort indicator), rows (hover, selection, keyboard focus ring), cells; `ChoiceGroup` control; clickable list rows; validation messages now wrap within the field width.

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **223 passed, 0 failed** (196 previous + 27 new).

New coverage: summary rules (transfers excluded, fees included, account filter), search by category/account/amount, fee lookup, plain amount formatting (incl. `long.MinValue`), CSV escaping and formula-injection protection, BOM/CRLF/atomic write; Quick Add flow (validation, save, idempotent double submit, disabled while gated), full form transfer with fee and fee removal on edit, service errors mapped to fields, Transactions page filters/search/sort/summary, paging (105 rows), delete with confirmation and restore, export of 523 rows across batches with filters respected, search results opening details or category filter, Account transfer action with live refresh.

### Manual verification (Windows 11, 125 % DPI, real app)

- Setup → Ctrl+N immediately after unlock → Quick Add expense (Rp45.000, Food & Drinks) → saved; Ctrl+N works again after the dialog closes.
- Full form income (Rp8.000.000 Salary); new account BCA; transfer Cash → BCA Rp1.000.000 with Rp6.500 fee → table shows the transfer (neutral) and the linked fee expense; summary "4 transactions · Income Rp8.000.000 · Expenses Rp51.500".
- Details panel for the transfer; overlay layout at ~1000 px.
- Export CSV through the native Save dialog → 4 rows, correct signs, fee marked.
- Log contains no PIN, no errors. Test data moved to `%LOCALAPPDATA%\AstraFinance.phase4-test-data` so the next launch starts a clean setup.

### Bugs found and fixed during this phase

- **Keyboard shortcuts (Ctrl+N, Ctrl+K…) did nothing after unlocking, finishing setup or pressing Esc in search** — nothing had keyboard focus. Focus is now restored after dialogs and gates.
- **Tall dialogs pushed Save/Cancel off-screen** on smaller windows; dialogs now keep title and buttons fixed and scroll the form.
- **Radio-button groups were invisible to screen readers** (only data items were exposed) and the dialog form region was announced by its C# class name. New `ChoiceGroup` control used for all radio groups; form regions are named after the dialog title.
- **Validation messages were cut off** in narrow fields; they now wrap.
- **Amounts and dates truncated** in the table at ~1000 px; columns size to content. Breadcrumb prefix hidden below 1200 px.
- Transfers loaded expense categories internally (hidden but wrong state).

### Remaining tasks / not in Phase 4

- Remembering the last-used account/type in Quick Add (currently defaults to Expense and the first active account).
- Excel/PDF export of transactions → Phase 8 (reports).
- On short windows the filter area takes several rows; a collapsible filter bar may be worth adding in Phase 10 small-window QA.

### Important decisions

1. **One form view model for Quick Add, the full form and editing** — a single validation and save path, as required.
2. **Fee rows are read-only views of their transfer**: editing/deleting a fee goes through the transfer.
3. **Sorting is server-side and limited to Date and Amount** (the indexed, meaningful orders); other columns aren't sortable rather than sorting only the visible page.
4. **CSV amounts are machine-readable** (no grouping, '.' decimal, signed) with a separate currency column; dates use the configured format.
5. **Category search results open Transactions filtered by that category** until Settings → Categories exists (Phase 9).

---

## Phase 5 — Dashboard

### Completed tasks

- **Charting library:** LiveCharts2 (`LiveChartsCore.SkiaSharpView.WPF` 2.0.5, stable, June 2026). Licence MIT; dependencies SkiaSharp/HarfBuzzSharp (MIT); no published advisories. Added to the App project only and listed in `THIRD_PARTY_NOTICES.md` and Settings → About. Charts use Segoe UI.
- **Dashboard** (`DashboardViewModel`, `DashboardPage`), all figures from the database:
  - Greeting and current date (refresh every minute; data reloads when the day changes).
  - Total balance (all accounts, red when negative) with the number of active accounts — click opens Accounts.
  - Income and expenses this month, plus net for the month.
  - Cash-flow chart: income vs. expenses per month for the last six months (current month last), theme-coloured, compact axis labels ("Rp8jt" / "Rp8M"), full amounts in tooltips. Click opens Analytics. When the period has no income or expenses, an empty-state message is shown instead of an empty chart.
  - Recent transactions (5 newest) — click opens the transaction's details; "View all" opens Transactions; empty state with "Add your first transaction" (Quick Add).
  - Budget summary card: honest note that budgets arrive in Phase 6 (no figures shown).
  - Live refresh after changes anywhere (Quick Add, edits, deletes); chart repaints on theme change.
  - Responsive layout: three summary cards side by side, chart and recent list side by side on wide windows; stacked below ~900 px; single-column cards below ~640 px.
- **Services:** `ITransactionService.GetMonthlyTotalsAsync` (consecutive months, zero months included, Income/Expense only, deleted excluded, exact integer sums). `MoneyFormatter.FormatCompact` for chart axes (display only; localized suffixes rb/jt/M/T and K/M/B/T).

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **237 passed, 0 failed** (223 previous + 14 new).

New coverage: monthly totals (window, zero months, transfers excluded, fees included, deleted excluded, year boundary), compact labels (IDR and USD, negative, rounding), dashboard figures (total balance, month income/expense/net, six-month series, recent ordering), empty dashboard (no fake data; opening balance is real), Quick Add from the dashboard with live refresh, card navigation (Accounts, Analytics, transaction details).

### Manual verification (real app, 125 % DPI)

- Dashboard with real data in dark and light themes (chart recoloured live), compact layout at ~880 px.
- Total balance card → Accounts.
- Test data was only borrowed for the run; the real data folder is back to a clean setup state (test data and the automatic backup it produced are in `%LOCALAPPDATA%\AstraFinance.phase4-test-data`).

### Remaining / notes

- Budget summary becomes real in Phase 6.
- Analytics (Phase 8) is still a status page; the cash-flow card already navigates there.
- Removed a duplicate Quick Add button from the dashboard header (Quick Add lives in the top bar per the specification).

### Important decisions

1. **Chart series are built in the view** from plain view-model data, so dashboard logic stays testable without the charting library.
2. **Monthly aggregation happens in memory** over a projected (date, type, amount) list — exact `long` sums and no reliance on SQL date functions.
3. **Compact labels are for axes only**; every amount a user reads as a value (cards, tooltips, lists) is the exact formatted amount.

---

## Phase 6 — Budgets and Savings Goals

### Completed tasks

- **Budget service** (`IBudgetService`, `BudgetService`): create, edit, delete, list, monthly overview. One monthly budget per expense category (enforced by the service and a unique index — migration `BudgetCategoryUnique`). Validation: positive amount, expense categories only, warning threshold 1–100 %. Start date is normalised to the first of the month.
- **Budget evaluation:** spent = sum of non-deleted **Expense** transactions in the category for the month (transfer fees count because they are expenses; income and transfers never do). Status Normal / Warning / Exceeded using the budget's own threshold or the configurable default (setting `Budgets.WarningThresholdPercent`, default 80 %).
- **Budgets page:** month navigation (previous / next / back to this month), total spent of total budget with progress, per-budget card with limit, spent, remaining or over, percentage and status badge; add, edit, delete (confirmed) and "Show expenses" (opens Transactions filtered by category). Empty state with a create action. Live refresh when transactions change.
- **Savings goal service** (`ISavingsGoalService`, `SavingsGoalService`): create, edit, allocate, release, history, complete / reopen, archive, per-account allocation info.
- **Allocation integrity:** allocations are signed history entries referencing the source account; they **never change account balances**. Allocate and release are validated inside a database transaction: amount ≤ account's unallocated balance (balance − allocations to all goals), release ≤ what the goal holds from that account, no allocation from inactive accounts or to archived goals. Archiving a goal records release entries for everything it still holds, so the history stays complete.
- **Savings Goals page:** goal cards (allocated / target, progress, remaining), details panel with allocated-by-account, contribution history, monthly pace suggestion from the target date, Allocate / Release / Edit / Mark completed / Reopen / Archive (confirmed), show-archived toggle, explainer that allocation doesn't move money, and a warning when spending pushes an account's balance below what it has allocated.
- **Dashboard:** budget summary card now shows real budgets for the month (top 4 by urgency, "+N more", empty state, "View all").
- **Global search:** budgets and savings goals are searchable; results open the relevant page/goal.
- Indonesian and English strings for everything above (~77 keys).

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **253 passed, 0 failed** (237 previous + 16 new).

New coverage: budget create/validate/duplicate/threshold, expense-only spending (income, transfers and deleted excluded; fees included), status thresholds, month boundaries, delete; goal allocation never changing balances, allocation limit across goals, release limit, inactive account and archived goal rejection, archive auto-release, over-allocation detection after spending; Budgets and Savings Goals pages, dashboard summary and search.

### Manual verification (real app, 125 % DPI)

- Upgrade path: the existing Phase 4 test database was migrated; a verified pre-migration safety copy was written to `Backups` first.
- Created budgets (Food & Drinks 90 % "Nearly used", Fees & Charges 6 % "On track") and a Laptop Fund goal with a Rp4.000.000 allocation (50 %); total balance unchanged on the dashboard; dashboard budget card shows both budgets.
- Test data was only borrowed; the real data folder is back to a clean setup state (moved to `%LOCALAPPDATA%\AstraFinance.phase4-test-data`).

### Bugs found and fixed during this phase

- Goal explainer banner was clipped at narrower widths (text in a horizontal StackPanel) — now a wrapping grid.
- Contribution history showed a dangling "·" when an entry had no note.

### Remaining / notes

- Budget-warning notifications are deferred to Phase 7 (notifications) / Phase 9 (notification settings); the default threshold is already stored as a setting.
- Budgets are monthly only (the specification's only required period).

### Important decisions

1. **One budget per category**, applying from its start month onward; editing changes it for all months (no per-month history of limits in v1).
2. **Deleting a budget is a hard delete** — budgets are plans, not financial records; transactions are untouched.
3. **Allocations are an append-only signed ledger** (positive = allocate, negative = release); a goal's allocated amount is the sum, so history and totals can never disagree.
4. **Over-allocation is warned, not blocked**, when it comes from spending — expenses must never be refused because money was earmarked.

---

## Phase 7 — Bills and Subscriptions

### Completed tasks

- **Recurrence rules** (`BillSchedule`, Core): daily, weekly, monthly, yearly. Occurrence *n* is always computed from the first due date, so a bill due on the 31st falls on the last day of shorter months and returns to the 31st; 29 February falls on 28 February in common years. Counting is capped (999) so a long-neglected daily bill can't cause a long loop.
- **Bill service** (`IBillService`, `BillService`): create, edit, pause/resume, delete (only without history), list and details with status, pay, skip, skip missed, undo skip, history.
  - **Next due** = the first occurrence after the latest *handled* occurrence (paid or skipped); the first due date when nothing is handled yet.
  - **Status**: Overdue, Due today, Due soon (within the reminder window, default 3 days), Upcoming, Paused. Overdue bills report how many payments were missed.
  - **Reaching a due date never changes a balance.** Only **Pay** (a confirmed payment) creates an Expense — through the same validation as every other transaction (`TransactionService.AddValidatedAsync`) and in **one database transaction** with the payment record. The amount, account and date can differ from the bill's usual values.
  - **Duplicate prevention**: unique (bill, due date) index; only the next open occurrence can be paid; the payment dialog's request id makes a double-submitted confirmation return the first result; a concurrent second confirmation is refused.
  - **Skip** marks an occurrence handled without an expense (for payments already entered by hand). **Skip missed** jumps an overdue bill to its next future occurrence. The latest skip can be undone.
  - **Deleting the payment's expense** (Transactions page) makes that occurrence unpaid again; paying it again reuses the record.
  - Migration `BillPaymentSkips`: `BillPayments.TransactionId` is now nullable (null = skipped).
- **Bills & Subscriptions page**: summary (expected payments in the next 30 days at usual amounts, overdue count), bill rows (status badge, relative due text, recurrence and account, amount, Pay button — prominent when due soon/today/overdue), details panel (Confirm payment, Skip, Skip missed, Edit, Pause/Resume, Delete when no history, payment history with undo), show-paused toggle, empty state. Forms: bill form (name, usual amount, recurrence, first due date, category — defaults to Bills & Utilities, payment account, notes) and payment confirmation (amount, account, date, note).
- **Reminders** (`IReminderService`, `ReminderService` + `ReminderScheduler` in the app):
  - Bill reminders once per occurrence and stage (due soon, due today, overdue); budget warnings (deferred from Phase 6) once per budget, month and level (warning, then exceeded). What was shown is remembered in the settings table, so restarts don't repeat reminders.
  - Checks run right after unlock, every 30 minutes, after transactions change and after bill changes. **Checks stop while the app is locked.**
  - Every reminder goes to the in-app notification list. When enabled, it is also shown as a **Windows notification** when Astra isn't in the foreground — **without amounts**, because Windows notifications can appear on the lock screen and in the notification centre.
- **Settings → Notifications**: master switch, bill reminders and how many days before (on the due date, 1, 2, 3, 5, 7 or 14 days), budget warnings and the default warning threshold (1–100 %), Windows notifications on/off. Stored in the database (backed up with the data).
- **Global search**: bills (including paused) by name; results open the bill's details.
- Indonesian and English strings for everything above (106 keys).

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **283 passed, 0 failed** (253 previous + 30 new).

New coverage: recurrence arithmetic (month ends, 29 February, next/latest occurrence, capped counting); bill status by due date; due dates never changing balances; payment creating exactly one expense, idempotent retries, refusing a second payment of the same occurrence, paying only the next open occurrence (early payment allowed); normal transaction validation on payments with nothing written on failure; deleting the payment expense reopening the occurrence and re-payment reusing the record; skip / undo skip / skip missed (no expenses); pause, delete rules and validation; bill payments counting toward budgets; reminders once per stage, settings respected, budget warnings once per level and month; Bills page add/pay/skip/undo, overdue summary, pause/delete; scheduler stops while locked, Windows notifications without amounts and not while in the foreground; settings persistence; search.

### Manual verification (real app, 125 % DPI)

- Upgrade path: the Phase 4 test database was migrated (2 migrations) after a verified safety copy.
- Added Electricity and Internet (monthly, due today), paid Electricity with a different amount (Rp362.500): one expense recorded, next due moved to 10/12/2026, history shows the payment; "Internet is due today" appeared in the notification list (with amount).
- Settings → Notifications shown and working; turning bill reminders off and back on with the window minimised delivered a Windows notification (Windows recorded it for `Aprotoos.AstraFinance`; the banner itself wasn't captured on screen, possibly because Do Not Disturb was on).
- Test data was only borrowed; the real data folder is back to a clean setup state.

### Bugs found and fixed during this phase

- The "expected in the next 30 days" total counted a monthly bill twice (today and 30 days later); the window is now exactly 30 days.
- "payment(s)" wording replaced by proper singular/plural strings.
- Bill changes didn't trigger a reminder check until a transaction changed; they now do.

### Remaining / notes

- Clicking a Windows notification only dismisses it in version 1 (no activation handler); the in-app list has every reminder.
- The installer (Phase 10) should give its Start-menu shortcut the same AppUserModelID (`Aprotoos.AstraFinance`) and remove the per-user registration on uninstall.
- Backup reminders are part of Phase 9 (backup settings).
- If a deleted payment expense is restored after the occurrence was paid again, both expenses exist (restoring is an explicit user action, and both appear in the history of transactions).

### Important decisions

1. **Handled occurrences, not a status flag**: a bill's state is derived from its payment records; there is no stored "paid" flag that could drift from the transactions.
2. **Skip without an expense** exists so users who already recorded a payment manually never have to create a duplicate expense.
3. **Bills with history can only be paused**, never deleted, so every payment stays explainable.
4. **Windows notifications carry no amounts**; amounts only appear inside the unlocked app. Reminders pause while the app is locked.
5. **Windows notification identity**: the unpackaged app registers its notification display name and icon for the current user only (`HKCU\Software\Classes\AppUserModelId\Aprotoos.AstraFinance`, icon copied to the data folder as `notification-icon.ico`) the first time it shows a Windows notification. No system-wide settings are changed.
6. **Notification preferences live in the database** (not `preferences.json`), because they are about financial reminders and should travel with backups.

---

## Phase 8 — Analytics and Reports (next)

Planned: analytics charts (income vs expenses, cash flow, expense breakdown, monthly trends, income sources, spending habits) with weekly/monthly/yearly/custom filters and empty-data handling; Income, Expense, Cash Flow, Account Balance, Budget and Savings Goals reports exported to PDF (library chosen after a licence check), Excel (ClosedXML) and CSV, respecting filters and formatting.
