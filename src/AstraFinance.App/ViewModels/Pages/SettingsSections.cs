using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Security;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

// =====================================================================================================
// Categories
// =====================================================================================================

/// <summary>A category in Settings → Categories.</summary>
public sealed record CategoryRow(Category Category, string Name, bool IsBuiltIn, bool CanDeactivate)
{
    public Guid Id => Category.Id;

    public string IconKey => Category.IconKey;

    public string ColorHex => Category.ColorHex;

    public bool IsActive => Category.IsActive;
}

/// <summary>Income and expense categories: add, rename/restyle, deactivate and reactivate.</summary>
public sealed partial class CategoriesSettingsViewModel : ObservableObject
{
    private readonly ICategoryService _categories;
    private readonly ILocalizationService _localization;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly DataChangeNotifier _changes;
    private readonly Func<CategoryFormViewModel> _formFactory;

    public CategoriesSettingsViewModel(
        ICategoryService categories,
        ILocalizationService localization,
        IDialogService dialogs,
        INotificationService notifications,
        DataChangeNotifier changes,
        Func<CategoryFormViewModel> formFactory)
    {
        _categories = categories;
        _localization = localization;
        _dialogs = dialogs;
        _notifications = notifications;
        _changes = changes;
        _formFactory = formFactory;
    }

    public ObservableCollection<CategoryRow> Expense { get; } = [];

    public ObservableCollection<CategoryRow> Income { get; } = [];

    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    public async Task LoadAsync()
    {
        var all = await _categories.ListAsync(includeInactive: true).ConfigureAwait(true);
        Fill(Expense, all.Where(c => c.Kind == CategoryKind.Expense));
        Fill(Income, all.Where(c => c.Kind == CategoryKind.Income));
    }

    [RelayCommand]
    private Task AddExpenseAsync() => AddAsync(CategoryKind.Expense);

    [RelayCommand]
    private Task AddIncomeAsync() => AddAsync(CategoryKind.Income);

    [RelayCommand]
    private async Task EditAsync(CategoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        var form = _formFactory();
        form.Load(row.Category, row.Name);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(CategoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsActive)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                _localization.GetString("Settings.Categories.Deactivate.Title"),
                _localization.Format("Settings.Categories.Deactivate.Body", row.Name),
                _localization.GetString("Settings.Categories.Deactivate")).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            await _categories.SetActiveAsync(row.Id, !row.IsActive).ConfigureAwait(true);
        }
        catch (FinanceValidationException ex)
        {
            await _dialogs.AlertAsync(NotificationKind.Warning, _localization.GetString("Settings.Section.Categories"),
                _localization.GetString($"Validation.{ex.Errors[0].Code}")).ConfigureAwait(true);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    partial void OnShowInactiveChanged(bool value) => _ = LoadAsync();

    private async Task AddAsync(CategoryKind kind)
    {
        var form = _formFactory();
        form.New(kind);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, _localization.GetString("Settings.Categories.Saved"), form.Saved!.Name);
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task ReloadAsync()
    {
        await LoadAsync().ConfigureAwait(true);
        _changes.Notify(DataChangeKind.Transactions); // category names and icons appear on other pages
    }

    private void Fill(ObservableCollection<CategoryRow> target, IEnumerable<Category> source)
    {
        target.Clear();
        foreach (var c in source.Where(c => ShowInactive || c.IsActive).OrderBy(c => c.IsActive ? 0 : 1).ThenBy(c => c.SortOrder))
        {
            target.Add(new CategoryRow(c, DefaultCategories.DisplayName(c, _localization), c.SystemKey is not null, c.SystemKey != DefaultCategories.FeesKey));
        }
    }
}

