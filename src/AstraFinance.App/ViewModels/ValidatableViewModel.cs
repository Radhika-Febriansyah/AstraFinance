using System.Collections;
using System.ComponentModel;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstraFinance.App.ViewModels;

/// <summary>
/// View model with per-field validation messages surfaced through <see cref="INotifyDataErrorInfo"/>,
/// so the Astra validation template shows them under the field.
/// </summary>
public abstract partial class ValidatableViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => _errors.Count > 0 || !string.IsNullOrEmpty(GeneralError);

    /// <summary>Error not tied to a specific field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string? GeneralError { get; protected set; }

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var message) ? new[] { message } : Array.Empty<string>();

    public string? GetError(string propertyName) => _errors.GetValueOrDefault(propertyName);

    protected void SetError(string propertyName, string? message)
    {
        var changed = string.IsNullOrEmpty(message)
            ? _errors.Remove(propertyName)
            : !_errors.TryGetValue(propertyName, out var existing) || existing != message;
        if (!string.IsNullOrEmpty(message))
        {
            _errors[propertyName] = message;
        }

        if (changed)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    protected void ClearAllErrors()
    {
        var keys = _errors.Keys.ToList();
        _errors.Clear();
        foreach (var key in keys)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(key));
        }

        GeneralError = null;
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>
    /// Shows service validation errors. <paramref name="fieldMap"/> maps service field names to
    /// view-model property names; unmapped errors become <see cref="GeneralError"/>.
    /// </summary>
    protected void ApplyValidation(FinanceValidationException exception, ILocalizationService localization, IReadOnlyDictionary<string, string> fieldMap)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(fieldMap);
        foreach (var error in exception.Errors)
        {
            var message = localization.GetString($"Validation.{error.Code}");
            if (fieldMap.TryGetValue(error.Field, out var property))
            {
                SetError(property, message);
            }
            else
            {
                GeneralError = message;
            }
        }
    }
}
