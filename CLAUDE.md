# Astra Finance — Project Specification & Claude Code Instructions

## 1. Project Overview

**Project Name:** Astra Finance  
**Application Type:** Native Windows Desktop Application  
**Primary Purpose:** Personal finance management  
**Architecture:** Local-first, offline-first  
**Primary Language:** Indonesian and English  
**Developer / Owner:** Aprotoos

Astra Finance is a modern personal finance management application designed for Windows.

The application allows users to manage personal income, expenses, account balances, budgets, savings goals, recurring bills, analytics, and financial reports.

The application must prioritize:

- Financial accuracy.
- Data integrity and reliability.
- A polished native Windows desktop experience.
- Offline functionality.
- Privacy and local data ownership.
- Lightweight performance.
- Maintainable and modular architecture.

This is a personal finance application, not a banking application. It must never imply that it connects to or controls a user's actual bank account unless a future integration explicitly implements that capability.

## 2. Core Technology Stack

Use the following technology stack:

| Component | Technology |
|---|---|
| Framework | .NET 10 LTS |
| Desktop UI | WPF |
| Language | C# |
| Architecture | MVVM |
| Database | SQLite |
| ORM | Entity Framework Core 10 |
| MVVM Toolkit | CommunityToolkit.Mvvm |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| Charts | LiveCharts2 |
| Excel Export | ClosedXML |
| CSV Export | Native .NET functionality |
| PDF Export | Select a suitable library after checking its current license |
| Installer | MSIX or another reliable Windows installer |

Use stable, mutually compatible package versions.

Do not introduce unnecessary dependencies.

Before adding a third-party package, verify its maintenance status, compatibility, licensing, and security implications.

Do not use a web application, browser wrapper, Electron, or a hosted web interface as the primary application.

The result must be a genuine Windows desktop application.

## 3. Architecture and Project Structure

Use a clean, modular MVVM architecture.

Suggested structure:

```text
AstraFinance/
│
├── AstraFinance.sln
│
├── src/
│   ├── AstraFinance.App/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Themes/
│   │   ├── Resources/
│   │   ├── Converters/
│   │   └── App.xaml
│   │
│   ├── AstraFinance.Core/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   ├── Models/
│   │   └── Services/
│   │
│   ├── AstraFinance.Infrastructure/
│   │   ├── Database/
│   │   ├── Repositories/
│   │   ├── Services/
│   │   ├── Backup/
│   │   └── Export/
│   │
│   └── AstraFinance.Tests/
│
├── assets/
│   ├── branding/
│   └── icons/
│
├── docs/
│
└── README.md
```

Adjust this structure when necessary, but maintain clear separation between UI, business logic, data access, and infrastructure.

### Architectural rules

- Views must not directly execute database queries.
- ViewModels must not contain database-specific implementation details.
- Business rules must be implemented in services or domain logic.
- Use dependency injection.
- Use asynchronous operations for database access and file operations where appropriate.
- Avoid blocking the UI thread.
- Use observable state and proper command bindings.
- Keep business logic testable without launching the WPF application.

## 4. Branding and Visual Design

The application name is **Astra Finance**.

### Visual identity

Use a modern, clean, professional financial dashboard aesthetic.

Primary accent: Soft coral / orange.

Dark theme:
- Charcoal background.
- Subtle navy surfaces.
- Soft white primary text.
- Muted gray secondary text.
- Coral accent.

Light theme:
- White background.
- Soft gray surfaces.
- Dark charcoal primary text.
- Muted gray secondary text.
- Coral accent.

### Appearance settings

Support three appearance modes:

1. Light Mode.
2. Dark Mode.
3. Follow System.

Follow System must respond to the Windows application theme preference when available.

### UI standards

- Use Segoe UI or a suitable modern Windows font.
- Use Lucide-style icons.
- Use consistent spacing and typography.
- Use restrained rounded corners.
- Avoid excessive gradients, shadows, and decorative elements.
- Use subtle animations.
- Provide hover, pressed, focused, disabled, loading, and validation states.
- Use consistent notification and dialog styling.
- Ensure readable contrast and keyboard accessibility.

All controls must match the Astra Finance design system.

This includes:
- Dropdown menus.
- Date pickers.
- Context menus.
- Dialogs.
- Tooltips.
- Search fields.
- Tables.
- Buttons.
- Navigation controls.
- Notifications.

Do not leave default WPF controls visibly unstyled.