/// <summary>Create a custom category or rename/restyle an existing one (the kind never changes).</summary>
public sealed partial class CategoryFormViewModel : ValidatableViewModel, IFormDialog
{
    public static readonly IReadOnlyList<string> CategoryIcons =
    [
        "Icon.Utensils", "Icon.Car", "Icon.Film", "Icon.ShoppingBag", "Icon.Zap", "Icon.HeartPulse", "Icon.GraduationCap",
        "Icon.Home", "Icon.Receipt", "Icon.Briefcase", "Icon.Store", "Icon.TrendingUp", "Icon.Gift", "Icon.Smartphone",
        "Icon.PiggyBank", "Icon.CreditCard", "Icon.Banknote", "Icon.MoreHorizontal",
    ];

    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal) { ["Name"] = nameof(Name) };

    private readonly ICategoryService _categories;
    private readonly ILocalizationService _localization;
    private Category? _existing;
    private string _originalName = string.Empty;

    public CategoryFormViewModel(ICategoryService categories, ILocalizationService localization)
    {
        _categories = categories;
        _localization = localization;
        SelectedIcon = CategoryIcons[0];
        SelectedColor = AccountDefaults.Colors[0];
    }

    public IReadOnlyList<string> IconOptions { get; } = CategoryIcons;

    public IReadOnlyList<string> ColorOptions { get; } = AccountDefaults.Colors;

    public CategoryKind Kind { get; private set; } = CategoryKind.Expense;

    public string Title => _localization.GetString(_existing is null
        ? Kind == CategoryKind.Income ? "Settings.Categories.AddIncome" : "Settings.Categories.AddExpense"
        : "Settings.Categories.Edit");

    public string PrimaryText => _localization.GetString("Common.Save");

    public bool IsDestructive => false;

    public bool IsBuiltIn => _existing?.SystemKey is not null;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedIcon { get; set; }

    [ObservableProperty]
    public partial string SelectedColor { get; set; }

    public Category? Saved { get; private set; }

    public void New(CategoryKind kind)
    {
        Kind = kind;
        _existing = null;
        OnPropertyChanged(nameof(Title));
    }

    public void Load(Category category, string displayName)
    {
        ArgumentNullException.ThrowIfNull(category);
        _existing = category;
        Kind = category.Kind;
        Name = displayName;
        _originalName = displayName;
        SelectedIcon = category.IconKey;
        SelectedColor = category.ColorHex;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsBuiltIn));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (string.IsNullOrWhiteSpace(Name))
        {
            SetError(nameof(Name), _localization.GetString("Validation.Required"));
            return false;
        }

        try
        {
            if (_existing is null)
            {
                Saved = await _categories.CreateAsync(new CategoryDraft(Name.Trim(), Kind, SelectedIcon, SelectedColor)).ConfigureAwait(true);
            }
            else
            {
                // An unchanged built-in name stays translated; a new name becomes the category's own name.
                var name = string.Equals(Name.Trim(), _originalName, StringComparison.Ordinal) ? null : Name.Trim();
                Saved = await _categories.UpdateAsync(_existing.Id, name, SelectedIcon, SelectedColor).ConfigureAwait(true);
            }

            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnNameChanged(string value) => SetError(nameof(Name), null);
}

// =====================================================================================================
// Security
// =====================================================================================================

