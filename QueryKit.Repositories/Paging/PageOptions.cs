namespace QueryKit.Repositories.Paging;

/// <summary>
/// Paging request options.
/// </summary>
public sealed record PageOptions
{
    /// <summary>
    /// Default page number.
    /// </summary>
    public const int DefaultPage = 1;
    
    /// <summary>
    /// Default page size.
    /// </summary>
    public const int DefaultPageSize = 50;
    
    /// <summary>
    /// Maximum allowed page size.
    /// </summary>
    public const int MaxPageSize = 500;

    /// <summary>
    /// 1-based page number. Values less than 1 will be clamped to 1.
    /// </summary>
    public int Page { get; set; } = DefaultPage;
    
    /// <summary>
    /// Page size requested. Values less than 1 will be clamped to 1; values greater than <see cref="MaxPageSize"/> will be clamped.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;
    
    /// <summary>
    /// Gets the sanitized page number.
    /// </summary>
    public int PageClamped => Page < 1 ? DefaultPage : Page;
    
    /// <summary>
    /// Gets the sanitized page size.
    /// </summary>
    public int PageSizeClamped => PageSize < 1 ? DefaultPageSize : (PageSize >  MaxPageSize ? MaxPageSize : PageSize);
    
    /// <summary>
    /// The number of items to skip based on the current page and page size.
    /// </summary>
    public int Skip => (PageClamped - 1) * PageSizeClamped;
    
    /// <summary>
    /// The number of items to take based on the current page size.
    /// </summary>
    public int Take => PageSizeClamped;
    
    /// <summary>
    /// Creates a new instance of <see cref="PageOptions"/> with the specified page and page size.
    /// </summary>
    public static PageOptions Create(int page, int pageSize) => new PageOptions { Page = page, PageSize = pageSize };
}