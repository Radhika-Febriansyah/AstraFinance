# Astra Finance — Implementation Status

Last updated: 2026-11-11 (Phase 10 — release 1.0.0)

| Phase | Name | Status |
|---|---|---|
| 1 | Foundation | ✅ Complete (built, tested, run-verified) |
| 2 | Database | ✅ Complete (built, tested, run-verified) |
| 3 | Onboarding and Accounts | ✅ Complete (built, tested, run-verified) |
| 4 | Transactions | ✅ Complete (built, tested, run-verified) |
| 5 | Dashboard | ✅ Complete (built, tested, run-verified) |
| 6 | Budgets and Savings Goals | ✅ Complete (built, tested, run-verified) |
| 7 | Bills and Subscriptions | ✅ Complete (built, tested, run-verified) |
| 8 | Analytics and Reports | ✅ Complete (built, tested, run-verified) |
| 9 | Settings and Data Protection | ✅ Complete (built, tested, run-verified) |
| 10 | QA and Release | ✅ Complete (1.0.0 built, installed and uninstalled on this PC) |

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
- Application icon generated from a script (`assets/branding/generate-icon.ps1`) plus `assets/branding/astra-mark.svg`. *(Replaced in 1.1 by `assets/branding/build-brand-assets.ps1` and `assets/branding/source/astra-symbol.svg`.)*

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

## Phase 8 — Analytics and Reports

### Completed tasks

- **Analytics service** (`IAnalyticsService`, `AnalyticsService`): one snapshot per period — income and expense totals and counts, a time series (per day up to 62 days, per month beyond), expense breakdown and income sources by category, spending per weekday, largest expense. Only non-deleted Income and Expense transactions count (transfer fees are expenses); transfers and balance adjustments never do. Exact `long` sums.
- **Periods** (`AnalyticsPeriods`): weekly (Monday–Sunday), monthly, yearly, custom range; previous/next/current period; no navigation into empty future periods.
- **Analytics page**: KPI cards (income, expenses with average per day, net with share of income kept, largest expense); charts — *Income vs expenses* (columns), *Cash flow* (running net line), *Expense breakdown* (donut + clickable legend), *Income sources* (bars), *Monthly trends* (12-month lines), *Spending habits* (spending per weekday + the busiest day). Formatted currency tooltips and compact axis labels, theme-aware colours, two-column → one-column layout. Empty periods show an explanation instead of empty charts; each chart has its own "nothing to show" state. Clicking a category opens its transactions.
- **Report service** (`IReportService`, `ReportService`): Income, Expense, Cash Flow, Account Balance, Budget and Savings Goals reports for any period, as a format-independent `ReportDocument` (summary, tables with typed cells, totals, notes). Every matching record is included — no paging or truncation — and each report states what it includes.
  - *Cash flow*: total balance at start + income − expenses + balance adjustments = total balance at end (transfers cancel out across accounts), with a running balance per day/month.
  - *Account balance*: per account opening (before the period), income, expenses, transfers in/out, adjustments and closing balance; inactive accounts included.
  - *Budget*: one row per budget and month (whole calendar months, current limits).
  - *Savings goals*: allocation of each goal at the end of the period plus contributions and releases within it.
