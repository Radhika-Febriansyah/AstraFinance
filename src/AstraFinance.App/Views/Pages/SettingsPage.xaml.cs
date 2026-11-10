using System.Windows;
using System.Windows.Controls;

namespace AstraFinance.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    private const double StackedBreakpoint = 860;
    private bool? _stacked;

    public SettingsPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    /// <summary>Section list beside the content on wide windows; a wrapping row above it on narrow ones.</summary>
    private void ApplyLayout(double width)
    {
        var stacked = width < StackedBreakpoint;
        if (_stacked == stacked)
        {
            return;
        }

        _stacked = stacked;
        NavColumn.Width = stacked ? new GridLength(0) : new GridLength(220);
        Grid.SetColumnSpan(SectionNav, stacked ? 2 : 1);
        Grid.SetRowSpan(SectionNav, stacked ? 1 : 2);
        SectionNav.Margin = stacked ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 20, 0);
        SectionNav.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(stacked ? typeof(WrapPanel) : typeof(StackPanel)));
        Grid.SetColumn(ContentScroller, stacked ? 0 : 1);
        Grid.SetColumnSpan(ContentScroller, stacked ? 2 : 1);
        Grid.SetRow(ContentScroller, stacked ? 2 : 1);
        Grid.SetRowSpan(ContentScroller, stacked ? 1 : 2);
    }
}