Do not use operating-system default dialogs when a custom Astra-styled dialog is practical. Native file selection dialogs may remain native when appropriate.

### Responsive and adaptive layout

The UI must adapt to window size.

- Sidebar can expand and collapse.
- Expanded sidebar shows icons and labels.
- Collapsed sidebar shows icons only.
- Content must resize naturally.
- Cards and tables must not overlap.
- Avoid clipped text and inaccessible controls.
- Important actions must remain accessible at smaller window sizes.

## 5. Main Application Navigation

Create the following main navigation:

1. Dashboard
2. Transactions
3. Accounts
4. Budgets
5. Savings Goals
6. Bills & Subscriptions
7. Analytics
8. Reports
9. Settings

Use a collapsible sidebar.

The top navigation area should include:
- Current page breadcrumb or title.
- Global search.
- Notification access.
- Profile or application menu.
- Quick Add Transaction action.

The default startup page must always be Dashboard.

## 6. First-Launch Onboarding

The first application launch must display a setup wizard.

Users cannot enter the Dashboard before completing the required initial setup.

### Onboarding steps

**Step 1 — Welcome**
- Introduce Astra Finance.
- Select Indonesian or English.

**Step 2 — Regional Settings**
- Set the primary currency.
- Configure number formatting.
- Configure date formatting.

Default currency: IDR.

For version 1, use one application-wide base currency. Multi-currency account aggregation and exchange-rate conversion are outside the initial scope.

**Step 3 — Create First Account**

This step is mandatory.

Allow users to create their first account with:

- Account name.
- Account type.
- Opening balance.
- Icon.
- Color.

The opening balance must be allowed to equal zero.

Supported account types:
- Cash.
- Bank Account.
- E-Wallet.
- Savings Account.
- Custom Account.

**Step 4 — Security PIN**
- Create a PIN.
- Confirm the PIN.
- Explain that the PIN protects application access.

**Step 5 — Backup Preferences**
- Select backup location.
- Configure automatic backup schedule.
- Explain manual backup functionality.

**Step 6 — Completion**
- Show setup completion.
- Navigate to Dashboard.

Persist onboarding progress safely so that an interrupted setup can resume without corrupting data.

## 7. Dashboard

Create a minimal financial overview.

Display:

- Greeting and current date.
- Total Balance.
- Monthly Income.
- Monthly Expenses.
- Cash Flow chart.
- Recent Transactions.
- Budget Summary.

Keep the dashboard uncluttered.

Do not display every available statistic on the Dashboard. Detailed information belongs on dedicated pages.

### Dashboard interactions

- Clicking Total Balance navigates to Accounts.
- Clicking Cash Flow navigates to Analytics.
- Clicking a recent transaction opens its details.
- Quick Add opens the simplified transaction form.

All displayed values must be derived from actual database records.

Do not use placeholder financial values after the application has been initialized.

## 8. Transactions

Transactions are the central financial activity module.

Support three transaction types:

### Income
Money entering an account.

### Expense
Money leaving an account.

### Transfer
Money moved between two accounts owned by the user.

A transfer must never be counted as income or expense.

### Transaction fields

- Transaction ID.
- Type.
- Amount.
- Description.
- Category.
- Account.
- Date.
- Optional notes.
- Creation timestamp.
- Last updated timestamp.
- Deletion status.

For transfers, include:
- Source account.
- Destination account.
- Transfer amount.
- Optional transfer fee, if implemented as a separate expense.

### Transaction page

Include:

- Search.
- Date range.
- Category filter.
- Account filter.
- Transaction type filter.
- Sorting.
- Pagination or efficient list virtualization when necessary.

Table columns:

- Date.
- Description.
- Category.
- Account.
- Amount.
- Type.

Users can:
- Add transactions.
- Edit transactions.
- View transaction details.
- Delete transactions.
- Restore deleted transactions when supported.
- Export filtered transactions.

### Quick Add Transaction

Provide two entry methods:

1. Quick Add: simplified form.
2. Full Form: complete transaction form.

Quick Add should include the essential fields:
- Type.
- Amount.
- Category.
- Account.
- Date.

Full Form may include additional notes and details.

Keyboard shortcut:

`Ctrl + N`

Both entry methods must use the same validation and transaction-saving services.

Prevent accidental duplicate submissions.

## 9. Accounts

Support:

- Cash.
- Bank Account.
- E-Wallet.
- Savings Account.
- Custom Account.

