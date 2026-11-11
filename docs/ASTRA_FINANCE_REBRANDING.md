# Astra Finance --- Visual Rebranding Specification

## Project context

-   **Application:** Astra Finance, an existing Windows desktop
    personal-finance application.
-   **Project path:** `C:\dev\AstraFinance`
-   **Private GitHub repository:**
    `https://github.com/Radhika-Febriansyah/AstraFinance.git`
-   **Current version:** `1.0.0`
-   **Stack:** .NET 10 LTS, WPF, C#, SQLite, EF Core 10, MVVM,
    CommunityToolkit.Mvvm, DI, LiveCharts2, ClosedXML, Inno Setup 6.7.3.
-   The current application is complete. Its source is backed up in the
    private repository.

## 1. Mission and non-negotiable safeguards

Perform a **complete visual rebranding using careful design
refactoring**, not a complete rewrite.

Preserve all existing features, workflows, financial calculations,
business logic, SQLite data, EF Core migrations, PIN security,
backup/restore behavior, offline-first operation, language support, and
theme behavior.

Do not reset, delete, recreate, or migrate the user's database as part
of visual rebranding. Do not weaken security, remove functionality, or
introduce unnecessary dependencies.

Do not push to GitHub, publish a release, or distribute an installer
without explicit user approval. Do not commit personal databases,
financial records, backups, secrets, API keys, or private configuration.
Inspect and preserve `.gitignore`.

Keep the existing `1.0.0` tag and history intact. Inspect the versioning
strategy and recommend a suitable new version (likely `1.0.1`) for the
rebranding.

## 2. Brand identity

**Brand name:** Astra Finance\
**Personality:** minimalist, premium, modern, professional, trustworthy,
elegant.

### Brand palette

  Token        Hex
  ------------ -----------
  Royal Blue   `#2563EB`
  Deep Navy    `#172554`
  White        `#FFFFFF`

Use Royal Blue for primary interactions and highlights, Deep Navy for
dark surfaces and brand backgrounds, and white for contrast and clean
surfaces.

Preserve functional colors: - Green: income and success. - Red:
expenses, errors, destructive warnings. - Amber: attention and
approaching limits. - Blue: information and active states.

Do not introduce unrelated colors into the core logo palette.

## 3. Logo and brand assets

Create an original abstract geometric symbol with: - Symmetrical
composition and a custom freeform silhouette. - One continuous unified
form. - Medium, balanced thickness. - A hybrid of angular geometry and
rounded curves. - A visual idea of financial growth, progress, and
reaching goals. - No literal currency symbols, generic stock-chart
clichés, or forced letter A. - Subtle diagonal gradient from Deep Navy
to Royal Blue. - Flat 2D finish: no bevels, 3D effects, or heavy
shadows.

Wordmark: - "Astra Finance" in modern sans-serif, Medium/Semibold, Title
Case, normal letter spacing. - Deep Navy text on light backgrounds and
white text on dark backgrounds.

Create: - Transparent master logo. - Light and Dark variants. -
Symbol-only version. - Horizontal and vertical symbol/wordmark
lockups. - Rounded-square Windows icon with Deep Navy background and
blue-gradient symbol. - SVG master files and transparent PNG exports. -
ICO containing 16, 32, 48, 64, 128, and 256 px sizes. - PNG exports at
512 and 1024 px.

SVG must remain the editable vector master; do not rely on a
low-resolution raster as the only source.

Organize assets in a clear branding directory. Verify legibility at
small icon sizes.

## 4. Complete rebranding scope

Update all relevant visual identity elements: - Main application window
and title bar. - EXE, taskbar, Start Menu, and optional Desktop shortcut
icons. - Sidebar, header, navigation, dashboard, and all financial
pages. - Shared controls, dialogs, popups, notifications, and loading
states. - PIN screen, splash screen, About page, and Settings. - Windows
installer and uninstaller.

Do not redesign application behavior or remove existing workflows.

## 5. Approved application visual system

### General shell

**Sidebar --- Premium Gradient Sidebar** - Deep Navy base with subtle
blue gradient. - Brand logo and name at the top. - Consistent modern
icons and elegant active indicator. - Expanded mode: icons and labels. -
Collapsed mode: icons only. - Balanced width, smooth transitions,
persisted collapsed/expanded preference. - Support Light and Dark
themes.

**Header --- Premium Header** - Page title, breadcrumbs where useful,
notifications, profile controls, and contextual actions.

