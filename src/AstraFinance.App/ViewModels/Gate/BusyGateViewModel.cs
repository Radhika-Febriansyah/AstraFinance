namespace AstraFinance.App.ViewModels.Gate;

/// <summary>
/// Full-window "please wait" gate for operations during which nothing else may touch the data
/// (restoring a backup). It has no actions, so the user can't start anything until it is removed.
/// </summary>
public sealed class BusyGateViewModel
{
    public BusyGateViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }
}
