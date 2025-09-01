namespace QueryKit.Repositories.Paging;

public sealed record PageOptions
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    
    public int PageClamped => Page < 1 ? DefaultPage : Page;
    public int PageSizeClamped => PageSize < 1 ? DefaultPageSize : (PageSize >  MaxPageSize ? MaxPageSize : PageSize);
    
    public int Skip => (PageClamped - 1) * PageSizeClamped;
    public int Take => PageSizeClamped;
    
    public static PageOptions Create(int page, int pageSize) => new PageOptions { Page = page, PageSize = pageSize };
}