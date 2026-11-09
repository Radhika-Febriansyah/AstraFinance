using System.Collections.ObjectModel;
using AstraFinance.App.Services;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels;

public sealed partial class SearchResultItemViewModel : ObservableObject
{
    public SearchResultItemViewModel(SearchResult result)
    {
        Result = result;
    }

    public SearchResult Result { get; }

    public string Title => Result.Title;

    public string? Subtitle => Result.Subtitle;

    public string IconKey => Result.IconKey;

    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }
}

public sealed class SearchGroupViewModel
{
    public SearchGroupViewModel(string header, IReadOnlyList<SearchResultItemViewModel> items)
    {
        Header = header;
        Items = items;
    }

    public string Header { get; }

    public IReadOnlyList<SearchResultItemViewModel> Items { get; }
}

/// <summary>
/// Top-bar global search: debounced querying of all registered search providers,
/// categorized results, and keyboard navigation (Up/Down/Enter/Escape).
/// </summary>
public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly IGlobalSearchService _search;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly List<SearchResultItemViewModel> _flat = [];
    private CancellationTokenSource? _cts;

    public SearchViewModel(IGlobalSearchService search, INavigationService navigation, ILocalizationService localization)
    {
        _search = search;
        _navigation = navigation;
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<SearchGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoResultsText))]
    public partial bool HasNoResults { get; private set; }

    [ObservableProperty]
    public partial SearchResultItemViewModel? Highlighted { get; private set; }

    public string NoResultsText => _localization.Format("Shell.Search.NoResults", Query.Trim());

    /// <summary>Exposed for tests: completes when the most recent search finishes.</summary>
    internal Task PendingSearch { get; private set; } = Task.CompletedTask;

    public void MoveHighlight(int delta)
    {
        if (_flat.Count == 0)
        {
            return;
        }

        var index = Highlighted is null ? -1 : _flat.IndexOf(Highlighted);
        index = index < 0 ? (delta > 0 ? 0 : _flat.Count - 1) : (index + delta + _flat.Count) % _flat.Count;
        SetHighlight(_flat[index]);
    }

    [RelayCommand]
    public async Task OpenHighlightedAsync()
    {
        if (Highlighted is not null)
        {
            await OpenAsync(Highlighted).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task OpenAsync(SearchResultItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Close();
        Query = string.Empty;
        await _navigation.NavigateAsync(item.Result.Target).ConfigureAwait(true);
    }

    [RelayCommand]
    public void Clear()
    {
        Query = string.Empty;
        Close();
    }

    public void Close() => IsOpen = false;

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    partial void OnQueryChanged(string value)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < _search.MinimumQueryLength)
        {
            ClearResults();
            IsOpen = false;
            PendingSearch = Task.CompletedTask;
            return;
        }

        _cts = new CancellationTokenSource();
        PendingSearch = RunSearchAsync(value, _cts.Token);
    }

    private async Task RunSearchAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(Debounce, token).ConfigureAwait(true);
            var groups = await _search.SearchAsync(query, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            ShowResults(groups);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query.
        }
    }

    private void ShowResults(IReadOnlyList<SearchResultGroup> groups)
    {
        ClearResults();
        foreach (var group in groups)
        {
            var items = group.Results.Select(r => new SearchResultItemViewModel(r)).ToList();
            _flat.AddRange(items);
            Groups.Add(new SearchGroupViewModel(_localization.GetString($"SearchCategory.{group.Category}"), items));
        }

        HasNoResults = _flat.Count == 0;
        if (_flat.Count > 0)
        {
            SetHighlight(_flat[0]);
        }

        IsOpen = true;
    }

    private void ClearResults()
    {
        Groups.Clear();
        _flat.Clear();
        Highlighted = null;
        HasNoResults = false;
    }

    private void SetHighlight(SearchResultItemViewModel item)
    {
        if (Highlighted is not null)
        {
            Highlighted.IsHighlighted = false;
        }

        Highlighted = item;
        item.IsHighlighted = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Results contain localized titles; re-run the query in the new language.
        if (!string.IsNullOrWhiteSpace(Query))
        {
            OnQueryChanged(Query);
        }
    }
}
