namespace AstraFinance.Core.Navigation;

/// <summary>
/// Static metadata describing a main navigation page.
/// Titles and descriptions are localization keys, never display strings.
/// </summary>
/// <param name="Page">The page identifier.</param>
/// <param name="TitleKey">Localization key of the page title.</param>
/// <param name="DescriptionKey">Localization key of a one-line page description.</param>
/// <param name="IconKey">Resource key of the page icon geometry (see Themes/Icons.xaml).</param>
/// <param name="PlannedPhase">Roadmap phase in which the page's functionality is implemented.</param>
/// <param name="IsFooterItem">True when the item is rendered in the sidebar footer group.</param>
public sealed record PageDescriptor(
    AppPage Page,
    string TitleKey,
    string DescriptionKey,
    string IconKey,
    int PlannedPhase,
    bool IsFooterItem = false);