- **Exporters** (`ReportExporter`): **PDF** (PDFsharp/MigraDoc 6.2.4: A4, landscape for wide tables, repeating table headers, page numbers, Segoe UI embedded), **Excel** (ClosedXML: summary sheet + one sheet per table, real numbers with currency number formats, real dates in the user's date format, frozen header rows), **CSV** (same sections; plain machine-readable amounts like the transaction export, user date format, UTF-8 BOM, formula-injection protection). Files are written to a temporary path and moved into place.
- **Reports page**: choose report, period (this/last month, this/last year, custom) and format; live preview of the summary and the number of records per table ("the export contains all N records"); export via the native Save dialog; open the file or show it in its folder.
- The placeholder ("in development") page is gone: every navigation page now has its real implementation, and startup fails fast if a page has no view model.

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **301 passed, 0 failed** (283 previous + 18 new); 8 consecutive full runs green after the backup lock fix.

New coverage: period resolution (Monday weeks, month-end shifts, bucket choice); analytics totals excluding transfers/deleted and including fees, weekday and category breakdowns, yearly buckets, empty periods; income/expense reports (every record, totals, shares, fee labelling); cash-flow reconciliation (opening + income − expenses + adjustments = closing = total balance); account report matching account balances; budget rows per month; goal allocations as of the period end; CSV (BOM, plain amounts, formula protection, totals), Excel (sheets, numeric/date cells and formats, totals), PDF (multi-page, portrait/landscape), no partial file after a failed export; Analytics and Reports pages; every page opens a real view model; every `StaticResource` is defined where it is used; repeated-error throttling; log size cap.

### Manual verification (real app, 125 % DPI)

- Analytics: monthly and yearly views, all six charts, empty-state behaviour; layout at 1600 px.
- Reports: exported Expense report (PDF and Excel), Account Balance report (PDF, landscape) and Cash Flow report (CSV) through the native Save dialog; the PDFs were reviewed page by page and the figures reconcile with the dashboard total (Rp14.948.500).
- Test data was only borrowed; the real data folder is back to a clean setup state.

### Bugs found and fixed during this phase

- **Analytics page failed to load** — it used a style (`LegendDot`) defined only inside the Dashboard page. Moved to the shared styles; a new test now checks every `StaticResource` in every XAML file.
- **Error storm filled the log** — that layout error repeated on every layout pass and each repeat was logged (≈130 MB in three minutes). Identical UI errors are now logged and shown once per minute, and each day's log file is capped at 20 MB (one notice when the cap is reached). The oversized log from the test run was moved out of the data folder.
- **Backups could fail with "database is locked"** if another connection briefly held a lock (seen as an intermittent test failure). Backup connections now use a 5-second busy timeout and retry a few times.
- PDFsharp's built-in Windows font lookup doesn't include Segoe UI → a small font resolver loads it from the Windows Fonts folder (Arial fallback).
- MigraDoc ignored the landscape flag → wide reports set the A4 landscape size explicitly.
- Income-sources rows overlapped the amount with the name → full-width row layout.

### Important decisions

1. **PDF library: PDFsharp/MigraDoc 6.2.4 (MIT), pinned exactly.** PDFsharp 6.5.0 and later are AGPL-3.0, QuestPDF uses a non-OSI community licence; the user chose the last MIT release (2026-11-10). Never upgrade past 6.2.x without a licence review.
2. **Security pin:** PDFsharp 6.2.4 depends on `System.Security.Cryptography.Pkcs` 8.0.1 (CVE-2026-70162, CVE-2026-84008). Central transitive pinning forces 10.0.15; `dotnet list package --vulnerable` reports no vulnerable packages.
3. **One report model, three renderers**, so PDF, Excel and CSV always contain the same records and totals.
4. **Opening balance** of an account counts from the beginning of time (it has no date); "balance at start of period" = opening balance + all activity before the period.
5. **CSV amounts are machine-readable** (no grouping, base currency stated in the header) — consistent with the transaction export; PDF and Excel show formatted currency.

### Remaining / notes

- Reports use the current budget limits for past months (no history of limit changes in v1) — stated in the report note.
- Large custom ranges are computed in memory; fine for personal data volumes, revisit in Phase 10 performance testing.

---

## Phase 9 — Settings and Data Protection

### Completed tasks

- **Settings reorganised into sections** with a section list (beside the content on wide windows, a wrapping row above it on narrow ones): General, Appearance, Categories, Notifications, Security, Data management, About.
- **General**: application language; number format and date format (applied app-wide immediately, with a live preview); base currency shown read-only with the reason (it is fixed once accounts exist — v1 uses one currency).
- **Categories**: income and expense lists; add custom categories (name, icon, colour); edit name/icon/colour (keeping a built-in name keeps it translated); deactivate/reactivate with confirmation; show deactivated. The transfer-fee category cannot be deactivated (service rule), because transfers with fees are recorded in it.
- **Notifications**: adds **backup reminders** — after 7, 14 or 30 days without a backup (counted from the first check for a new user, repeated at most once per period; automatic backups count).
- **Security**: change PIN (current PIN verified and counted as an attempt; new PIN twice), lock now, and a configurable **wrong-PIN cooldown** (3/5/10 allowed attempts; first pause 30 s/1/5/15 min, doubling to at least 15 min). The page states plainly that the PIN is access control and does not encrypt the data file.
- **Data management**:
  - Backup folder (change/open), automatic schedule (off/daily/weekly/monthly), number of automatic backups to keep, last backup, **Back up now**.
  - **Backup history** (manual, automatic, safety copies before updates and restores) with **Restore** per entry and **Restore from file…**.
  - **Safe restore** (`RestoreService`): (1) validate — SQLite header, `integrity_check`, Astra tables, no migrations from a newer version, not the live file; (2) safety copy of the current data (`pre-restore-*.db`) via the online backup API; (3) the UI is blocked by a full-window "Restoring…" gate and reminders are stopped, so nothing else writes; (4) the backup is staged into a verified temporary copy and copied over the live database with SQLite's backup API (one transaction — a failure leaves the data unchanged); (5) the database initializer upgrades an older backup and runs `quick_check`; if the restored data can't be opened, the safety copy is put back automatically; (6) the regional format and all pages reload; (7) success or the exact problem is reported. The app then locks: the restored data's own PIN applies (stated in the confirmation).
  - **Export all data** as one readable JSON file (`DataExportService`): accounts, categories, all transactions including deleted ones (flagged), balance adjustments, budgets, goals and allocations, bills and payments; amounts as integer minor units with the currency stated. The PIN hash and other settings are never exported.
  - **Import transactions from CSV** (`TransactionImportService` + import dialog): comma/semicolon/tab detection, quotes and multi-line fields, UTF-8 BOM; column mapping guessed from English/Indonesian headers and Astra's own export (adjustable), date format and number format (plain or the app's regional format), target account; a **preview** marks every row Ready / Possible duplicate / Skipped / Error with the reason; duplicates (same account, date, type, amount, description — or repeated in the file) are excluded unless chosen; transfers and rows of other accounts are never imported; unknown categories use "Other income/expenses". All selected rows are saved **in one database transaction through the normal transaction validation** — all or nothing.

