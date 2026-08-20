namespace CallAnalog.Softphone.Models;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int CurrentPage { get; init; }
    public int LastPage { get; init; }
    public int Total { get; init; }
    public int LoadedCount { get; init; }
    public bool HasMore => CurrentPage < LastPage;
}
