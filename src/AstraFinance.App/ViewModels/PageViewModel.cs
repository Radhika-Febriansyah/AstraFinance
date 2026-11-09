using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstraFinance.App.ViewModels;

/// <summary>Base class for main navigation pages.</summary>
public abstract partial class PageViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    protected PageViewModel(AppPage page, ILocalizationService localization)
    {
        Page = page;
        Descriptor = PageCatalog.Get(page);
        Localization = localization;
        Localization.LanguageChanged += OnLanguageChangedInternal;
    }

    public AppPage Page { get; }

    public PageDescriptor Descriptor { get; }

    public string Title => Localization.GetString(Descriptor.TitleKey);

    public string Description => Localization.GetString(Descriptor.DescriptionKey);

    [ObservableProperty]
    public partial bool IsLoading { get; protected set; }

    protected ILocalizationService Localization { get; }

    /// <summary>Called after the page becomes active. <paramref name="target"/> may carry an entity id.</summary>
    public virtual Task OnNavigatedToAsync(NavigationTarget target) => Task.CompletedTask;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void OnLanguageChanged()
    {
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Localization.LanguageChanged -= OnLanguageChangedInternal;
        }

        _disposed = true;
    }

    private void OnLanguageChangedInternal(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnLanguageChanged();
    }
}