### Build status

`dotnet build` — **succeeded, 0 warnings, 0 errors**.

### Test status

`dotnet test` — **316 passed, 0 failed** (301 previous + 15 new); three consecutive full runs green.

New coverage: configurable PIN cooldown (applied on wrong PINs, invalid values refused, max cooldown); fee category protected; restore (data replaced, safety copy holds the previous data, history lists kinds, database usable afterwards); invalid backups refused (text file, non-Astra database, newer version, live file, missing file) with the current data unchanged; JSON export (all records incl. deleted and transfer links, no security data); CSV parsing (quotes, semicolons, multi-line, BOM); Astra-export import (mapping, types, transfer/other-account skipping, category fallback, formula-protection removal, bad dates/amounts, duplicates on re-import); bank-style import (signs, regional numbers, date variants, in-file duplicates, all-or-nothing rollback); backup reminders; Settings sections, categories, regional formats, change PIN/cooldown/lock, backup + restore through the shell (lock afterwards), JSON export and CSV import through the UI.

### Manual verification (real app, 125 % DPI)

- All sections; Back up now; CSV import of a bank-style file (2 ready, 1 error shown with its reason); JSON export (checked: 6 transactions including the 2 imported, no PIN data); **Restore from file** of the earlier backup: confirmation with backup details → "Restoring…" → lock screen → unlock → dashboard back at Rp14.948.500 (the import undone), safety copy `pre-restore-*.db` written.
- Test data was only borrowed; the real data folder is back to a clean setup state.

### Bugs found and fixed during this phase

- Change-PIN form lost its "incorrect PIN" message (clearing the field cleared the error) — order fixed, covered by a test.
- Import preview showed internal category keys ("OtherIncome") and the dialog content was wider than the dialog — now translated names and a two-column layout with the reason on its own line.
- An older budget-reminder test now had to opt out of the new backup reminders.

### Important decisions

