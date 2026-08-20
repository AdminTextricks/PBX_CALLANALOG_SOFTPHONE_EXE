namespace CallAnalog.Softphone.Models;

public sealed class RecentCallItem
{
    public required string DisplayName { get; init; }
    public required string Number { get; init; }
    public required string TimeLabel { get; init; }
    public required string Direction { get; init; }
    public bool IsMissed { get; init; }
}
