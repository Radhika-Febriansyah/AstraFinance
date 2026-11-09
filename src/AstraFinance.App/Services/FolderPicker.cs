using Microsoft.Win32;

namespace AstraFinance.App.Services;

/// <summary>Native folder selection (native file dialogs are allowed by the design guidelines).</summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or null when cancelled.</summary>
    string? PickFolder(string title, string? initialDirectory);
}

public sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = initialDirectory is not null && System.IO.Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
        };
        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FolderName : null;
    }
}
