namespace FixFlow.Api.Infrastructure.Http;

/// <summary>The paging envelope for every list endpoint (architecture.md §7).</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public const int MaxPageSize = 100;

    /// <summary>Clamps client paging input to sane bounds (page ≥ 1, 1 ≤ size ≤ 100).</summary>
    public static (int Page, int PageSize) Clamp(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
}
