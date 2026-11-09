using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstraFinance.App.ViewModels;

public sealed class NavItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    public NavItemViewModel(PageDescriptor descriptor, ILocalizationService localization)
    {
        Descriptor = descriptor;
        _localization = localization;
    }

    public PageDescriptor Descriptor { get; }

    public AppPage Page => Descriptor.Page;

    public string IconKey => Descriptor.IconKey;

    public string Title => _localization.GetString(Descriptor.TitleKey);

    public void RefreshText() => OnPropertyChanged(nameof(Title));
}