/// <summary>Change the PIN, lock now, and choose the failed-attempt cooldown.</summary>
public sealed partial class SecuritySettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<int> AttemptChoices = [3, 5, 10];

    public static readonly IReadOnlyList<int> CooldownSecondChoices = [30, 60, 300, 900];

    private readonly IPinSecuritySettings _settings;
    private readonly ILocalizationService _localization;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly AppSession _session;
    private readonly Func<ChangePinFormViewModel> _changePinFactory;
    private bool _loading;

    public SecuritySettingsViewModel(
        IPinSecuritySettings settings,
        ILocalizationService localization,
        IDialogService dialogs,
        INotificationService notifications,
        AppSession session,
        Func<ChangePinFormViewModel> changePinFactory)
    {
        _settings = settings;
        _localization = localization;
        _dialogs = dialogs;
        _notifications = notifications;
        _session = session;
        _changePinFactory = changePinFactory;
        BuildOptions();
    }

    [ObservableProperty]
    public partial IReadOnlyList<SelectOption<int>> AttemptOptions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SelectOption<int>> CooldownOptions { get; private set; } = [];

    [ObservableProperty]
    public partial SelectOption<int>? SelectedAttempts { get; set; }

    [ObservableProperty]
    public partial SelectOption<int>? SelectedCooldown { get; set; }

    [ObservableProperty]
    public partial string CooldownSummary { get; private set; } = string.Empty;

    /// <summary>The latest save (tests await it).</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    public async Task LoadAsync()
    {
        var s = await _settings.GetAttemptSettingsAsync().ConfigureAwait(true);
        _loading = true;
        SelectedAttempts = AttemptOptions.FirstOrDefault(o => o.Value == s.FreeAttempts) ?? AttemptOptions[1];
        SelectedCooldown = CooldownOptions.FirstOrDefault(o => o.Value == (int)s.BaseCooldown.TotalSeconds) ?? CooldownOptions[0];
        _loading = false;
        UpdateSummary();
    }

    public void RefreshLanguage()
    {
        var attempts = SelectedAttempts?.Value;
        var cooldown = SelectedCooldown?.Value;
        BuildOptions();
        _loading = true;
        SelectedAttempts = AttemptOptions.FirstOrDefault(o => o.Value == attempts);
        SelectedCooldown = CooldownOptions.FirstOrDefault(o => o.Value == cooldown);
        _loading = false;
        UpdateSummary();
    }

    [RelayCommand]
    private async Task ChangePinAsync()
    {
        if (await _dialogs.ShowFormAsync(_changePinFactory()).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, _localization.GetString("Settings.Security.PinChanged"), string.Empty);
        }
    }

    [RelayCommand]
    private Task LockNowAsync() => _session.LockAsync();

    partial void OnSelectedAttemptsChanged(SelectOption<int>? value) => Save();

    partial void OnSelectedCooldownChanged(SelectOption<int>? value) => Save();

    private void Save()
    {
        if (_loading || SelectedAttempts is null || SelectedCooldown is null)
        {
            return;
        }

        UpdateSummary();
        LastSave = _settings.SaveAttemptSettingsAsync(new PinAttemptSettings(SelectedAttempts.Value, TimeSpan.FromSeconds(SelectedCooldown.Value)));
    }

    private void UpdateSummary()
    {
        if (SelectedAttempts is null || SelectedCooldown is null)
        {
            return;
        }

        var settings = new PinAttemptSettings(SelectedAttempts.Value, TimeSpan.FromSeconds(SelectedCooldown.Value));
        CooldownSummary = _localization.Format("Settings.Security.Cooldown.Summary", settings.FreeAttempts, Duration(settings.BaseCooldown), Duration(settings.MaxCooldown));
    }

    private void BuildOptions()
    {
        AttemptOptions = AttemptChoices.Select(n => new SelectOption<int>(n, _localization.Format("Settings.Security.Attempts.Option", n))).ToList();
        CooldownOptions = CooldownSecondChoices.Select(s => new SelectOption<int>(s, Duration(TimeSpan.FromSeconds(s)))).ToList();
    }

    private string Duration(TimeSpan span) => span.TotalMinutes >= 1
        ? _localization.Format("Settings.Security.Minutes", (int)span.TotalMinutes)
        : _localization.Format("Settings.Security.Seconds", (int)span.TotalSeconds);
}

/// <summary>Change the PIN: the current PIN is checked (and counts as an attempt) before the new one is saved.</summary>
public sealed partial class ChangePinFormViewModel : ValidatableViewModel, IFormDialog
{
    private readonly IPinService _pin;
    private readonly ILocalizationService _localization;

    public ChangePinFormViewModel(IPinService pin, ILocalizationService localization)
    {
        _pin = pin;
        _localization = localization;
    }

    public string Title => _localization.GetString("Settings.Security.ChangePin");

    public string PrimaryText => _localization.GetString("Settings.Security.ChangePin.Confirm");

    public bool IsDestructive => false;

    [ObservableProperty]
    public partial string CurrentPin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPin { get; set; } = string.Empty;

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (CurrentPin.Length == 0)
        {
            SetError(nameof(CurrentPin), _localization.GetString("Validation.Required"));
        }

        if (!PinRules.IsValidFormat(NewPin))
        {
            SetError(nameof(NewPin), _localization.GetString("Validation.PinFormat"));
        }
        else if (NewPin != ConfirmPin)
        {
            SetError(nameof(ConfirmPin), _localization.GetString("Validation.PinMismatch"));
        }

        if (HasErrors)
        {
            return false;
        }

