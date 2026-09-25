using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WordPressSharp.Models;

namespace WordPressSharp;

/// <summary>
/// Client for a site's WordPress REST API. Use an Application Password over HTTPS for server-to-server authentication.
/// </summary>
public sealed class WordPressClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _siteUri;
    private readonly Uri? _configuredRestRoot;
    private readonly AuthenticationHeaderValue? _authorization;
    private Uri? _restRoot;
    private bool _disposed;

    public WordPressClient(WordPressClientOptions options)
        : this(options, CreateHttpClient(options), ownsHttpClient: true)
    {
    }

    /// <summary>Creates a client using a caller-managed HttpClient (recommended for dependency injection).</summary>
    public WordPressClient(WordPressClientOptions options, HttpClient httpClient)
        : this(options, httpClient, ownsHttpClient: false)
    {
    }

    private WordPressClient(WordPressClientOptions options, HttpClient httpClient, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        options.Validate();

        _siteUri = EnsureTrailingSlash(options.SiteUri);
        _configuredRestRoot = options.RestRoot is null ? null : EnsureTrailingSlash(options.RestRoot);
        _restRoot = _configuredRestRoot;
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        if (options.Username is not null && options.ApplicationPassword is not null)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.ApplicationPassword}"));
            _authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    /// <summary>The REST root after it has been configured or discovered.</summary>
    public Uri? RestRoot => _restRoot;

    private static HttpClient CreateHttpClient(WordPressClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var client = new HttpClient { Timeout = options.Timeout };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Discovers the REST root from the WordPress site index, unless an explicit root was configured.</summary>
    public async Task<WordPressApiIndex> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Uri indexUri;
        if (_configuredRestRoot is not null)
        {
            indexUri = _configuredRestRoot;
        }
        else
        {
            using var homeRequest = CreateRequest(HttpMethod.Get, _siteUri);
            using var homeResponse = await ValidateAndDetachResponseAsync(
                await _httpClient.SendAsync(homeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            indexUri = new Uri(_siteUri, GetHeaderLink(homeResponse, "https://api.w.org/") ?? "wp-json/");
        }

        using var indexRequest = CreateRequest(HttpMethod.Get, indexUri);
        using var response = await ValidateAndDetachResponseAsync(
            await _httpClient.SendAsync(indexRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        var index = await response.Content.ReadFromJsonAsync<WordPressApiIndex>(WordPressJson.DefaultOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("WordPress returned an empty REST API index.");

        _restRoot = indexUri;
        return index;
    }

    /// <summary>Returns route/schema information from the REST index. Includes plugin and custom routes.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>> GetRoutesAsync(CancellationToken cancellationToken = default)
    {
        var index = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return index.Routes ?? new Dictionary<string, JsonElement>();
    }

    /// <summary>Issues an OPTIONS request to inspect a route's available methods and argument schema.</summary>
    public async Task<JsonElement> GetEndpointSchemaAsync(string route, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Options, route, null, null, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Performs an extensible request against a WordPress REST route, including plugin/custom routes.</summary>
    public async Task<JsonElement> SendJsonAsync(
        HttpMethod method,
        string route,
        object? payload = null,
        IEnumerable<KeyValuePair<string, string?>>? query = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(method, route, payload, query, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>GETs any REST route and deserializes the JSON response.</summary>
    public async Task<T?> GetAsync<T>(string route, IEnumerable<KeyValuePair<string, string?>>? query = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, route, null, query, cancellationToken).ConfigureAwait(false);
        return await ReadContentAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets a page from a WordPress collection, including total-item and total-page headers.</summary>
    public async Task<WordPressPage<T>> GetPageAsync<T>(string route, WordPressListOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, route, null, options?.ToQueryParameters(), cancellationToken).ConfigureAwait(false);
        var items = await ReadArrayAsync<T>(response, cancellationToken).ConfigureAwait(false);
        return new WordPressPage<T>(items, ReadHeaderInteger(response, "X-WP-Total"), ReadHeaderInteger(response, "X-WP-TotalPages"));
    }

    public Task<WordPressContent?> GetPostAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressContent>($"wp/v2/posts/{id}", cancellationToken: cancellationToken);
    public Task<WordPressPage<WordPressContent>> GetPostsAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressContent>("wp/v2/posts", options, cancellationToken);
    public Task<JsonElement> CreatePostAsync(WordPressWriteRequest post, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/posts", post, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdatePostAsync(int id, WordPressWriteRequest post, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/posts/{id}", post, cancellationToken: cancellationToken);
    public Task<JsonElement> DeletePostAsync(int id, bool force = false, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, $"wp/v2/posts/{id}", query: [new("force", force ? "true" : "false")], cancellationToken: cancellationToken);

    public Task<WordPressContent?> GetPageAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressContent>($"wp/v2/pages/{id}", cancellationToken: cancellationToken);
    public Task<WordPressPage<WordPressContent>> GetPagesAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressContent>("wp/v2/pages", options, cancellationToken);
    public Task<JsonElement> CreatePageAsync(WordPressWriteRequest page, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/pages", page, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdatePageAsync(int id, WordPressWriteRequest page, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/pages/{id}", page, cancellationToken: cancellationToken);
    public Task<JsonElement> DeletePageAsync(int id, bool force = false, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, $"wp/v2/pages/{id}", query: [new("force", force ? "true" : "false")], cancellationToken: cancellationToken);

    /// <summary>Lists registered content through the REST base for a post type (for example, a custom post type).</summary>
    public Task<WordPressPage<T>> GetContentAsync<T>(string restBase, WordPressListOptions? options = null, CancellationToken cancellationToken = default) =>
        GetPageAsync<T>(BuildResourcePath(restBase), options, cancellationToken);

    /// <summary>Gets a registered content item through its REST base.</summary>
    public Task<T?> GetContentAsync<T>(string restBase, int id, CancellationToken cancellationToken = default) =>
        GetAsync<T>($"{BuildResourcePath(restBase)}/{id}", cancellationToken: cancellationToken);

    /// <summary>Creates an item for a registered content type through its REST base.</summary>
    public Task<JsonElement> CreateContentAsync(string restBase, WordPressWriteRequest content, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, BuildResourcePath(restBase), content, cancellationToken: cancellationToken);

    /// <summary>Updates an item for a registered content type through its REST base.</summary>
    public Task<JsonElement> UpdateContentAsync(string restBase, int id, WordPressWriteRequest content, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, $"{BuildResourcePath(restBase)}/{id}", content, cancellationToken: cancellationToken);

    public Task<JsonElement> DeleteContentAsync(string restBase, int id, bool force = false, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Delete, $"{BuildResourcePath(restBase)}/{id}", query: [new("force", force ? "true" : "false")], cancellationToken: cancellationToken);

    public Task<WordPressPage<JsonElement>> GetPostRevisionsAsync(int postId, WordPressListOptions? options = null, CancellationToken cancellationToken = default) =>
        SendCollectionAsync<JsonElement>($"wp/v2/posts/{postId}/revisions", options, cancellationToken);
    public Task<JsonElement> GetPostRevisionAsync(int postId, int revisionId, CancellationToken cancellationToken = default) =>
        GetJsonAsync($"wp/v2/posts/{postId}/revisions/{revisionId}", cancellationToken);
    public Task<WordPressPage<JsonElement>> GetPageRevisionsAsync(int pageId, WordPressListOptions? options = null, CancellationToken cancellationToken = default) =>
        SendCollectionAsync<JsonElement>($"wp/v2/pages/{pageId}/revisions", options, cancellationToken);
    public Task<JsonElement> GetPageRevisionAsync(int pageId, int revisionId, CancellationToken cancellationToken = default) =>
        GetJsonAsync($"wp/v2/pages/{pageId}/revisions/{revisionId}", cancellationToken);
    public Task<JsonElement> GetPostAutosavesAsync(int postId, CancellationToken cancellationToken = default) =>
        GetJsonAsync($"wp/v2/posts/{postId}/autosaves", cancellationToken);
    public Task<JsonElement> GetPageAutosavesAsync(int pageId, CancellationToken cancellationToken = default) =>
        GetJsonAsync($"wp/v2/pages/{pageId}/autosaves", cancellationToken);

    public Task<WordPressMedia?> GetMediaAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressMedia>($"wp/v2/media/{id}", cancellationToken: cancellationToken);
    public Task<WordPressPage<WordPressMedia>> GetMediaAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressMedia>("wp/v2/media", options, cancellationToken);
    public Task<JsonElement> CreateMediaAsync(WordPressWriteRequest media, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/media", media, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateMediaAsync(int id, WordPressWriteRequest media, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/media/{id}", media, cancellationToken: cancellationToken);
    public Task<JsonElement> DeleteMediaAsync(int id, bool force = false, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, $"wp/v2/media/{id}", query: [new("force", force ? "true" : "false")], cancellationToken: cancellationToken);

    /// <summary>Uploads binary media content using WordPress's media endpoint.</summary>
    public async Task<JsonElement> UploadMediaAsync(string fileName, string contentType, Stream content, string? title = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);
        ThrowIfDisposed();
        await EnsureRestRootAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Post, BuildUri("wp/v2/media", null));
        request.Headers.TryAddWithoutValidation("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(fileName)}\"");
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content = streamContent;
        using var response = await ValidateAndDetachResponseAsync(
            await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        var uploaded = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        if (title is null || !uploaded.TryGetProperty("id", out var idNode) || !idNode.TryGetInt32(out var id))
            return uploaded;
        return await SendJsonAsync(HttpMethod.Post, $"wp/v2/media/{id}", WordPressPayload.Create(new { title }), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<WordPressComment?> GetCommentAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressComment>($"wp/v2/comments/{id}", cancellationToken: cancellationToken);
    public Task<WordPressPage<WordPressComment>> GetCommentsAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressComment>("wp/v2/comments", options, cancellationToken);
    public Task<JsonElement> CreateCommentAsync(WordPressWriteRequest comment, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/comments", comment, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateCommentAsync(int id, WordPressWriteRequest comment, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/comments/{id}", comment, cancellationToken: cancellationToken);
    public Task<JsonElement> DeleteCommentAsync(int id, bool force = false, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, $"wp/v2/comments/{id}", query: [new("force", force ? "true" : "false")], cancellationToken: cancellationToken);

    public Task<WordPressUser?> GetUserAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressUser>($"wp/v2/users/{id}", cancellationToken: cancellationToken);
    public Task<WordPressUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default) => GetAsync<WordPressUser>("wp/v2/users/me", cancellationToken: cancellationToken);
    public Task<WordPressPage<WordPressUser>> GetUsersAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressUser>("wp/v2/users", options, cancellationToken);
    public Task<JsonElement> CreateUserAsync(WordPressWriteRequest user, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/users", user, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateUserAsync(int id, WordPressWriteRequest user, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/users/{id}", user, cancellationToken: cancellationToken);
    public Task<JsonElement> DeleteUserAsync(int id, int? reassign = null, bool force = false, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Delete, $"wp/v2/users/{id}", query: new[] { new KeyValuePair<string, string?>("force", force ? "true" : "false"), new("reassign", reassign?.ToString(System.Globalization.CultureInfo.InvariantCulture)) }, cancellationToken: cancellationToken);

    public Task<JsonElement> GetApplicationPasswordsAsync(CancellationToken cancellationToken = default) => GetJsonAsync("wp/v2/users/me/application-passwords", cancellationToken);
    public Task<JsonElement> CreateApplicationPasswordAsync(WordPressWriteRequest password, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/users/me/application-passwords", password, cancellationToken: cancellationToken);
    public Task<JsonElement> GetApplicationPasswordAsync(string uuid, CancellationToken cancellationToken = default) => GetJsonAsync($"wp/v2/users/me/application-passwords/{Uri.EscapeDataString(uuid)}", cancellationToken);
    public Task<JsonElement> DeleteApplicationPasswordAsync(string uuid, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, $"wp/v2/users/me/application-passwords/{Uri.EscapeDataString(uuid)}", cancellationToken: cancellationToken);

    public Task<WordPressPage<WordPressTerm>> GetCategoriesAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressTerm>("wp/v2/categories", options, cancellationToken);
    public Task<WordPressTerm?> GetCategoryAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressTerm>($"wp/v2/categories/{id}", cancellationToken: cancellationToken);
    public Task<JsonElement> CreateCategoryAsync(WordPressWriteRequest term, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/categories", term, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateCategoryAsync(int id, WordPressWriteRequest term, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/categories/{id}", term, cancellationToken: cancellationToken);
    public Task<JsonElement> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Delete, $"wp/v2/categories/{id}", cancellationToken: cancellationToken);

    public Task<WordPressPage<WordPressTerm>> GetTagsAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<WordPressTerm>("wp/v2/tags", options, cancellationToken);
    public Task<WordPressTerm?> GetTagAsync(int id, CancellationToken cancellationToken = default) => GetAsync<WordPressTerm>($"wp/v2/tags/{id}", cancellationToken: cancellationToken);
    public Task<JsonElement> CreateTagAsync(WordPressWriteRequest term, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/tags", term, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateTagAsync(int id, WordPressWriteRequest term, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/tags/{id}", term, cancellationToken: cancellationToken);
    public Task<JsonElement> DeleteTagAsync(int id, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Delete, $"wp/v2/tags/{id}", cancellationToken: cancellationToken);

    /// <summary>Lists terms for any REST-exposed taxonomy, including custom taxonomies.</summary>
    public Task<WordPressPage<WordPressTerm>> GetTermsAsync(string restBase, WordPressListOptions? options = null, CancellationToken cancellationToken = default) =>
        GetPageAsync<WordPressTerm>(BuildResourcePath(restBase), options, cancellationToken);

    public Task<WordPressTerm?> GetTermAsync(string restBase, int id, CancellationToken cancellationToken = default) =>
        GetAsync<WordPressTerm>($"{BuildResourcePath(restBase)}/{id}", cancellationToken: cancellationToken);

    public Task<JsonElement> CreateTermAsync(string restBase, WordPressWriteRequest term, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, BuildResourcePath(restBase), term, cancellationToken: cancellationToken);

    public Task<JsonElement> UpdateTermAsync(string restBase, int id, WordPressWriteRequest term, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, $"{BuildResourcePath(restBase)}/{id}", term, cancellationToken: cancellationToken);

    public Task<JsonElement> DeleteTermAsync(string restBase, int id, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Delete, $"{BuildResourcePath(restBase)}/{id}", cancellationToken: cancellationToken);

    public Task<IReadOnlyDictionary<string, WordPressPostType>?> GetPostTypesAsync(CancellationToken cancellationToken = default) => GetAsync<IReadOnlyDictionary<string, WordPressPostType>>("wp/v2/types", cancellationToken: cancellationToken);
    public Task<IReadOnlyDictionary<string, WordPressTaxonomy>?> GetTaxonomiesAsync(CancellationToken cancellationToken = default) => GetAsync<IReadOnlyDictionary<string, WordPressTaxonomy>>("wp/v2/taxonomies", cancellationToken: cancellationToken);
    public Task<IReadOnlyDictionary<string, WordPressStatus>?> GetStatusesAsync(CancellationToken cancellationToken = default) => GetAsync<IReadOnlyDictionary<string, WordPressStatus>>("wp/v2/statuses", cancellationToken: cancellationToken);
    public Task<JsonElement> GetSettingsAsync(CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Get, "wp/v2/settings", cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateSettingsAsync(WordPressWriteRequest settings, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/settings", settings, cancellationToken: cancellationToken);

    public Task<WordPressPage<JsonElement>> GetBlocksAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<JsonElement>("wp/v2/blocks", options, cancellationToken);
    public Task<JsonElement> GetBlockAsync(int id, CancellationToken cancellationToken = default) => GetJsonAsync($"wp/v2/blocks/{id}", cancellationToken);
    public Task<JsonElement> CreateBlockAsync(WordPressWriteRequest block, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, "wp/v2/blocks", block, cancellationToken: cancellationToken);
    public Task<JsonElement> UpdateBlockAsync(int id, WordPressWriteRequest block, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Post, $"wp/v2/blocks/{id}", block, cancellationToken: cancellationToken);
    public Task<JsonElement> DeleteBlockAsync(int id, bool force = false, CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Delete, $"wp/v2/blocks/{id}", query: [new("force", force ? "true" : "false")], cancellationToken: cancellationToken);
    public Task<JsonElement> GetBlockTypesAsync(CancellationToken cancellationToken = default) => SendJsonAsync(HttpMethod.Get, "wp/v2/block-types", cancellationToken: cancellationToken);
    public Task<JsonElement> RenderBlockAsync(string blockName, WordPressWriteRequest attributes, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, $"wp/v2/block-renderer/{Uri.EscapeDataString(blockName)}", new { attributes = attributes.Properties }, cancellationToken: cancellationToken);
    public Task<WordPressPage<JsonElement>> GetThemesAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<JsonElement>("wp/v2/themes", options, cancellationToken);
    public Task<WordPressPage<JsonElement>> GetPluginsAsync(WordPressListOptions? options = null, CancellationToken cancellationToken = default) => GetPageAsync<JsonElement>("wp/v2/plugins", options, cancellationToken);
    public Task<WordPressPage<JsonElement>> SearchAsync(string search, WordPressListOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(search);
        options ??= new WordPressListOptions();
        var parameters = options.ToQueryParameters()
            .Where(parameter => !parameter.Key.Equals("search", StringComparison.OrdinalIgnoreCase))
            .Append(new KeyValuePair<string, string?>("search", search));
        return GetPageFromQueryAsync<JsonElement>("wp/v2/search", parameters, cancellationToken);
    }

    public Task<WordPressPage<JsonElement>> GetBlockDirectoryItemsAsync(string search, WordPressListOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(search);
        options ??= new WordPressListOptions();
        var parameters = options.ToQueryParameters()
            .Where(parameter => !parameter.Key.Equals("search", StringComparison.OrdinalIgnoreCase))
            .Append(new KeyValuePair<string, string?>("search", search));
        return GetPageFromQueryAsync<JsonElement>("wp/v2/block-directory/search", parameters, cancellationToken);
    }

    public Task<JsonElement> GetRenderedBlockAsync(string blockName, WordPressWriteRequest attributes, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, $"wp/v2/block-renderer/{Uri.EscapeDataString(blockName)}",
            query: [new("attributes", JsonSerializer.Serialize(attributes.Properties, WordPressJson.DefaultOptions))], cancellationToken: cancellationToken);

    public Task<JsonElement> GetBlockAutosavesAsync(int blockId, CancellationToken cancellationToken = default) =>
        GetJsonAsync($"wp/v2/blocks/{blockId}/autosaves", cancellationToken);

    private async Task<WordPressPage<T>> GetPageFromQueryAsync<T>(string route, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, route, null, query, cancellationToken).ConfigureAwait(false);
        var items = await ReadArrayAsync<T>(response, cancellationToken).ConfigureAwait(false);
        return new WordPressPage<T>(items, ReadHeaderInteger(response, "X-WP-Total"), ReadHeaderInteger(response, "X-WP-TotalPages"));
    }

    private async Task<WordPressPage<T>> SendCollectionAsync<T>(string route, WordPressListOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, route, null, options?.ToQueryParameters(), cancellationToken).ConfigureAwait(false);
        var items = await ReadArrayAsync<T>(response, cancellationToken).ConfigureAwait(false);
        return new WordPressPage<T>(items, ReadHeaderInteger(response, "X-WP-Total"), ReadHeaderInteger(response, "X-WP-TotalPages"));
    }

    private Task<JsonElement> GetJsonAsync(string route, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Get, route, cancellationToken: cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, object? payload, IEnumerable<KeyValuePair<string, string?>>? query, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureRestRootAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(method, BuildUri(route, query));
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: WordPressJson.DefaultOptions);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return await ValidateAndDetachResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private Task EnsureRestRootAsync(CancellationToken cancellationToken) =>
        _restRoot is null ? DiscoverAsync(cancellationToken) : Task.CompletedTask;

    private static async Task<HttpResponseMessage> ValidateAndDetachResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_authorization is not null && CanSendCredentials(uri))
            request.Headers.Authorization = _authorization;
        return request;
    }

    private Uri BuildUri(string route, IEnumerable<KeyValuePair<string, string?>>? query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (Uri.TryCreate(route, UriKind.Absolute, out _))
            throw new ArgumentException("REST route must be relative to the configured WordPress site.", nameof(route));
        if (route.Contains('?') || route.Contains('#'))
            throw new ArgumentException("Pass query parameters separately and do not include a URL fragment in the REST route.", nameof(route));

        var root = _restRoot ?? new Uri(_siteUri, "wp-json/");
        var routeWithoutLeadingSlash = route.TrimStart('/');
        if (routeWithoutLeadingSlash.Split('/').Any(segment => segment == ".."))
            throw new ArgumentException("REST route cannot contain path traversal segments.", nameof(route));
        Uri uri;
        var queryParameters = query?.ToList() ?? [];
        var existingRestRoute = ParseQuery(root.Query).FirstOrDefault(pair => pair.Key == "rest_route");
        if (existingRestRoute.Key is not null)
        {
            var path = string.IsNullOrEmpty(existingRestRoute.Value) ? string.Empty : existingRestRoute.Value.TrimEnd('/');
            queryParameters.Add(new("rest_route", $"{path}/{routeWithoutLeadingSlash}"));
            uri = new UriBuilder(root) { Query = string.Empty }.Uri;
        }
        else
        {
            uri = new Uri(root, routeWithoutLeadingSlash);
        }

        var queryString = queryParameters.Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}")
            .ToArray();
        if (queryString is { Length: > 0 })
        {
            var rootQuery = existingRestRoute.Key is null ? uri.Query.TrimStart('?') : string.Join("&", ParseQuery(root.Query)
                .Where(pair => pair.Key != "rest_route")
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
            var allQuery = string.Join("&", new[] { rootQuery }.Where(value => !string.IsNullOrEmpty(value)).Concat(queryString));
            uri = new UriBuilder(uri) { Query = allQuery }.Uri;
        }
        return uri;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator < 0 ? part : part[..separator];
            var value = separator < 0 ? string.Empty : part[(separator + 1)..];
            yield return new KeyValuePair<string, string>(Uri.UnescapeDataString(key.Replace('+', ' ')), Uri.UnescapeDataString(value.Replace('+', ' ')));
        }
    }

    private bool CanSendCredentials(Uri uri) =>
        uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
        uri.IdnHost.Equals(_siteUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == _siteUri.Port;

    private static string BuildResourcePath(string restBase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restBase);
        var resource = restBase.Trim('/');
        if (resource.Contains("..", StringComparison.Ordinal) || resource.Contains('?') || resource.Contains('#') || Uri.TryCreate(resource, UriKind.Absolute, out _))
            throw new ArgumentException("Resource REST base must be a relative route without query or fragment.", nameof(restBase));
        return resource.Contains('/') || resource.StartsWith("wp/", StringComparison.Ordinal) ? resource : $"wp/v2/{resource}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        JsonElement? data = null;
        var code = "wordpress_error";
        var message = $"WordPress REST request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("code", out var codeNode) && codeNode.ValueKind == JsonValueKind.String) code = codeNode.GetString() ?? code;
                if (root.TryGetProperty("message", out var messageNode) && messageNode.ValueKind == JsonValueKind.String) message = messageNode.GetString() ?? message;
                if (root.TryGetProperty("data", out var dataNode)) data = dataNode.Clone();
            }
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        throw new WordPressApiException(response.StatusCode, code, message, data);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null || response.Content.Headers.ContentLength == 0)
            return JsonDocument.Parse("null").RootElement.Clone();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    private static async Task<List<T>> ReadArrayAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null || response.Content.Headers.ContentLength == 0)
            return [];
        var items = await response.Content.ReadFromJsonAsync<List<T>>(WordPressJson.DefaultOptions, cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    private static async Task<T?> ReadContentAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null || response.Content.Headers.ContentLength == 0)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(WordPressJson.DefaultOptions, cancellationToken).ConfigureAwait(false);
    }

    private static int? ReadHeaderInteger(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var value) ? value : null;

    private static string? GetHeaderLink(HttpResponseMessage response, string relation)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        foreach (var value in values)
        {
            var position = 0;
            while ((position = value.IndexOf('<', position)) >= 0)
            {
                var end = value.IndexOf('>', position + 1);
                if (end < 0) break;
                var link = value[(position + 1)..end];
                var next = value.IndexOf('<', end + 1);
                var parameters = value[(end + 1)..(next < 0 ? value.Length : next)];
                if (parameters.Contains($"rel=\"{relation}\"", StringComparison.OrdinalIgnoreCase) ||
                    parameters.Contains($"rel={relation}", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(link, UriKind.Absolute, out var absolute)) return absolute.ToString();
                    return link;
                }
                position = end + 1;
            }
        }
        return null;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            return uri;
        return new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
