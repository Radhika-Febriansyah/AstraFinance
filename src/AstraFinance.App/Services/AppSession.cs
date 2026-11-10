using Microsoft.Win32;

namespace AstraFinance.App.Services;

/// <summary>
/// Shell-level actions that pages may request without depending on the shell: lock the app now, and restore a
/// backup (which needs the shell to block the UI, stop background work and reopen everything afterwards).
/// The shell registers the handlers when it is created.
/// </summary>
public sealed class AppSession
{
    public Func<Task>? LockHandler { get; set; }

    public Func<string, Task<bool>>? RestoreHandler { get; set; }

    public Task LockAsync() => LockHandler?.Invoke() ?? Task.CompletedTask;

    /// <summary>Restores the backup at <paramref name="path"/>; true when it succeeded.</summary>
    public Task<bool> RestoreAsync(string path) => RestoreHandler?.Invoke(path) ?? Task.FromResult(false);
}

/// <summary>Native "Open" dialog (native file dialogs are allowed by the design guidelines).</summary>
public interface IFileOpenPicker
{
    string? PickOpenFile(string title, string filter, string? initialDirectory);
}

public sealed class WindowsFileOpenPicker : IFileOpenPicker
{
    public string? PickOpenFile(string title, string filter, string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