        var result = await _pin.ChangePinAsync(CurrentPin, NewPin).ConfigureAwait(true);
        switch (result.Outcome)
        {
            case PinCheckOutcome.Success:
                return true;
            case PinCheckOutcome.CoolingDown:
                SetError(nameof(CurrentPin), _localization.Format("Settings.Security.ChangePin.Cooldown", result.RetryAt?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
                return false;
            default:
                CurrentPin = string.Empty; // cleared before the message is set (changing it clears the error)
                SetError(nameof(CurrentPin), result.RetryAt is { } retry
                    ? _localization.Format("Settings.Security.ChangePin.Cooldown", retry.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                    : _localization.Format("Settings.Security.ChangePin.Wrong", result.RemainingFreeAttempts));
                return false;
        }
    }

    partial void OnCurrentPinChanged(string value) => SetError(nameof(CurrentPin), null);

    partial void OnNewPinChanged(string value) => SetError(nameof(NewPin), null);

    partial void OnConfirmPinChanged(string value) => SetError(nameof(ConfirmPin), null);
}

// =====================================================================================================
// Data management
// =====================================================================================================

/// <summary>A backup in the history list.</summary>
public sealed record BackupRow(string Path, string FileName, string DateText, string KindText, string SizeText);

/// <summary>How well protected the data is, for the Backup Center status badge.</summary>
public enum BackupHealth
{
    /// <summary>No backup has been made yet.</summary>
    None = 0,

    /// <summary>The last backup is recent enough for the schedule.</summary>
    Current = 1,

    /// <summary>The last backup is older than the schedule expects.</summary>
    Stale = 2,
}

/// <summary>Backups (settings, history, manual backup, restore) and data import/export.</summary>
public sealed partial class DataSettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<int> RetentionChoices = [5, 10, 20, 50];

    private readonly IBackupService _backup;
    private readonly IRestoreService _restore;
    private readonly IDataExportService _export;
    private readonly ITransactionImportService _import;
    private readonly ILocalizationService _localization;
    private readonly IRegionalFormatService _format;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IFolderPicker _folderPicker;
    private readonly IFileOpenPicker _openPicker;
    private readonly IFileSavePicker _savePicker;
    private readonly DataChangeNotifier _changes;
    private readonly AppSession _session;
    private readonly IClock _clock;
    private readonly Func<ImportFormViewModel> _importFactory;
    private bool _loading;

    public DataSettingsViewModel(
        IBackupService backup,
        IRestoreService restore,
        IDataExportService export,
        ITransactionImportService import,
        ILocalizationService localization,
        IRegionalFormatService format,
        IDialogService dialogs,
        INotificationService notifications,
        IFolderPicker folderPicker,
        IFileOpenPicker openPicker,
        IFileSavePicker savePicker,
        DataChangeNotifier changes,
        AppSession session,
        IClock clock,
        Func<ImportFormViewModel> importFactory)
    {
        _backup = backup;
        _restore = restore;
        _export = export;
        _import = import;
        _localization = localization;
        _format = format;
        _dialogs = dialogs;
        _notifications = notifications;
        _folderPicker = folderPicker;
        _openPicker = openPicker;
        _savePicker = savePicker;
        _changes = changes;
        _session = session;
        _clock = clock;
        _importFactory = importFactory;
        ScheduleOptions = Enum.GetValues<BackupSchedule>()
            .Select(s => new ChoiceOption<BackupSchedule>(s, () => _localization.GetString($"BackupSchedule.{s}"), null, v => Schedule = v))
            .ToList();
        RetentionOptions = RetentionChoices.Select(n => new SelectOption<int>(n, n.ToString(CultureInfo.InvariantCulture))).ToList();
    }

    public IReadOnlyList<ChoiceOption<BackupSchedule>> ScheduleOptions { get; }

    public IReadOnlyList<SelectOption<int>> RetentionOptions { get; }

    [ObservableProperty]
    public partial string BackupDirectory { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial BackupSchedule Schedule { get; private set; }

    [ObservableProperty]
    public partial SelectOption<int>? SelectedRetention { get; set; }

    [ObservableProperty]
    public partial string LastBackupText { get; private set; } = string.Empty;

    public ObservableCollection<BackupRow> History { get; } = [];

    [ObservableProperty]
    public partial bool HasNoHistory { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackUpNowCommand), nameof(ExportJsonCommand), nameof(ImportCsvCommand), nameof(RestoreCommand), nameof(RestoreFromFileCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>The latest settings save (tests await it).</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var settings = await _backup.GetSettingsAsync().ConfigureAwait(true);
            BackupDirectory = settings.Directory;
            Schedule = settings.Schedule;
            foreach (var option in ScheduleOptions)
            {
                option.SetSelectedSilently(option.Value == settings.Schedule);
            }

            SelectedRetention = RetentionOptions.FirstOrDefault(o => o.Value == settings.RetentionCount)
                                ?? RetentionOptions.OrderBy(o => Math.Abs(o.Value - settings.RetentionCount)).First();
        }
        finally
        {
            _loading = false;
        }

        await LoadHistoryAsync().ConfigureAwait(true);
    }

    public void RefreshLanguage()
    {
        foreach (var option in ScheduleOptions)
        {
            option.RefreshText();
        }

        _ = LoadHistoryAsync();
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand]
    private void ChangeFolder()
    {
        var folder = _folderPicker.PickFolder(_localization.GetString("Settings.Data.ChooseFolder"), BackupDirectory);
        if (folder is null)
        {
            return;
        }

        if (!_backup.IsWritableDirectory(folder))
        {
            _ = _dialogs.AlertAsync(NotificationKind.Warning, _localization.GetString("Settings.Data.Folder"), _localization.GetString("Validation.DirectoryNotWritable"));
            return;
        }

        BackupDirectory = folder;
        SaveSettings();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Directory.Exists(BackupDirectory))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupDirectory}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task BackUpNowAsync()
    {
        IsBusy = true;
        try
        {
            var created = await _backup.CreateBackupAsync(isAutomatic: false).ConfigureAwait(true);
            _notifications.Notify(NotificationKind.Success, _localization.GetString("Backup.Created.Title"), created.Path);
            await LoadHistoryAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            _notifications.Notify(NotificationKind.Error, _localization.GetString("Backup.Failed.Title"), _localization.GetString("Backup.Failed.Body"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RestoreAsync(BackupRow? row) => row is null ? Task.CompletedTask : ConfirmAndRestoreAsync(row.Path);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RestoreFromFileAsync()
    {
        var path = _openPicker.PickOpenFile(_localization.GetString("Settings.Data.Restore.Pick"), _localization.GetString("Settings.Data.Restore.Filter"), BackupDirectory);
        return path is null ? Task.CompletedTask : ConfirmAndRestoreAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ExportJsonAsync()
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"astra-finance-data-{_clock.Today:yyyyMMdd}.json");
        var path = _savePicker.PickSaveFile(_localization.GetString("Settings.Data.Export.Title"), name, _localization.GetString("Settings.Data.Export.Filter"));
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var count = await _export.ExportJsonAsync(path).ConfigureAwait(true);
            _notifications.Notify(NotificationKind.Success, _localization.Format("Settings.Data.Export.Done", count), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Notify(NotificationKind.Error, _localization.GetString("Transactions.Export.Failed"), _localization.GetString("Transactions.Export.FailedBody"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ImportCsvAsync()
    {
        var path = _openPicker.PickOpenFile(_localization.GetString("Settings.Data.Import.Pick"), _localization.GetString("Reports.Filter.Csv"), null);
        if (path is null)
        {
            return;
        }

        CsvTable table;
        try
        {
            table = await _import.ReadAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImportFailedException)
        {
            await _dialogs.AlertAsync(NotificationKind.Error, _localization.GetString("Settings.Data.Import.Title"), _localization.GetString("Settings.Data.Import.ReadFailed")).ConfigureAwait(true);
            return;
        }

        if (table.Headers.Count < 2 || table.Rows.Count == 0)
        {
            await _dialogs.AlertAsync(NotificationKind.Warning, _localization.GetString("Settings.Data.Import.Title"), _localization.GetString("Settings.Data.Import.Empty")).ConfigureAwait(true);
            return;
        }

        var form = _importFactory();
        if (!await form.InitializeAsync(Path.GetFileName(path), table).ConfigureAwait(true))
        {
            await _dialogs.AlertAsync(NotificationKind.Info, _localization.GetString("Settings.Data.Import.Title"), _localization.GetString("Bills.NoAccounts")).ConfigureAwait(true);
            return;
        }

        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, _localization.Format("Settings.Data.Import.Done", form.ImportedCount), form.AccountName);
            _changes.Notify(DataChangeKind.Transactions);
        }
    }

    partial void OnScheduleChanged(BackupSchedule value) => SaveSettings();

    partial void OnSelectedRetentionChanged(SelectOption<int>? value) => SaveSettings();

    private void SaveSettings()
    {
        if (_loading || SelectedRetention is null)
        {
            return;
        }

        LastSave = _backup.SaveSettingsAsync(new BackupSettings(BackupDirectory, Schedule, SelectedRetention.Value));
        _ = LastSave.ContinueWith(_ => LoadHistoryAsync(), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task LoadHistoryAsync()
    {
        var last = await _backup.GetLastBackupAsync().ConfigureAwait(true);
        LastBackupText = last is { } l
            ? _localization.Format("Settings.Data.LastBackup", FormatTime(l))
            : _localization.GetString("Settings.Data.NoBackupYet");
        Health = EvaluateHealth(last, Schedule, _clock.UtcNow);
        HealthText = _localization.GetString($"Settings.Data.Health.{Health}");
        History.Clear();
        foreach (var entry in await _restore.ListBackupsAsync().ConfigureAwait(true))
        {
            History.Add(new BackupRow(
                entry.Path,
                entry.FileName,
                FormatTime(entry.CreatedAt),
                _localization.GetString($"BackupKind.{entry.Kind}"),
                string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, entry.SizeBytes / 1024):N0} KB")));
        }

        HasNoHistory = History.Count == 0;
    }

    [ObservableProperty]
    public partial BackupHealth Health { get; private set; }

    [ObservableProperty]
    public partial string HealthText { get; private set; } = string.Empty;

    /// <summary>
    /// Current while the last backup is within the schedule's interval (plus a day of slack), or 30 days
    /// when automatic backups are off; Stale when older; None without any backup.
    /// </summary>
    internal static BackupHealth EvaluateHealth(DateTimeOffset? lastBackup, BackupSchedule schedule, DateTimeOffset now)
    {
        if (lastBackup is not { } last)
        {
            return BackupHealth.None;
        }

        var allowed = schedule switch
        {
            BackupSchedule.Daily => TimeSpan.FromDays(2),
            BackupSchedule.Weekly => TimeSpan.FromDays(8),
            BackupSchedule.Monthly => TimeSpan.FromDays(32),
            _ => TimeSpan.FromDays(30),
        };
        return now - last <= allowed ? BackupHealth.Current : BackupHealth.Stale;
    }

    private string FormatTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return $"{_format.FormatDate(DateOnly.FromDateTime(local.DateTime))} {local:HH:mm}";
    }

    private async Task ConfirmAndRestoreAsync(string path)
    {
        var inspection = await _restore.InspectAsync(path).ConfigureAwait(true);
        if (!inspection.IsValid)
        {
            await _dialogs.AlertAsync(NotificationKind.Warning, _localization.GetString("Settings.Data.Restore.Title"),
                _localization.GetString($"Restore.Problem.{inspection.ProblemCode}")).ConfigureAwait(true);
            return;
        }

        var details = _localization.Format(
            "Settings.Data.Restore.Body",
            Path.GetFileName(path),
            inspection.AccountCount,
            inspection.TransactionCount,
            inspection.LastTransactionDate is { } d ? _format.FormatDate(d) : "—");
        var confirmed = await _dialogs.ConfirmAsync(
            _localization.GetString("Settings.Data.Restore.Title"),
            details,
            _localization.GetString("Settings.Data.Restore.Confirm"),
            isDestructive: true).ConfigureAwait(true);
        if (confirmed)
        {
            await _session.RestoreAsync(path).ConfigureAwait(true);
        }
    }
}

// =====================================================================================================
// CSV import dialog
// =====================================================================================================

/// <summary>One row of the import preview.</summary>
public sealed record ImportPreviewRow(int Line, string Status, ImportRowStatus Kind, string DateText, string Description, string CategoryText, string AmountText, string? Message);

/// <summary>Column mapping, options and preview for a CSV import; nothing is saved before the user confirms.</summary>
public sealed partial class ImportFormViewModel : ValidatableViewModel, IFormDialog
{
    public const int MaxPreviewRows = 200;

    private readonly ITransactionImportService _import;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ILocalizationService _localization;
    private readonly IRegionalFormatService _format;
    private Dictionary<Guid, string> _categoryNames = [];
    private CsvTable? _table;
    private ImportPreview? _preview;
    private bool _initializing;
    private int _previewVersion;

    public ImportFormViewModel(ITransactionImportService import, IAccountService accounts, ICategoryService categories, ILocalizationService localization, IRegionalFormatService format)
    {
        _import = import;
        _accounts = accounts;
        _categories = categories;
        _localization = localization;
        _format = format;
        var sample = new DateOnly(2026, 11, 30);
        DateStyleOptions = Enum.GetValues<DateFormatStyle>()
            .Select(s => new SelectOption<DateFormatStyle>(s, new Core.Money.RegionalFormat(format.Current.Currency, format.Current.NumberStyle, s).FormatDate(sample)))
            .ToList();
        NumberStyleOptions =
        [
            new(ImportNumberStyle.Plain, localization.GetString("Settings.Data.Import.Number.Plain")),
            new(ImportNumberStyle.Regional, localization.Format("Settings.Data.Import.Number.Regional", Core.Money.MoneyFormatter.Format(150_000_050, format.Current, includeSymbol: false))),
        ];
    }

    public string Title => _localization.GetString("Settings.Data.Import.Title");

    public string PrimaryText => _localization.Format("Settings.Data.Import.Confirm", RowsToImport);

    public bool IsDestructive => false;

    public string FileName { get; private set; } = string.Empty;

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    public ObservableCollection<SelectOption<int?>> Columns { get; } = [];

    public ObservableCollection<SelectOption<int?>> OptionalColumns { get; } = [];

    public IReadOnlyList<SelectOption<DateFormatStyle>> DateStyleOptions { get; }

    public IReadOnlyList<SelectOption<ImportNumberStyle>> NumberStyleOptions { get; }

    [ObservableProperty]
    public partial AccountOption? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial SelectOption<int?>? DateColumn { get; set; }

    [ObservableProperty]
    public partial SelectOption<int?>? AmountColumn { get; set; }

    [ObservableProperty]
    public partial SelectOption<int?>? DescriptionColumn { get; set; }

    [ObservableProperty]
    public partial SelectOption<int?>? CategoryColumn { get; set; }

    [ObservableProperty]
    public partial SelectOption<int?>? TypeColumn { get; set; }

    [ObservableProperty]
    public partial SelectOption<DateFormatStyle>? DateStyle { get; set; }

    [ObservableProperty]
    public partial SelectOption<ImportNumberStyle>? NumberStyle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(RowsToImport))]
    public partial bool IncludeDuplicates { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(RowsToImport))]
    public partial string SummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ImportPreviewRow> PreviewRows { get; private set; } = [];

    [ObservableProperty]
    public partial string? MoreRowsText { get; private set; }

    public int RowsToImport => _preview is null ? 0 : _preview.ReadyCount + (IncludeDuplicates ? _preview.DuplicateCount : 0);

    public int ImportedCount { get; private set; }

    public string AccountName => SelectedAccount?.Name ?? string.Empty;

    /// <summary>The preview currently shown (tests await <see cref="RefreshPreviewAsync"/>).</summary>
    public ImportPreview? Preview => _preview;

    /// <summary>Sets up the dialog for a file; returns false when there is no active account to import into.</summary>
    public async Task<bool> InitializeAsync(string fileName, CsvTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _initializing = true;
        _table = table;
        FileName = fileName;
        Accounts.Clear();
        foreach (var a in await _accounts.ListAsync().ConfigureAwait(true))
        {
            Accounts.Add(new AccountOption(a.Account.Id, a.Account.Name, a.Account.IconKey));
        }

        _categoryNames = (await _categories.ListAsync(includeInactive: true).ConfigureAwait(true))
            .ToDictionary(c => c.Id, c => DefaultCategories.DisplayName(c, _localization));
        Columns.Clear();
        OptionalColumns.Clear();
        OptionalColumns.Add(new SelectOption<int?>(null, _localization.GetString("Settings.Data.Import.NoColumn")));
        for (var i = 0; i < table.Headers.Count; i++)
        {
            var label = string.IsNullOrWhiteSpace(table.Headers[i]) ? _localization.Format("Settings.Data.Import.Column", i + 1) : table.Headers[i];
            Columns.Add(new SelectOption<int?>(i, label));
            OptionalColumns.Add(new SelectOption<int?>(i, label));
        }

        var guess = _import.GuessMapping(table.Headers);
        DateColumn = Columns.FirstOrDefault(c => c.Value == guess.DateColumn);
        AmountColumn = Columns.FirstOrDefault(c => c.Value == guess.AmountColumn);
        DescriptionColumn = OptionalColumns.First(c => c.Value == guess.DescriptionColumn);
        CategoryColumn = OptionalColumns.First(c => c.Value == guess.CategoryColumn);
        TypeColumn = OptionalColumns.First(c => c.Value == guess.TypeColumn);
        _accountColumn = guess.AccountColumn;
        _notesColumn = guess.NotesColumn;

        // Astra's own export → plain numbers and its date format; otherwise the app's regional settings.
        var plain = table.Rows.Take(20).All(r => guess.AmountColumn < r.Count && System.Text.RegularExpressions.Regex.IsMatch(r[guess.AmountColumn].Trim(), @"^[+-]?\d+(\.\d+)?$"));
        NumberStyle = NumberStyleOptions.First(o => o.Value == (plain ? ImportNumberStyle.Plain : ImportNumberStyle.Regional));
        DateStyle = DateStyleOptions.First(o => o.Value == _format.Current.DateStyle);
        SelectedAccount = Accounts.FirstOrDefault();
        _initializing = false;
        OnPropertyChanged(nameof(FileName));
        await RefreshPreviewAsync().ConfigureAwait(true);
        return Accounts.Count > 0;
    }

    private int? _accountColumn;
    private int? _notesColumn;

    public async Task RefreshPreviewAsync()
    {
        if (_initializing || _table is null || SelectedAccount is null || DateColumn?.Value is null || AmountColumn?.Value is null || DateStyle is null || NumberStyle is null)
        {
            return;
        }

        var version = ++_previewVersion;
        var mapping = new ImportMapping(DateColumn.Value.Value, AmountColumn.Value.Value, DescriptionColumn?.Value, CategoryColumn?.Value, TypeColumn?.Value, _notesColumn, _accountColumn);
        var preview = await _import.PreviewAsync(_table, new ImportOptions(SelectedAccount.Id, mapping, DateStyle.Value, NumberStyle.Value)).ConfigureAwait(true);
        if (version != _previewVersion)
        {
            return;
        }

        _preview = preview;
        SummaryText = _localization.Format("Settings.Data.Import.Summary", preview.ReadyCount, preview.DuplicateCount, preview.SkippedCount, preview.ErrorCount);
        PreviewRows = preview.Rows.Take(MaxPreviewRows).Select(r => new ImportPreviewRow(
            r.LineNumber,
            _localization.GetString($"ImportStatus.{r.Status}"),
            r.Status,
            r.Date == default ? string.Empty : _format.FormatDate(r.Date),
            r.Description,
            r.CategoryId is { } categoryId ? _categoryNames.GetValueOrDefault(categoryId, r.CategoryName) : string.Empty,
            r.Amount == 0 ? string.Empty : (r.Type == TransactionType.Expense ? "-" : "+") + _format.FormatMoney(r.Amount),
            r.MessageCode is null ? null : _localization.GetString($"ImportMessage.{r.MessageCode}"))).ToList();
        MoreRowsText = preview.Rows.Count > MaxPreviewRows ? _localization.Format("Settings.Data.Import.More", preview.Rows.Count - MaxPreviewRows) : null;
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(RowsToImport));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (_preview is null || SelectedAccount is null || RowsToImport == 0)
        {
            GeneralError = _localization.GetString("Settings.Data.Import.Nothing");
            return false;
        }

        var rows = _preview.Rows.Where(r => r.Status == ImportRowStatus.Ready || (IncludeDuplicates && r.Status == ImportRowStatus.Duplicate)).ToList();
        try
        {
            ImportedCount = await _import.ImportAsync(SelectedAccount.Id, rows).ConfigureAwait(true);
            return true;
        }
        catch (ImportFailedException ex)
        {
            GeneralError = _localization.Format("Settings.Data.Import.Failed", ex.LineNumber, _localization.GetString($"Validation.{ex.Code}"));
            return false;
        }
    }

    partial void OnSelectedAccountChanged(AccountOption? value) => _ = RefreshPreviewAsync();

    partial void OnDateColumnChanged(SelectOption<int?>? value) => _ = RefreshPreviewAsync();

    partial void OnAmountColumnChanged(SelectOption<int?>? value) => _ = RefreshPreviewAsync();

    partial void OnDescriptionColumnChanged(SelectOption<int?>? value) => _ = RefreshPreviewAsync();

    partial void OnCategoryColumnChanged(SelectOption<int?>? value) => _ = RefreshPreviewAsync();

    partial void OnTypeColumnChanged(SelectOption<int?>? value) => _ = RefreshPreviewAsync();

    partial void OnDateStyleChanged(SelectOption<DateFormatStyle>? value) => _ = RefreshPreviewAsync();

    partial void OnNumberStyleChanged(SelectOption<ImportNumberStyle>? value) => _ = RefreshPreviewAsync();
}
