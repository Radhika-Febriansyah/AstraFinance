using System.ComponentModel;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.Services;

/// <summary>
/// Content of a form dialog (rendered by a DataTemplate in the dialog layer). <see cref="SubmitAsync"/>
/// validates and saves; returning false keeps the dialog open (e.g. to show validation errors).
/// </summary>
public interface IFormDialog : INotifyPropertyChanged
{
    string Title { get; }

    string PrimaryText { get; }

    bool IsDestructive { get; }

    Task<bool> SubmitAsync();
}

/// <summary>Astra-styled in-window dialogs (confirmation, alerts and forms).</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string? cancelText = null, bool isDestructive = false);

    Task AlertAsync(NotificationKind kind, string title, string message);

    /// <summary>Shows a form; returns true when it was submitted successfully.</summary>
    Task<bool> ShowFormAsync(IFormDialog form);
}

/// <summary>One dialog request rendered by the shell's dialog layer.</summary>
public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogViewModel(NotificationKind kind, string title, string message, string primaryText, string? secondaryText, bool isDestructive, IFormDialog? form = null)
    {
        Kind = kind;
        Title = title;
        Message = message;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        IsDestructive = isDestructive;
        Form = form;
    }

    public NotificationKind Kind { get; }

    public string Title { get; }

    public string Message { get; }

    public string PrimaryText { get; }

    public string? SecondaryText { get; }

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryText);

    public bool IsDestructive { get; }

    /// <summary>Form content; null for message dialogs.</summary>
    public IFormDialog? Form { get; }

    public bool IsForm => Form is not null;

    public bool HasIcon => Form is null;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    public Task<bool> Completion => _completion.Task;

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (Form is null)
        {
            _completion.TrySetResult(true);
            return;
        }

        IsBusy = true;
        try
        {
            if (await Form.SubmitAsync().ConfigureAwait(true))
            {
                _completion.TrySetResult(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            _completion.TrySetResult(false);
        }
    }
}

/// <summary>Queues dialogs so only one is visible at a time; the shell binds to <see cref="ActiveDialog"/>.</summary>
public sealed partial class DialogService : ObservableObject, IDialogService, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogService(ILocalizationService localization)
    {
        _localization = localization;
    }

    [ObservableProperty]
    public partial DialogViewModel? ActiveDialog { get; private set; }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string? cancelText = null, bool isDestructive = false) =>
        await ShowAsync(new DialogViewModel(
            isDestructive ? NotificationKind.Warning : NotificationKind.Info,
            title,
            message,
            confirmText,
            cancelText ?? _localization.GetString("Dialog.Cancel"),
            isDestructive)).ConfigureAwait(true);

    public Task AlertAsync(NotificationKind kind, string title, string message) =>
        ShowAsync(new DialogViewModel(kind, title, message, _localization.GetString("Dialog.OK"), null, false));

    public Task<bool> ShowFormAsync(IFormDialog form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return ShowAsync(new DialogViewModel(
            NotificationKind.Info, form.Title, string.Empty, form.PrimaryText, _localization.GetString("Dialog.Cancel"), form.IsDestructive, form));
    }

    public void Dispose() => _gate.Dispose();

    private async Task<bool> ShowAsync(DialogViewModel dialog)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            ActiveDialog = dialog;
            return await dialog.Completion.ConfigureAwait(true);
        }
        finally
        {
            ActiveDialog = null;
            _gate.Release();
        }
    }
}
