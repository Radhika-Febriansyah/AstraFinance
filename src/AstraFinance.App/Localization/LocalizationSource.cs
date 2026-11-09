using System.ComponentModel;
using AstraFinance.Core.Interfaces;

namespace AstraFinance.App.Localization;

/// <summary>
/// Bindable bridge between XAML and <see cref="ILocalizationService"/>. Bindings to
/// <c>[Key]</c> refresh automatically when the language changes.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    private ILocalizationService? _service;

    private LocalizationSource()
    {
    }

    public static LocalizationSource Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _service?.GetString(key) ?? key;

    public void Attach(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service is not null)
        {
            _service.LanguageChanged -= OnLanguageChanged;
        }

        _service = service;
        _service.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}