**Dashboard --- Premium Dashboard** - Preserve all current information
and functions. - Improve statistic cards, charts, spacing, hierarchy,
and visual balance. - Use restrained Deep Navy/Royal Blue gradients for
suitable statistic cards. - Keep income/expense functional colors.

### Financial pages

Apply these approved designs while preserving existing data,
calculations, and operations:

-   **Transactions --- Premium Transaction Center:** summary cards,
    search/filter toolbar, gradient primary Add Transaction action,
    modern table, category icons, income/expense badges, pagination.
-   **Budget --- Premium Budget Cards:** category icon/name, budget
    limit, amount spent, remaining amount, progress bar,
    approaching-limit warning, exceeded state. Normal = blue,
    approaching = amber, exceeded = red.
-   **Calendar --- Premium Financial Calendar:** monthly calendar,
    income/expense indicators, today highlight, selected-date detail
    panel, daily transaction summary.
-   **Insights --- Premium Financial Insights:** statistic cards,
    interactive charts, spending distribution, financial trends,
    summaries, period filters.
-   **Accounts --- Premium Account Cards:** account name, type, current
    balance, icon, identity color, view/edit/delete actions.
-   **Goals --- Premium Goal Cards:** icon/name, target amount,
    accumulated amount, remaining amount, progress percentage/bar,
    deadline, status.
-   **Financial Overview --- Premium Financial Overview:** total assets
    and account balances, income/expenses, budget status, goal progress,
    recent activity. Avoid unnecessary duplication or overcrowding of
    Dashboard.
-   **Reports --- Premium Analytics Layout:** summary cards, interactive
    charts, structured tables, period filters, income/expense summaries,
    existing export behavior.
-   **Categories and Accounts management:** category cards with
    icon/name/color/supporting information; clear account management
    presentation and premium add/edit/delete interactions.
-   **Backup & Restore --- Premium Backup Center:** backup status, last
    backup time, storage location, prominent Backup/Restore actions,
    history table, status indicators, explicit restore confirmation.
    Preserve existing safety behavior.

## 6. Shared components and interaction design

Build a reusable design system rather than duplicating page-specific
styles.

-   **Primary buttons:** selective Deep Navy-to-Royal Blue gradient,
    white Medium/Semibold text, rounded corners, elegant hover/pressed
    states. Secondary buttons stay simpler.
-   **Inputs:** rounded corners, adaptive surface, subtle border, Royal
    Blue focus, clear labels/placeholders/validation.
-   **Dropdowns:** floating rounded panel, soft shadow, short animation,
    Royal Blue active selection.
-   **Tables:** elegant headers, comfortable row spacing, subtle hover,
    aligned numbers, consistent typography and status badges.
-   **Badges:** soft backgrounds; green success, red expense/error,
    amber warning/pending, blue information/active.
-   **Calendar/date picker:** premium floating rounded panel, month
    navigation, Royal Blue selected date, visible today highlight,
    Light/Dark support.
-   **Dialogs:** modern floating solid panels, rounded corners, soft
    shadows, smooth entry/exit, clear actions. Confirmation strength
    must match risk; destructive actions clearly explain consequences.
-   **Settings:** compact category sidebar, separate setting cards,
    icons/descriptions, consistent custom controls.
-   **About:** premium brand showcase with large logo, name, version,
    short description, developer information; avoid cluttering it with
    technical details.

## 7. Splash, PIN, notifications, and motion

**Splash screen --- Elegant Dark** - Deep Navy background, large symbol,
product name, subtle geometric details, gentle fade-in.

**PIN screen --- Premium Glass** - Deep Navy background, translucent
glassmorphism panel, soft transparency, blue accents, rounded-square PIN
buttons, white numerals, subtle hover/pressed states. - Preserve
existing PIN logic and security exactly.

**Notifications --- Premium Floating Card** - Rounded card, subtle
transparency, icon, soft animation, Light/Dark support. - Position at
top-right inside the application window, stack consistently, allow
dismissal. - Smart duration based on message length/type; pause while
hovered. - Important/actionable/critical notices remain until handled
when appropriate. - Use functional status colors.

**Micro-interactions** - Subtle
button/card/sidebar/dialog/dropdown/notification feedback and loading
indicators. - Balanced, brief, consistent animations. - Respect Windows
Reduce Motion where feasible. - Avoid animation that harms performance
or usability.

## 8. Optional UI sounds

Add subtle, optional short electronic tones only for selected events
such as PIN success/error, important notifications, and successful
transaction saving.

Settings must provide: - Master sound toggle. - Overall volume. -
Per-category controls. - Preview for each sound. - Persistent
preferences.

No continuous background audio. The master toggle must disable all UI
sounds.

