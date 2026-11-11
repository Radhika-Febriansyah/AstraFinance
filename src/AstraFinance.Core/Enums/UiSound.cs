namespace AstraFinance.Core.Enums;

/// <summary>The few events that may play a short, optional UI tone. Never continuous audio.</summary>
public enum UiSound
{
    PinSuccess = 0,
    PinError = 1,
    ImportantNotice = 2,
    TransactionSaved = 3,
}

/// <summary>Groups of UI sounds that the user can switch on or off separately.</summary>
public enum UiSoundCategory
{
    Pin = 0,
    Notifications = 1,
    Transactions = 2,
}