Each account includes:

- Account ID.
- Account name.
- Account type.
- Opening balance.
- Current calculated balance.
- Icon.
- Color.
- Active/inactive status.
- Creation date.

### Account functionality

- Create account.
- Edit account.
- Deactivate account.
- View account details.
- View account transaction history.
- Transfer money between accounts.
- Correct account balance through a documented Balance Adjustment.

Never silently overwrite an account balance.

Balance corrections must require a reason or note and must preserve the financial history.

Deactivating an account must not delete its historical transactions.

## 10. Budgets

Support monthly budgets by expense category.

Example categories:

- Food & Drinks.
- Transportation.
- Entertainment.
- Shopping.
- Bills & Utilities.
- Health.
- Education.

Each budget contains:

- Category.
- Budget amount.
- Period.
- Start date.
- Optional notes.

Display:

- Budget limit.
- Amount spent.
- Remaining amount.
- Usage percentage.
- Status.

Statuses:

- Normal.
- Warning.
- Exceeded.

Use configurable warning thresholds.

Only Expense transactions count toward budgets.

Income and Transfer transactions must never affect expense budgets.

## 11. Savings Goals

Allow users to create savings goals.

Each goal contains:

- Goal name.
- Target amount.
- Target date.
- Icon or image.
- Current allocated amount.
- Status.
- Contribution history.

Example:

Laptop Fund:
- Target: Rp8,000,000.
- Allocated: Rp4,000,000.
- Progress: 50%.

### Important financial rule

Savings Goals must use fund allocation rather than creating additional money.

Allocating money to a savings goal must not increase or decrease the user's total account balance.

Contributions must reference the account from which the allocation is made.

Prevent allocations from exceeding the available unallocated balance unless the user explicitly changes the allocation.

Support:
- Create goal.
- Edit goal.
- Add allocation.
- Reduce allocation.
- View contribution history.
- Mark completed.
- Archive goal.

## 12. Bills & Subscriptions

Manage recurring financial obligations.

Examples:
- Electricity.
- Internet.
- Mobile data.
- Streaming subscriptions.
- Software subscriptions.
- Other recurring bills.

Each item includes:

- Name.
- Amount.
- Category.
- Due date.
- Recurrence.
- Payment account.
- Payment status.
- Optional notes.

Supported recurrence:
- Daily.
- Weekly.
- Monthly.
- Yearly.

### Payment behavior

A recurring bill must not automatically reduce account balances merely because it reaches its due date.

Only a confirmed payment may create an Expense transaction.

Avoid duplicate payment records.

### Notifications

Use local Windows notifications when practical.

Allow users to configure notification preferences.

## 13. Analytics

Provide interactive financial analytics.

Charts:

- Income vs Expenses.
- Cash Flow.
- Expense Breakdown by Category.
- Monthly Trends.
- Income Sources.
- Spending Habits.

Filters:

- Weekly.
- Monthly.
- Yearly.
- Custom Date Range.

Charts must use real database data.

Display useful tooltips and formatted currency values.

Handle empty datasets gracefully.

Do not show misleading charts when there is insufficient data.

## 14. Reports

Reports are separate from Analytics.

Analytics visualizes financial information inside the application.

Reports generate exportable documents.

Support:

- Income Report.
- Expense Report.
- Cash Flow Report.
- Account Balance Report.
- Budget Report.
- Savings Goals Report.

Export formats:

- PDF.
- Excel (.xlsx).
- CSV.

Allow users to select a custom reporting period.

Exports must respect active filters and use correct currency and date formatting.

Do not silently omit records.

## 15. Settings

Organize settings into clear sections.

### General
- Application language.
- Date format.
- Number format.
- Primary currency.

### Appearance
- Light Mode.
- Dark Mode.
- Follow System.
- Accent color.

### Categories
- Manage income categories.
- Manage expense categories.
- Add custom categories.
- Edit categories.
- Deactivate categories.

### Notifications
- Enable or disable notifications.
- Configure bill reminders.
- Configure budget warnings.
- Configure backup reminders.

### Security
- Change PIN.
- Lock application manually.
- Configure failed-attempt cooldown.

### Data Management
- Create backup.
- Restore backup.
- Configure automatic backup.
- Import data.
- Export data.
- View backup history.

### About
- Application name.
- Version.
- Developer: Aprotoos.
- License and third-party acknowledgments.

## 16. Security and PIN

The application must require a PIN when launched after initial setup.