1. **Import/export (user's choice, 2026-11-11):** JSON for the full export; CSV import of income/expenses into one account with preview, validation and duplicate detection. Full-data *import* is covered by backup/restore.
2. **Restore keeps the backup's PIN** and locks the app afterwards — a backup is a complete snapshot, including its access settings.
3. **Transfers are never imported from CSV**: a statement shows only one side, and guessing the other account could create money that doesn't exist.
4. **Duplicate rule** for imports: same account, date, type, amount and description (case-insensitive). Possible duplicates can still be imported deliberately.
5. **Cooldown settings live in the database** (backed up with the data); the original policy remains the default.

### Remaining / notes

- Clicking a backup reminder doesn't open Settings directly (notifications have no actions in v1); the text says where to go.
- Restoring a backup made by an older app version upgrades it (with the usual pre-migration copy); a backup from a newer version is refused.

---

## Phase 10 — QA and Release

### Completed tasks

- **Version 1.0.0** (`Directory.Build.props`), release notes in [docs/RELEASE_NOTES.md](RELEASE_NOTES.md), README install/build sections.
- **Installer (Inno Setup 6.7.3, the user's choice; installed per-user with permission on 2026-11-11)** — `installer/AstraFinance.iss`:
  - Per-user, **no administrator rights** (`PrivilegesRequired=lowest`), installs to `%LOCALAPPDATA%\Programs\Astra Finance`; 64-bit, Windows 10 2004+.
  - Start Menu shortcut, **optional Desktop shortcut** (unchecked by default); both carry the app's AppUserModelID so Windows notifications are attributed to Astra Finance; the notification display name is registered for the user and removed on uninstall.
  - Proper Windows uninstaller (Settings → Apps). Closes a running Astra Finance first (single-instance mutex).
  - **Data is preserved by default**: after removing the program the uninstaller asks whether to delete `%LOCALAPPDATA%\AstraFinance` — default **No**, deleting requires a second confirmation (default No again). Silent uninstalls never delete data unless `/DELETEUSERDATA` is given. Backups in other folders are never touched.
- **One-command release build** — `installer/build-release.ps1`: tests (Release) → self-contained `win-x64` publish → installer, printing the SHA-256.
- **New tests**: upgrade of a database created with the **first release schema** (real records in every table) to the current schema — safety copy taken, every balance, bill payment, budget and allocation intact; **offline guarantee** (none of the app's assemblies references HTTP/socket/web libraries); **end-to-end reconciliation** across balances, analytics, all report types and budgets with transfers, fees, adjustments, delete/restore, bill payment, goal allocation and CSV import.
- **Fix**: Transactions table on small windows — Type and Account columns hide below 820 px and Category below 640 px, so Date, Description and **Amount** always stay visible (all fields remain in the details panel).

### Build status

`dotnet build` / `dotnet publish -c Release -r win-x64 --self-contained` — **succeeded, 0 warnings, 0 errors**. Installer: `artifacts/installer/AstraFinance-Setup-1.0.0.exe`, 56.8 MB.

### Test status

`dotnet test` — **321 passed, 0 failed** (316 previous + 5 new), in Debug and in Release (release script).

### QA performed (real app, Windows 11, 125 % DPI)

| Area | Result |
|---|---|
| Unit + integration tests | 321/321 (Debug and Release) |
| Clean installation | Silent per-user install; no admin prompt; Start Menu + Desktop shortcuts; Apps entry "Astra Finance 1.0.0 — Aprotoos"; first launch from the Start Menu shows onboarding on an empty data folder; onboarding completed (account, PIN, backups) → Dashboard |
| Single instance | Second launch (Desktop shortcut) focuses the running window — one process |
| Restart | PIN required; unlock → Dashboard |
| Uninstall (silent) | Program, shortcuts, Apps entry and notification key removed; **data kept** |
| Reinstall | Existing data picked up (lock screen with the existing PIN) |
| Uninstall (interactive, default answers) | Program removed; **data kept** (log: "User chose No") |
| Uninstall `/DELETEUSERDATA` | Program and data removed |
| Upgrades | Automated test from the first schema; earlier phases upgraded the real Phase 4 database twice with safety copies |
| Backup & restore | Automated (service + UI) and in the real app (Phase 9) |
| Offline | Automated reference check; the app never needs a network connection |
| Light / Dark themes | All 9 pages captured in Light at 1600×1000 and Dark at the minimum window size — no unreadable or unstyled elements |
| Smaller windows | All 9 pages at ≈800×600 and at the 640×520 minimum: sidebar collapses, Quick Add becomes an icon, cards stack; Transactions fixed (see above) |

The user's own data folder was set aside during the clean-install test and restored unchanged afterwards (preferences: English, Follow System, Blue).

### Notes from QA

- During one interactive uninstall run, both "delete data?" prompts were answered **Yes** by someone at the desktop (logged "User chose Yes" twice; the automation could not see those buttons). Only QA test data was present; this confirms deleting requires two explicit answers. The default-answer path was then verified separately (data kept).

### Known limitations (also in the release notes)

- The installer is not code-signed (SmartScreen may warn); signing needs a code-signing certificate.
- Installer screens are English only (Inno Setup ships no Indonesian translation); the app itself is Indonesian/English.
- PIN = access control, not encryption; one base currency; Windows notification clicks only dismiss; reports use current budget limits for past months; CSV imports over 20 MB are refused.

### Important decisions

1. **Inno Setup, per-user** (user's choice): no admin rights, data outside the install folder, uninstall keeps data by default.
2. **Self-contained publish** (no separate .NET runtime install needed), folder-based (not single-file) so native SQLite/Skia libraries load reliably.
3. Inno Setup 6.7.3 was installed for the current user only via winget (official jrsoftware release, SHA-256 verified) after the user's approval.

---

# Version 1.1 — Visual rebranding (branch `rebrand-v1.1`)

Specification: `docs/ASTRA_FINANCE_REBRANDING.md`. Phases below are the rebranding phases, not the original 1–10.

## Rebrand Phase 1 — Audit ✅

Audit reported and approved. Decisions: version **1.1.0** on branch `rebrand-v1.1`; Calendar as a read-only page over
existing transactions; Financial Overview delivered through Dashboard + Accounts; Insights = Analytics; Reports gets
summary cards and a preview table (no duplicate charts); accounts keep **deactivate** (no hard delete); sounds generated
in code (no audio dependency), off by default; installer gains an Indonesian translation.

## Rebrand Phase 2 — Design system and assets ✅

### Completed tasks
- Light/Dark palettes rebuilt on Deep Navy `#172554`, Royal Blue `#2563EB`, White; functional green/red/amber/blue kept.
- "Blue" accent = Royal Blue, the default (listed first as "Royal Blue (Astra)"); other presets kept.
- Accent gradient (Deep Navy → Royal Blue) for primary buttons; text on fills chosen by contrast; on-fill hover/pressed overlays.
- Tokens for brand sidebar, hero cards, glass PIN panel, splash backdrop, status badges (`Badge.Success/Danger/Warning/Info/Neutral`),
  `Card.Hero`, `Text.OnHero*`, `Motion.Slow`; radii 6/8/14/16.
- Logo: "Winged Star" chosen by the user from three concepts. Vector master `assets/branding/source/astra-symbol.svg`;
  generator `assets/branding/build-brand-assets.ps1` (WPF only) produces SVG/PNG variants and lockups, the 16–256 px `.ico`
  and Inno Setup wizard bitmaps. See `assets/branding/README.md`.
- In-app `Brand.Symbol` replaces `Brand.Star`; brand tiles use the Deep Navy app-icon look.
- Cached Windows notification icon is refreshed when the app icon changes (previously kept forever).
- New records default to Royal Blue; existing records keep their colors (no data change).

### Build status
Build succeeded (warnings as errors).

### Test status
327/327 passed (new: brand default and gradient contrast, Tokens keys resolvable, in-app symbol matches master,
icon sizes, asset set, notification icon refresh).

### Known issues / notes
- Not yet checked in the running app: the visual pass happens at the start of Phase 3 on a separate test data folder
  (launching now would create a database in the real data folder).
- The wordmark uses Segoe UI Variable Display Semibold, whose licence doesn't explicitly cover logo use; the generator
  can switch to an open-licence font (e.g. Inter) once installed.
- The sidebar is still the light surface in Light mode until Phase 3 applies the navy brand sidebar.

### Important decisions
1. Royal Blue on Deep Navy is unreadable as navy → navy, so symbols on navy use Royal Blue → Sky (`#60A5FA`, a Royal Blue tint).
2. Accent presets other than Blue stay available (existing user preference); a saved accent is respected.
3. No database, migration, financial-logic or PIN changes.

## Rebrand Phase 3 — Application shell ✅

### Completed tasks
- **Sidebar:** Deep Navy brand gradient in both themes; symbol + white wordmark; nav items with sidebar text colors,
  translucent hover/selected states and a Sky active indicator; white focus ring on navy; collapse toggle restyled;
  collapsed/expanded preference unchanged (already persisted).
- **Title bar:** Deep Navy caption with white text and light caption buttons, continuing the sidebar.
- **Header:** page title with its section icon tile; gradient Quick Add; search, notifications and menu kept.
- **Buttons:** icon buttons support on-fill/navy states (`Button.Icon.OnBrand`); disabled primary buttons turn neutral
  instead of a washed-out gradient.
- **Dialogs:** floating panel with an entrance animation (scrim fade, panel fade + scale from 96 %).
- **Reduced motion:** `MotionPreferences` follows Windows "Animation effects"; sidebar, page, dialog and splash motion
  become instant when it is off.
- **Splash ("Elegant Dark"):** Deep Navy window with orbit-ring details, large symbol, product name, loading line and
  version; fades in; shown while the database opens and the shell initializes; closed before any startup error
  message. Shutdown is explicit until the main window exists, so closing the splash can't end the app.
- **PIN screen ("Premium Glass"):** Deep Navy backdrop with orbit rings, translucent panel with a glass edge,
  rounded-square keypad with white numerals, delete and gradient unlock keys. The keypad types into the existing PIN
  box (same length limit, caret and binding); PIN logic, cooldown and verification are unchanged.
- **Onboarding:** progress rail uses the navy brand sidebar style.
- **Development:** Debug builds honour `ASTRA_FINANCE_DATA_DIR` to run against a separate data folder (Release ignores it).

### Build status
Debug and Release builds succeeded (warnings as errors).

### Test status
327/327 passed. Manual checks on a test data folder (real data folder verified unchanged by hash):
light and dark shell, collapsed sidebar, narrow window (auto icon-only sidebar), Quick Add dialog, splash,
PIN keypad (wrong PIN → error message; correct PIN → unlocked), onboarding rail on an empty folder.

### Known issues / notes
- Dialogs animate in; closing is instant (an exit animation would need the dialog content to outlive its view model).
- The splash appears ~1.9 s after launch (runtime start-up) and stays at least 0.5 s so a fast start doesn't flash it.
- The splash shows the assembly version (still 1.0.0 until the Phase 8 version bump).

## Rebrand Phase 4 — Financial pages ✅

### Completed tasks
- Shared page components (`Themes/Controls/Cards.xaml`): interactive and hero card buttons, tinted icon tiles,
  statistic text, toolbar surface; rounded, slightly thicker progress bars.
- **Dashboard:** hero Total Balance; income/expense cards with icon tiles; recent rows tinted by type; budget bars
  blue / amber / red.
- **Accounts:** hero total; account cards with identity tile, balance and Edit / Deactivate–Reactivate on the card
  (same confirmation as the details view).
- **Transactions:** summary cards for everything matching the filters (from the existing `SummarizeAsync`), filter
  toolbar, type badges and tiles (income green, expense red, transfer blue); cards adapt to small windows.
- **Calendar (new, read-only):** month grid with income/expense markers, today highlight, month totals and a
  selected-day panel; uses the existing transaction query; `CalendarSummary` in Core.
- **Budgets:** cards with status badge, percentage, spent/limit, progress and remaining (normal blue, approaching
  amber, exceeded red); hero monthly total.
- **Savings Goals, Bills:** card redesigns with identity tiles, percentages, deadlines, statistic summary cards.
- **Analytics (Insights):** toolbar period selector; KPI cards with icon tiles.
- **Reports:** summary cards and a structured "included in the export" table; export unchanged.
- **Settings:** section icon tiles; **Backup Center** (status badge from the schedule, last backup, location, Back up
  now / Restore from file, history table); category cards; About brand showcase.

### Build status
Build succeeded (warnings as errors).

### Test status
342/342 passed (new: account-card deactivation, Calendar summary and view model, backup health, ten-page catalog).
Manual checks on the test data folder (real data folder verified unchanged by hash): every page in light and/or
dark, narrow layouts (Transactions, Calendar), Indonesian (PIN screen, Calendar), backup into the test folder.

### Known issues / notes
- The test database copied from earlier QA stored a backup folder inside the real data folder; it was re-pointed to
  the test folder before any automatic backup was due (nothing was written to the real folder).
- Settings sections keep one card per section (with icon headers) rather than one card per setting.

## Rebrand Phase 5 — Notifications, interactions and audio ✅

### Completed tasks
- **Notification cards (Premium Floating Card):** top right below the header, newest first, up to three; tinted
  icon tile per type, slight transparency, popup shadow; slide-in and fade-out (`MotionAssist`). Duration from
  `ToastTiming`: 4–12 s by text length, warnings longer, errors stay until dismissed. The countdown bar pauses while
  the card is hovered or has keyboard focus. When room is needed the oldest self-closing card goes first.
- **Micro-interactions:** buttons settle to 98 % and clickable cards to 99 % while pressed (instant feedback, no
  animation); existing hover, dialog, sidebar and dropdown motion kept. All motion follows Windows "Animation effects".
- **Optional UI sounds:** short two-note tones generated in code (`ToneSynthesizer`, 16-bit WAV in memory) and played
  with the Windows sound player — no audio files or dependencies. Events: PIN accepted / rejected, important
  notifications (warnings and errors), transaction saved. Off by default.
- **Sound settings** (Settings → Notifications → Sounds): master switch (disables every sound), volume 0–100,
  one switch per category, preview per category (needs the master switch). Stored in the local preferences file;
  1.0 preference files load unchanged with sounds off.
- Backup reminder dropdown widened (its text was cut off).

### Build status
Debug and Release builds succeeded (warnings as errors).

### Test status
352/352 passed (new: toast timing, sound rules and volume clamping, tone WAV format and volume scaling,
1.0 preferences compatibility, notification ordering/stickiness/sounds, sound settings persistence and preview).
Manual: success card with countdown at top right; Sounds card; preview plays without errors (real data folder
verified unchanged by hash).

### Known issues / notes
- Sound output itself was not listened to during automated checks; playback failures (e.g. no audio device) are
  ignored by design.
- Clicking a Windows (system) notification still only dismisses it (unchanged from 1.0).

## Rebrand Phase 6 — Installer and uninstaller ✅

### Completed tasks
- **Banner ("Minimalist Corporate"):** Deep Navy, blue-gradient symbol left, white wordmark right, no tagline
  (`installer/branding/header-banner.png`, generated by `build-brand-assets.ps1`). Shown on every inner Setup page
  and in the uninstaller (installed to `{app}\branding` so the uninstaller can load it); page titles stay readable
  in white beside it. Finish page keeps the tall navy brand image; app icon on Setup and in Apps.
- **Light/dark:** `WizardStyle=modern dynamic` follows Windows; the brand images are used in dark mode too
  (`...DynamicDark` directives — without them Inno showed its stock dark image).
- **Indonesian:** `installer/Languages/Indonesian.isl` (own translation of every Default.isl message) plus localized
  custom messages (shortcut texts, data prompts). A first installation follows the Windows display language;
  upgrades use the language Setup remembers for the installation, which the app keeps in step with its own
  language (`InstallerLanguageSync`, only Astra Finance's own uninstall entry, only when installed with Setup).
  Restarting Setup in the app language from its own start-up code was tried and rejected: Windows refused it
  (access denied) for both Exec and ShellExec.
- **Version** is read from the published app (`ProductVersion`, single source in Directory.Build.props).
- **Data protection unchanged:** uninstall keeps data by default; deleting needs two explicit confirmations with
  "No" as default; silent uninstall keeps data unless `/DELETEUSERDATA`. Start Menu shortcut and optional Desktop
  shortcut kept.

### Build status
Release build script succeeded (tests → self-contained publish → installer, 57.2 MB).

### Test status
353/353 passed (new: installer language names match the setup script and AppId).
Installer QA with the user's real data folder moved aside (backed up and restored; identical by hash afterwards)
and a copy of the test data in its place:
- Released 1.0.0 installed silently → upgraded with the new Setup (English, since 1.0.0 remembered English);
  new app started on the kept data and switched the remembered language to Indonesian.
- Setup run again → opened in Indonesian by itself; dark-mode Finish page shows the navy brand image.
- Interactive uninstall (Indonesian, then English): confirmation, branded progress window, data question with
  "No" as default → program, shortcuts, notification registration and Apps entry removed; data unchanged byte for
  byte.
- Silent uninstall → data kept. Silent uninstall with `/DELETEUSERDATA` (test copy) → data removed as requested.

### Known issues / notes
- The uninstaller uses the language of the most recent installation/upgrade (Inno Setup limitation); after
  changing the app language, the next upgrade brings the uninstaller in line.
- The installer is still not code-signed (SmartScreen may ask for confirmation).
- The installer still reports version 1.0.0 until the Phase 8 version bump.

## Rebrand Phase 7 — Quality assurance ✅

### Completed tasks
- **Data-safety review:** `AstraFinance.Infrastructure` (database, migrations, backup/restore, exports) is unchanged
  since `v1.0.0` apart from the PDF brand colour below; Core changes are UI-only (accent palette, calendar summary,
  feedback rules, strings) plus Royal Blue as the default colour offered for new records. No schema change, no
  migration, no change to PIN hashing or balance rules.
- **Manual QA on the test data folder** (debug override, never the real folder), at the display's 125% scaling:
  every page in English/Light, Indonesian/Dark, Indonesian at the minimum window (640×520) and Follow System
  (Windows in dark mode → app dark); PIN wrong/right; Quick Add validation and save with exact balance, expense,
  net and budget-warning changes; delete → totals restored → Show deleted → restore; manual backup → data changed →
  restore from Backup history (safety copy, lock, PIN, data back); Expense Report exported as PDF, Excel and CSV
  with matching totals; keyboard: PIN + Enter, Tab focus ring, Ctrl+K search with arrow/Enter to open a result,
  Ctrl+B, Ctrl+N, Esc, Ctrl+L; shell icons of the app, Setup and `astra.ico` at 16–256 px on light and dark.
- **Regressions fixed:**
  - Analytics KPI labels (icon tile beside the label) were clipped in Indonesian ("Bersih (pemasukan - pengel…") —
    the header is now a DockPanel and the label wraps.
  - Transactions: at small widths the type chips slid under the "Show deleted" switch — the switch is docked right
    and the chips wrap.
  - Transactions: at the minimum window height the table showed only its header — the page now scrolls below a
    minimum content height, so several rows stay visible; larger windows are unchanged.
  - Transactions: with the details panel docked, the new type badges were clipped — columns now follow the table's
    own width (Type/Account step aside first).
  - Transactions: the new summary cards totalled deleted records under the normal Income/Expenses/Net labels while
    "Show deleted" was on — they are hidden in that view (deleted records never count).
  - PDF reports still printed the "Astra Finance" line in the 1.0 coral — now Royal Blue.
  - `AccentColor` documentation still called Coral the brand default.
- Real data folder verified identical by hash (4 files); the app is not installed.

### Build status
Debug and Release builds succeed with 0 warnings.

### Test status
353/353 passed after the fixes.

### Known issues / notes
- Pre-existing (unchanged 1.0 code, not introduced by the rebrand): report category shares are truncated (91% + 8%);
  the pre-restore safety copy is named with a UTC time while manual backups use local time; data-grid rows expose
  their type name to screen readers instead of their content.
- A saved accent is respected (decision from Phase 1): a 1.0 user whose preferences say Coral keeps Coral until
  choosing Blue in Settings → Appearance.
- Only 125% scaling was exercised (the app is Per-Monitor V2 aware); other scale factors and live switching of the
  Windows theme were not tested because they would change system settings.
- UI sounds were verified by tests (valid short WAV, volume scaling) but not listened to.
- Dialogs have no exit animation; the installer is not code-signed; versions still read 1.0.0 until Phase 8.

## Rebrand Phase 8 — Version 1.1.0 ✅

### Completed tasks
- Version **1.1.0** (minor: new Calendar page, Backup Center, optional sounds, Indonesian installer; no data-format
  change). Single source `Directory.Build.props`; app manifest identity 1.1.0.0; Setup, Apps entry, splash and
  About read it from the build.
- `docs/RELEASE_NOTES.md`: new 1.1.0 section (what's new, fixes, upgrading, new limitations) above the unchanged 1.0.0
  notes. README status and install text updated.
- `v1.0.0` tag and history untouched; nothing pushed, no tag created for 1.1.0 yet (awaiting approval).

### Build status
`installer/build-release.ps1`: tests → self-contained publish → `AstraFinance-Setup-1.1.0.exe` (57.2 MB).
Dependency audit: no vulnerable packages.

### Test status
353/353 passed (Release). Published exe: ProductVersion 1.1.0, FileVersion 1.1.0.0; Setup reports 1.1.0; splash and
About show "Version 1.1.0" (Debug build on test data).

### Known issues / notes
- The 1.0.0 → 1.1.0 upgrade path was verified in rebrand phase 6 with this installer script; it was not repeated
  after the version bump.
