using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AstraFinance.App.Controls;

/// <summary>
/// ItemsControl for a group of radio choices. The default ItemsControl automation peer exposes
/// only data items, hiding the radio buttons from screen readers and UI automation; this control
/// exposes its visual children (the radio buttons) directly inside a named group.
/// </summary>
public sealed class ChoiceGroup : ItemsControl
{
    static ChoiceGroup()
    {
        FocusableProperty.OverrideMetadata(typeof(ChoiceGroup), new FrameworkPropertyMetadata(false));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChoiceGroupAutomationPeer(this);

    private sealed class ChoiceGroupAutomationPeer(ChoiceGroup owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(ChoiceGroup);
    }
}