Support:
- PIN creation.
- PIN confirmation.
- PIN change.
- Manual application lock.
- Failed-attempt cooldown.

Never store PINs as plaintext.

Use a secure password-hashing mechanism with a unique random salt and an appropriately configured work factor.

Do not store the original PIN in settings or logs.

Important security distinction:

A PIN lock is an application access control, not database encryption.

Do not claim that the SQLite database is encrypted merely because a PIN is enabled.

For version 1, protect application data using appropriate Windows user-profile file permissions. Document the database-at-rest encryption limitation clearly.

Never expose financial data, PINs, or sensitive database contents in diagnostic logs.

## 17. Backup and Restore

Support both automatic and manual backups, plus backup reminders.

### Automatic backup

- Configurable schedule.
- Configurable backup directory.
- Backup history.
- Last backup timestamp.
- Backup status.
- Retention policy.

### Manual backup

Users can create a backup at any time.

### Restore

Before restoring:

1. Validate the selected backup.
2. Create a safety backup of the current database.
3. Ensure the database is not being modified during replacement.
4. Restore safely.
5. Validate database integrity.
6. Reopen the application data.
7. Report success or failure clearly.

Use SQLite's supported backup mechanism or another safe method.

Do not simply copy an actively used SQLite database file without accounting for WAL and journal files.

A failed restore must not destroy the user's existing database.

## 18. Database and Financial Integrity

This section is critical.

Use SQLite with Entity Framework Core.

Use database migrations for schema changes.

Keep the database in a stable application-data directory outside the installation directory.

### Money precision

Never use `float` or `double` for financial calculations.

Use integer minor units or another exact representation.

For IDR in version 1, store whole Rupiah as an integer (`long`).

Design currency formatting so that future currencies with fractional units can be supported safely.

### Balance calculation

Account balance must be derived from:

Opening Balance + Income - Expense + Transfers In - Transfers Out + Balance Adjustments.

The exact implementation must avoid counting transfer records twice.

### Transaction consistency

- Use database transactions for multi-record financial operations.
- Ensure transfers are atomic.
- Prevent duplicate writes.
- Validate account references.
- Validate transaction amounts.
- Validate category references.
- Handle application interruption safely.

### Deletion

Use soft deletion or an equivalent recoverable deletion mechanism for financial transactions.

Deleted records must not appear in normal balance calculations, budgets, or reports.

Preserve enough information to restore records safely.

### Date and time

Store transaction dates consistently.

Do not introduce timezone shifts when saving or displaying date-only financial transactions.

Use UTC timestamps for audit metadata when appropriate, while keeping transaction dates as date-only values.

### Database validation

Add tests for:
- Opening balances.
- Income.
- Expenses.
- Transfers.
- Transfer fees.
- Balance adjustments.
- Editing transactions.
- Deleting and restoring transactions.
- Budget calculations.
- Savings allocations.
- Backup and restore.

## 19. Global Search

Provide a topbar search field.

Search across:
- Transactions.
- Accounts.
- Categories.
- Budgets.
- Savings Goals.
- Bills and Subscriptions.

Display categorized search results.

Clicking a result must navigate to its relevant detail page.

Search must remain responsive with larger datasets.

## 20. Empty, Loading, and Error States

Every major page must have a meaningful empty state.

Examples:

- No transactions yet.
- No accounts available.
- No budgets created.
- No savings goals.
- No recurring bills.

Provide clear actions to create the first relevant record.

Also implement:

- Loading indicators.
- Validation messages.
- Error notifications.
- Confirmation dialogs.
- Success notifications.
- Recovery guidance.

Never display fake data as though it were real user financial information.

Demo data, if provided, must be explicitly identified and opt-in.

## 21. Performance and Reliability

The application must remain responsive.

Requirements:

- Avoid unnecessary database queries.
- Use asynchronous database operations where appropriate.
- Use efficient data loading.
- Avoid excessive chart refreshes.
- Use pagination or virtualization for large transaction lists.
- Dispose resources correctly.
- Prevent multiple application instances when practical; a second launch should focus the existing window.
- Handle unexpected exceptions without silently losing data.

Do not sacrifice financial correctness for performance.

## 22. Future Expansion — Astra Finance Connect

Do not implement this in version 1.

Keep the architecture modular enough to support a future optional Telegram integration.

Potential future features:

