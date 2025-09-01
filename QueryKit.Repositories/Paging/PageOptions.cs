namespace QueryKit.Repositories.Paging;

/// <summary>
/// Paging request options.
/// </summary>
public sealed record PageOptions
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <summary>
    /// 1-based page number. Values less than 1 will be clamped to 1.
    /// </summary>
    public int Page { get; init; } = DefaultPage;
    
    /// <summary>
    /// Page size requested. Values less than 1 will be clamped to 1; values greater than <see cref="MaxPageSize"/> will be clamped.
    /// </summary>
    public int PageSize { get; init; } = DefaultPageSize;
    
    /// <summary>
    /// Gets the sanitized page number.
    /// </summary>
    public int PageClamped => Page < 1 ? DefaultPage : Page;
    
    /// <summary>
    /// Gets the sanitized page size.
    /// </summary>
    public int PageSizeClamped => PageSize < 1 ? DefaultPageSize : (PageSize >  MaxPageSize ? MaxPageSize : PageSize);
    
    public int Skip => (PageClamped - 1) * PageSizeClamped;
    public int Take => PageSizeClamped;
    
    public static PageOptions Create(int page, int pageSize) => new PageOptions { Page = page, PageSize = pageSize };
}