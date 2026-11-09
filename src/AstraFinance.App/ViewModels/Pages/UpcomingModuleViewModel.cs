using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Navigation;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>
/// Honest status page for a module that is scheduled in a later roadmap phase.
/// It shows no figures and offers no non-functional actions. Each module replaces this
/// registration with its real view model when implemented.
/// </summary>
public sealed class UpcomingModuleViewModel : PageViewModel
{
    public UpcomingModuleViewModel(AppPage page, ILocalizationService localization)
        : base(page, localization)
    {
    }

    public string IconKey => Descriptor.IconKey;

    public string Notice => Localization.Format("Module.InDevelopment.Body", Title, Descriptor.PlannedPhase);

    public string PhaseBadge => Localization.Format("Module.InDevelopment.Badge", Descriptor.PlannedPhase);

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Notice));
        OnPropertyChanged(nameof(PhaseBadge));
    }
}