- Telegram Bot Assistant.
- Add transactions through Telegram.
- Check balances through Telegram.
- Request financial summaries through Telegram.
- Receive bill reminders.
- Optional synchronization between desktop and other devices.

The initial application must not depend on Telegram, cloud hosting, or a remote backend.

Do not create an online account system in version 1.

## 23. Implementation Roadmap

Implement the application in controlled phases.

Do not attempt to build everything in one uncontrolled operation.

### Phase 1 — Foundation
- Inspect workspace.
- Create solution and projects.
- Configure .NET and dependencies.
- Implement dependency injection.
- Establish MVVM architecture.
- Create design tokens and theme system.
- Create main application shell.
- Implement collapsible sidebar.
- Implement localization foundation.

### Phase 2 — Database
- Create entities.
- Configure SQLite.
- Configure Entity Framework Core.
- Implement migrations.
- Implement repositories and services.
- Implement financial calculation rules.
- Write core unit tests.

### Phase 3 — Onboarding and Accounts
- Implement first-launch wizard.
- Implement mandatory first account creation.
- Implement PIN setup.
- Implement account management.
- Implement opening balance.
- Test account balance calculations.

### Phase 4 — Transactions
- Implement Income.
- Implement Expense.
- Implement Transfer.
- Implement Quick Add.
- Implement Full Form.
- Implement editing and deletion.
- Implement transaction details.
- Test financial integrity.

### Phase 5 — Dashboard
- Implement dashboard cards.
- Implement Cash Flow chart.
- Implement recent transactions.
- Implement budget summary.
- Implement navigation interactions.

### Phase 6 — Budgets and Savings Goals
- Implement budget management.
- Implement budget calculations.
- Implement savings goal management.
- Implement fund allocation.
- Test allocation integrity.

### Phase 7 — Bills and Subscriptions
- Implement recurring bill management.
- Implement payment confirmation.
- Implement local notifications.
- Prevent duplicate payment records.

### Phase 8 — Analytics and Reports
- Implement analytics charts.
- Implement date filters.
- Implement PDF export.
- Implement Excel export.
- Implement CSV export.
- Verify exported data.

### Phase 9 — Settings and Data Protection
- Implement appearance settings.
- Implement language switching.
- Implement backup scheduling.
- Implement manual backup.
- Implement restore.
- Implement data import/export.
- Implement security settings.

### Phase 10 — Quality Assurance and Release
- Run all unit tests.
- Run integration tests.
- Test clean installation.
- Test application upgrades.
- Test backup and restore.
- Test offline operation.
- Test Light and Dark themes.
- Test smaller window sizes.
- Fix all critical issues.
- Prepare release installer.
- Prepare README and release notes.

## 24. Claude Code Working Instructions

You are responsible for implementing Astra Finance based on this specification.

Follow these rules strictly:

1. Inspect the existing workspace before making changes.
2. Do not overwrite unrelated files.
3. If the workspace is empty, initialize the project structure.
4. Follow the implementation roadmap.
5. Work in small, verifiable stages.
6. Build the solution after meaningful changes.
7. Run relevant tests before moving to the next phase.
8. Fix build errors and test failures before continuing.
9. Never replace real functionality with placeholder buttons.
10. Never use fake financial calculations.
11. Never hardcode sample financial data into production behavior.
12. Keep the UI consistent with the Astra Finance design system.
13. Keep the application usable without internet access.
14. Avoid unnecessary dependencies.
15. Document important architectural decisions.
16. Preserve existing user data during upgrades and migrations.
17. Do not implement future Telegram functionality in version 1.
18. Do not claim that a feature is complete unless it has been implemented and verified.

### Progress reporting

Maintain a development progress document at:

`docs/IMPLEMENTATION_STATUS.md`

For each phase, record:
- Completed tasks.
- Remaining tasks.
- Build status.
- Test status.
- Known issues.
- Important decisions.

Update this document after each completed phase.

### Important execution behavior

Start by inspecting the workspace and checking the installed .NET SDK.

Then present a short implementation plan and begin Phase 1.

Do not stop after creating a project template or a static UI preview.

Continue implementing the application phase by phase, verifying each stage.

If an important technical decision is genuinely ambiguous, explain the options and ask before making a decision that could affect financial correctness, data safety, or project architecture.

If a third-party library has licensing restrictions, do not silently introduce it. Find an appropriate alternative or explain the limitation.

The final goal is a fully functional, reliable, polished Windows desktop application named **Astra Finance**.

---

**End of Specification**