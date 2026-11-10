using System.Diagnostics;
using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

public enum ReportPeriodPreset
{
    ThisMonth = 0,
    LastMonth = 1,
    ThisYear = 2,
    LastYear = 3,
    Custom = 4,
}

/// <summary>One line of the report preview.</summary>
public sealed record ReportPreviewLine(string Label, string Value);

/// <summary>
/// Exportable reports (separate from Analytics). The user picks a report, a period and a format; the page
/// previews the totals and the number of records, and the export writes exactly that report to a file.
/// </summary>
public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly IReportService _reports;
    private readonly IReportExporter _exporter;
    private readonly IFileSavePicker _savePicker;
    private readonly INotificationService _notifications;
    private readonly DataChangeNotifier _changes;
    private readonly IClock _clock;
    private bool _suppressPreview;
    private int _previewVersion;

    public ReportsViewModel(
        ILocalizationService localization,
        IReportService reports,
        IReportExporter exporter,
        IFileSavePicker savePicker,
        INotificationService notifications,
        DataChangeNotifier changes,
        IClock clock)
        : base(AppPage.Reports, localization)
    {
        _reports = reports;
        _exporter = exporter;
        _savePicker = savePicker;
        _notifications = notifications;
        _changes = changes;
        _clock = clock;

        ReportOptions = Enum.GetValues<ReportKind>()
            .Select(k => new ChoiceOption<ReportKind>(k, () => Localization.GetString($"Report.{k}.Title"), IconFor(k), v => Kind = v))
            .ToList();
        PeriodOptions = Enum.GetValues<ReportPeriodPreset>()
            .Select(p => new ChoiceOption<ReportPeriodPreset>(p, () => Localization.GetString($"Reports.Period.{p}"), null, SetPreset))
            .ToList();
        FormatOptions =
        [
            new(ExportFormat.Pdf, () => "PDF", "Icon.Reports", f => Format = f),
            new(ExportFormat.Excel, () => "Excel (.xlsx)", "Icon.Layers", f => Format = f),
            new(ExportFormat.Csv, () => "CSV", "Icon.Receipt", f => Format = f),
        ];
        ReportOptions[0].SetSelectedSilently(true);
        PeriodOptions[0].SetSelectedSilently(true);
        FormatOptions[0].SetSelectedSilently(true);
        _changes.Changed += OnDataChanged;
    }

    public IReadOnlyList<ChoiceOption<ReportKind>> ReportOptions { get; }

    public IReadOnlyList<ChoiceOption<ReportPeriodPreset>> PeriodOptions { get; }

    public IReadOnlyList<ChoiceOption<ExportFormat>> FormatOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindDescription))]
    public partial ReportKind Kind { get; private set; }

    public string KindDescription => Localization.GetString($"Reports.{Kind}.Description");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    public partial ReportPeriodPreset Preset { get; private set; }

    public bool IsCustom => Preset == ReportPeriodPreset.Custom;

    [ObservableProperty]
    public partial DateOnly? CustomFrom { get; set; }

    [ObservableProperty]
    public partial DateOnly? CustomTo { get; set; }

    [ObservableProperty]
    public partial string? PeriodError { get; private set; }

    [ObservableProperty]
    public partial ExportFormat Format { get; private set; }

    [ObservableProperty]
    public partial string PreviewTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PreviewPeriod { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ReportPreviewLine> PreviewSummary { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ReportPreviewLine> PreviewTables { get; private set; } = [];

    /// <summary>"The export contains N records", so nothing is ever left out silently.</summary>
    [ObservableProperty]
    public partial string PreviewCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsExporting { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastExport))]
    public partial string? LastExportPath { get; private set; }

    public bool HasLastExport => LastExportPath is not null;

    public override Task OnNavigatedToAsync(NavigationTarget target) => RefreshPreviewAsync();

    /// <summary>The selected period, or null with an error message when the custom range is incomplete or reversed.</summary>
    public DateRange? ResolvePeriod()
    {
        PeriodError = null;
        var today = _clock.Today;
        switch (Preset)
        {
            case ReportPeriodPreset.ThisMonth:
                return DateRange.MonthOf(today);
            case ReportPeriodPreset.LastMonth:
                return DateRange.MonthOf(today.AddMonths(-1));
            case ReportPeriodPreset.ThisYear:
                return AnalyticsPeriods.Resolve(AnalyticsPeriod.Year, today);
            case ReportPeriodPreset.LastYear:
                return AnalyticsPeriods.Resolve(AnalyticsPeriod.Year, today.AddYears(-1));
            default:
                if (CustomFrom is null || CustomTo is null)
                {
                    PeriodError = Localization.GetString("Analytics.Custom.Required");
                    return null;
                }

                if (CustomTo < CustomFrom)
                {
                    PeriodError = Localization.GetString("Analytics.Custom.Order");
                    return null;
                }

                return new DateRange(CustomFrom.Value, CustomTo.Value);
        }
    }

    public async Task RefreshPreviewAsync()
    {
        if (_suppressPreview)
        {
            return;
        }

        var period = ResolvePeriod();
        var version = ++_previewVersion;
        if (period is null)
        {
            PreviewSummary = [];
            PreviewTables = [];
            PreviewCountText = string.Empty;
            return;
        }

        IsLoading = true;
        try
        {
            var report = await _reports.BuildAsync(Kind, period.Value).ConfigureAwait(true);
            if (version != _previewVersion)
            {
                return;
            }

            PreviewTitle = report.Title;
            PreviewPeriod = report.PeriodText;
            PreviewSummary = report.Summary.Select(s => new ReportPreviewLine(s.Label, s.Value.Display(report.Format))).ToList();
            PreviewTables = report.Tables.Select(t => new ReportPreviewLine(t.Title, Localization.Format(t.Rows.Count == 1 ? "Reports.RowsOne" : "Reports.Rows", t.Rows.Count))).ToList();
            PreviewCountText = Localization.Format("Reports.AllIncluded", report.RowCount);
        }
        finally
        {
            if (version == _previewVersion)
            {
                IsLoading = false;
            }
        }
    }

    private bool CanExport() => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var period = ResolvePeriod();
        if (period is null)
        {
            return;
        }

        var (extension, filterKey) = Format switch
        {
            ExportFormat.Pdf => ("pdf", "Reports.Filter.Pdf"),
            ExportFormat.Excel => ("xlsx", "Reports.Filter.Excel"),
            _ => ("csv", "Reports.Filter.Csv"),
        };
        var fileName = string.Create(CultureInfo.InvariantCulture,
            $"astra-{Kind.ToString().ToLowerInvariant()}-{period.Value.Start:yyyyMMdd}-{period.Value.End:yyyyMMdd}.{extension}");
        var path = _savePicker.PickSaveFile(Localization.GetString("Reports.Export.Title"), fileName, Localization.GetString(filterKey));
        if (path is null)
        {
            return;
        }

        IsExporting = true;
        try
        {
            // Built fresh, so the file reflects the data at the moment of export.
            var report = await _reports.BuildAsync(Kind, period.Value).ConfigureAwait(true);
            await _exporter.ExportAsync(report, Format, path).ConfigureAwait(true);
            LastExportPath = path;
            _notifications.Notify(NotificationKind.Success, Localization.Format("Reports.Export.Done", report.Title), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _notifications.Notify(NotificationKind.Error, Localization.GetString("Transactions.Export.Failed"), Localization.GetString("Transactions.Export.FailedBody"));
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenLastExport()
    {
        if (LastExportPath is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void ShowLastExportInFolder()
    {
        if (LastExportPath is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
    }

    protected override void OnLanguageChanged()
    {
        foreach (var option in ReportOptions)
        {
            option.RefreshText();
        }

        foreach (var option in PeriodOptions)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(KindDescription));
        _ = RefreshPreviewAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
        }

        base.Dispose(disposing);
    }

    partial void OnKindChanged(ReportKind value) => _ = RefreshPreviewAsync();

    partial void OnCustomFromChanged(DateOnly? value) => _ = RefreshPreviewAsync();

    partial void OnCustomToChanged(DateOnly? value) => _ = RefreshPreviewAsync();

    private void SetPreset(ReportPeriodPreset preset)
    {
        if (preset == ReportPeriodPreset.Custom && CustomFrom is null && CustomTo is null)
        {
            var month = DateRange.MonthOf(_clock.Today);
            _suppressPreview = true;
            CustomFrom = month.Start;
            CustomTo = month.End;
            _suppressPreview = false;
        }

        Preset = preset;
        _ = RefreshPreviewAsync();
    }

    private static string IconFor(ReportKind kind) => kind switch
    {
        ReportKind.Income => "Icon.ArrowDownLeft",
        ReportKind.Expense => "Icon.ArrowUpRight",
        ReportKind.CashFlow => "Icon.Analytics",
        ReportKind.AccountBalance => "Icon.Accounts",
        ReportKind.Budget => "Icon.Budgets",
        _ => "Icon.SavingsGoals",
    };

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = RefreshPreviewAsync();
}
