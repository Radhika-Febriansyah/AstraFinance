namespace AstraFinance.Core.Services;

/// <summary>Chooses a time-of-day greeting key (Indonesian pagi/siang/sore/malam periods).</summary>
public static class GreetingProvider
{
    public static string GetGreetingKey(TimeOnly time) => time.Hour switch
    {
        >= 4 and < 11 => "Greeting.Morning",
        >= 11 and < 15 => "Greeting.Midday",
        >= 15 and < 18 => "Greeting.Afternoon",
        _ => "Greeting.Night",
    };
}