## 9. Language, themes, accessibility, and responsive behavior

Maintain Indonesian and English. All new user-facing text must be
localized; do not hardcode new strings in only one language. Brand
name/logo remain unchanged. Splash, PIN, dialogs, notifications,
settings, installer, and uninstaller should follow the available app
language; use the defined default where language is not yet available.

Maintain Light Mode, Dark Mode, and Follow System. Every new component
must support both themes.

Use an **Adaptive Premium Layout**: - Cards and grids adapt to available
width. - Sidebar remains balanced. - Tables remain readable. -
Dialogs/dropdowns stay within the visible window. - Resizing must not
break layout or hide functionality. - Support Windows DPI scaling.

Meet **High-End Product Experience** standards: - Keyboard navigation
and visible focus. - Appropriate contrast and readable typography. -
Precise alignment and consistent spacing. - Complete hover, pressed,
loading, empty, success, and error states. - Reduced-motion support
where feasible. - Do not sacrifice usability for visual effects.

## 10. Installer and uninstaller

Use the existing Inno Setup configuration where possible.

Create a branded Windows Setup.exe and Windows-integrated uninstaller.

**Banner --- Minimalist Corporate** - Deep Navy background. -
Blue-gradient symbol. - White "Astra Finance" wordmark. - Horizontal
layout: symbol left, text right. - No tagline. - Use the same banner for
installer and uninstaller.

Apply the app icon and branding to both experiences. Preserve Start Menu
shortcut and optional Desktop shortcut behavior.

**Critical data protection:** uninstall must preserve the SQLite
database, financial records, settings, and backups by default. Never
silently delete personal data. Any option to delete personal data must
be separate, explicit, and clearly explain its consequences. Verify
upgrade/uninstall behavior without risking user data.

## 11. Architecture and refactoring boundaries

Use **Careful Design Refactoring**: - Improve shared design resources
and reusable UI components. - Refactor presentation structure where
necessary for visual consistency. - Keep MVVM, DI, and separation
between presentation, application logic, and data access. - Do not put
business logic into visual components. - Do not change financial logic,
database schema, migrations, PIN security, or data handling for cosmetic
reasons. - Avoid broad rewrites and unnecessary dependencies.

## 12. Required workflow

### Phase 1 --- Audit (start here)

Inspect: - Git status, branches, tags, and current versioning. -
Solution/project structure and build configuration. - Existing views,
shared controls, themes, and resources. - All financial pages and
navigation. - Branding assets and icon usage. - Installer/uninstaller
scripts and data paths. - `.gitignore` and sensitive-data protections. -
Risks to database, user data, and existing behavior.

**Before implementation, report the audit findings, risks, recommended
branch/version, and a phased execution plan. Stop and wait for user
approval.**

### Phase 2 --- Design system and assets

Establish tokens/resources, logo variants, reusable component styles,
Light/Dark resources, app icon, and installer assets.

### Phase 3 --- Application shell

Update application branding, sidebar, header, navigation, shared
controls/dialogs, splash, and PIN presentation.

### Phase 4 --- Financial pages

Apply the approved designs to all pages listed above. Preserve all
existing functionality.

### Phase 5 --- Notifications, interactions, and audio

Implement notification presentation, micro-interactions, and optional
sound settings.

### Phase 6 --- Installer and uninstaller

Apply branding and verify safe installation, upgrade, and uninstall
behavior.

### Phase 7 --- Quality assurance

Test build/startup, existing database compatibility and data
preservation, PIN, transactions/calculations, backup/restore,
reports/exports, Indonesian/English, Light/Dark/Follow System, resizing,
DPI scaling, keyboard navigation, installer/uninstaller, and all icons.

Fix regressions introduced by rebranding. Report test results and any
remaining limitations.

### Phase 8 --- Versioning and review

Recommend a new version based on the existing strategy (likely `1.0.1`).
Preserve the `1.0.0` tag and history. Present changed files, assets,
test results, limitations, and next steps.

Do not push, publish, or release until the user explicitly approves.

## Final acceptance criteria

Deliver a cohesive, polished, premium Astra Finance experience with: -
Original, usable brand assets and vector masters. - Consistent design
system across all pages. - Functional Light/Dark/Follow System themes. -
Adaptive Windows layout and accessible interactions. - Branded
application, installer, and uninstaller. - Optional controllable UI
sounds. - All original features, security, calculations, and user data
preserved.

**Visual quality matters. Application reliability and user-data safety
are non-negotiable. Start with Phase 1 --- Audit only, report findings,
and wait for approval before implementing.**
