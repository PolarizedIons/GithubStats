using System.Collections;
using Octokit.GraphQL;
using Serilog;

namespace GithubStatsWorker;

public class GQLPagedResponse<T>
{
    public string? EndCursor { get; set; }
    public bool HasNextPage { get; set; }
    public List<T> Items { get; set; } = new();
}

public class PagedResponse<T> : IAsyncEnumerable<T>
{
    private readonly IConnection _connection;
    private readonly ICompiledQuery<GQLPagedResponse<T>> _query;
    private readonly Dictionary<string, object?> _parameters;
    public string? LastEndCursor { get; private set; }
    private bool _hasNextPage = true;
    private string? _nextPageCursor;
    private int _pageNumber;

    public PagedResponse(IConnection connection, ICompiledQuery<GQLPagedResponse<T>> query, Dictionary<string, object?>? parameters)
    {
        _connection = connection;
        _query = query;
        _parameters = parameters ?? new Dictionary<string, object?>();
        if (!_parameters.ContainsKey("after"))
        {
            _parameters["after"] = null;
        }

        LastEndCursor = (string?)_parameters["after"];
    }

    private async Task<List<T>> NextPage()
    {
        if (!_hasNextPage)
        {
            return new List<T>();
        }

        Log.Debug("Loading page {Count}...", ++_pageNumber);
        GQLPagedResponse<T>? page;
        try
        {
            page = await _connection.Run(_query, _parameters);
        }
        catch
        {
            _hasNextPage = false;
            return new List<T>();
        }

        _hasNextPage = page.HasNextPage;
        _nextPageCursor = page.EndCursor;
        _parameters["after"] = _nextPageCursor;
        if (!string.IsNullOrEmpty(_nextPageCursor))
        {
            LastEndCursor = _nextPageCursor;
        }

        return page.Items;
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new())
    {
        List<T> items;
        do
        {
            items = await NextPage();
            foreach (var item in items)
            {
                yield return item;
            }
        } while (_hasNextPage || items.Count > 0);
    }
}
