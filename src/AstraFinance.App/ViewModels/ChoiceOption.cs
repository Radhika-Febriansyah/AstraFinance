using CommunityToolkit.Mvvm.ComponentModel;

namespace AstraFinance.App.ViewModels;

/// <summary>A selectable option (rendered as a radio choice) whose selection invokes a callback.</summary>
public sealed partial class ChoiceOption<T> : ObservableObject
{
    private readonly Func<string> _label;
    private readonly Action<T> _onSelected;

    public ChoiceOption(T value, Func<string> label, string? iconKey, Action<T> onSelected)
    {
        Value = value;
        _label = label;
        IconKey = iconKey;
        _onSelected = onSelected;
    }

    public T Value { get; }

    public string Label => _label();

    public string? IconKey { get; }

    /// <summary>Optional swatch color (hex) for color choices.</summary>
    [ObservableProperty]
    public partial string? SwatchHex { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Updates the selection state without invoking the callback.</summary>
    public void SetSelectedSilently(bool value)
    {
        _suppress = true;
        IsSelected = value;
        _suppress = false;
    }

    public void RefreshText() => OnPropertyChanged(nameof(Label));

    /// <summary>Used by screen readers and ComboBox keyboard text search.</summary>
    public override string ToString() => Label;

    private bool _suppress;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !_suppress)
        {
            _onSelected(Value);
        }
    }
}
