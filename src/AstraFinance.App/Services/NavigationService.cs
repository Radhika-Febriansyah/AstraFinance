using AstraFinance.App.ViewModels;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.App.Services;

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    event EventHandler? Navigated;

    Task NavigateAsync(NavigationTarget target);

    Task NavigateAsync(AppPage page) => NavigateAsync(new NavigationTarget(page));
}

/// <summary>Maps each <see cref="AppPage"/> to a factory that creates its view model.</summary>
public sealed class PageRegistry
{
    private readonly Dictionary<AppPage, Func<IServiceProvider, PageViewModel>> _factories = [];

    public PageRegistry Register(AppPage page, Func<IServiceProvider, PageViewModel> factory)
    {
        _factories[page] = factory;
        return this;
    }

    public bool IsRegistered(AppPage page) => _factories.ContainsKey(page);

    public PageViewModel Create(AppPage page, IServiceProvider services) =>
        _factories.TryGetValue(page, out var factory)
            ? factory(services)
            : throw new InvalidOperationException($"No page registered for {page}.");
}

/// <summary>
/// Creates a fresh page view model for each navigation and disposes the previous one,
/// so pages always show current data and release their event subscriptions.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly PageRegistry _registry;
    private readonly IServiceProvider _services;

    public NavigationService(PageRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
    }

    public PageViewModel? CurrentPage { get; private set; }

    public event EventHandler? Navigated;

    public async Task NavigateAsync(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (CurrentPage is not null && CurrentPage.Page == target.Page && target.EntityId is null)
        {
            return;
        }

        var next = _registry.Create(target.Page, _services);
        var previous = CurrentPage;
        CurrentPage = next;
        Navigated?.Invoke(this, EventArgs.Empty);
        previous?.Dispose();

        await next.OnNavigatedToAsync(target).ConfigureAwait(true);
    }
}
