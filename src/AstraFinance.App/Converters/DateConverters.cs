using System.Globalization;
using System.Windows.Data;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;

namespace AstraFinance.App.Converters;

/// <summary>Gives converters access to the user's regional format (set once at startup).</summary>
public static class DisplayFormat
{
    public static IRegionalFormatService? Service { get; set; }

    public static RegionalFormat Current => Service?.Current ?? RegionalFormat.Default;
}

/// <summary>DateOnly? (view model) ⇄ DateTime? (DatePicker). Never applies a time zone.</summary>
public sealed class DateOnlyToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly d ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dt ? DateOnly.FromDateTime(dt) : null;
}

/// <summary>Formats a DateTime? using the user's configured date format.</summary>
public sealed class RegionalDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTime dt => DisplayFormat.Current.FormatDate(DateOnly.FromDateTime(dt)),
        DateOnly d => DisplayFormat.Current.FormatDate(d),
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
