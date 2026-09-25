using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WordPressSharp;
using WordPressSharp.Models;
using Xunit;

namespace WordPressSharpTest;

public sealed class WordPressClientTests
{
    [Fact]
    public async Task GetPageAsync_BuildsUrlAndReadsPaginationHeaders()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
                return Task.FromResult(RestDiscoveryResponse("https://example.test/wp-json/"));
            if (request.RequestUri.AbsolutePath == "/wp-json/")
                return Task.FromResult(JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{}}"));
            Assert.Equal("/wp-json/wp/v2/posts", request.RequestUri!.AbsolutePath);
            Assert.Equal("https://example.test/wp-json/wp/v2/posts?page=2&per_page=25&search=hello%20world", request.RequestUri.AbsoluteUri);
            return Task.FromResult(JsonResponse("[]", headers: new Dictionary<string, string>
            {
                ["X-WP-Total"] = "51",
                ["X-WP-TotalPages"] = "3"
            }));
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions { SiteUri = new Uri("https://example.test") }, http);

        var page = await client.GetPostsAsync(new WordPressListOptions { Page = 2, PerPage = 25, Search = "hello world" });

        Assert.Empty(page.Items);
        Assert.Equal(51, page.TotalItems);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task Constructor_AddsApplicationPasswordBasicAuthentication()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/blog/")
                return Task.FromResult(RestDiscoveryResponse("https://example.test/blog/wp-json/"));
            if (request.RequestUri.AbsolutePath.EndsWith("/wp-json/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{}}"));
            Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:app pass")), request.Headers.Authorization?.ToString());
            return Task.FromResult(JsonResponse("{}"));
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions
        {
            SiteUri = new Uri("https://example.test/blog"),
            Username = "alice",
            ApplicationPassword = "app pass"
        }, http);

        await client.GetAsync<JsonElement>("wp/v2/settings");
    }

    [Fact]
    public void ApplicationPassword_RejectsInsecureHttp()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse("{}"));
        });
        using var http = new HttpClient(handler);
        Assert.Throws<ArgumentException>(() => new WordPressClient(new WordPressClientOptions
        {
            SiteUri = new Uri("http://example.test"),
            Username = "alice",
            ApplicationPassword = "secret"
        }, http));
    }

    [Fact]
    public async Task SendJsonAsync_SerializesWritePayloadAndEncodesQuery()
    {
        var handler = new RecordingHandler(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
                return RestDiscoveryResponse("https://example.test/wp-json/");
            if (request.RequestUri.AbsolutePath == "/wp-json/")
                return JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{}} ");
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://example.test/wp-json/custom/v1/items?filter=a%26b", request.RequestUri!.ToString());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("Draft", json.RootElement.GetProperty("title").GetString());
            Assert.Equal(7, json.RootElement.GetProperty("featured_media").GetInt32());
            return JsonResponse("{\"ok\":true}");
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions { SiteUri = new Uri("https://example.test") }, http);

        var result = await client.SendJsonAsync(HttpMethod.Post, "custom/v1/items",
            WordPressPayload.Create(new { Title = "Draft", FeaturedMedia = 7 }),
            [new KeyValuePair<string, string?>("filter", "a&b")]);

        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task FailedRequest_ThrowsStructuredWordPressError()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
                return Task.FromResult(RestDiscoveryResponse("https://example.test/wp-json/"));
            if (request.RequestUri.AbsolutePath == "/wp-json/")
                return Task.FromResult(JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{}}"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"code\":\"rest_forbidden\",\"message\":\"Sorry, you cannot do that.\",\"data\":{\"status\":403}}", Encoding.UTF8, "application/json")
            });
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions { SiteUri = new Uri("https://example.test") }, http);

        var error = await Assert.ThrowsAsync<WordPressApiException>(() => client.GetAsync<JsonElement>("wp/v2/settings"));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("rest_forbidden", error.Code);
        Assert.Equal("Sorry, you cannot do that.", error.Message);
        Assert.Equal(403, error.ErrorData!.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task DiscoverAsync_UsesWordPressRestLinkHeader()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/site/")
            {
                var home = JsonResponse("<html></html>");
                home.Headers.TryAddWithoutValidation("Link", "<https://example.test/site/index.php?rest_route=/>; rel=\"https://api.w.org/\"");
                return Task.FromResult(home);
            }

            Assert.Equal("https://example.test/site/index.php?rest_route=/", request.RequestUri.AbsoluteUri);
            return Task.FromResult(JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{\"/wp/v2/posts\":{}}}"));
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions { SiteUri = new Uri("https://example.test/site") }, http);

        var index = await client.DiscoverAsync();

        Assert.Contains("wp/v2", index.Namespaces!);
        Assert.Equal("https://example.test/site/index.php?rest_route=/", client.RestRoot!.ToString());
    }

    [Fact]
    public void Options_RequiresCredentialsAsAPair()
    {
        Assert.Throws<ArgumentException>(() => new WordPressClient(new WordPressClientOptions
        {
            SiteUri = new Uri("https://example.test"),
            Username = "alice"
        }));
    }

    [Fact]
    public async Task CustomContentType_UsesRegisteredRestBase()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
                return Task.FromResult(RestDiscoveryResponse("https://example.test/wp-json/"));
            if (request.RequestUri.AbsolutePath == "/wp-json/")
                return Task.FromResult(JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{}}"));
            Assert.Equal("https://example.test/wp-json/wp/v2/books/12", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(JsonResponse("{\"id\":12}"));
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions { SiteUri = new Uri("https://example.test") }, http);

        var book = await client.GetContentAsync<JsonElement>("books", 12);

        Assert.Equal(12, book.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task DeleteRequest_CanReturnEmptyBody()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/")
                return Task.FromResult(RestDiscoveryResponse("https://example.test/wp-json/"));
            if (request.RequestUri.AbsolutePath == "/wp-json/")
                return Task.FromResult(JsonResponse("{\"namespaces\":[\"wp/v2\"],\"routes\":{}}"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        using var http = new HttpClient(handler);
        using var client = new WordPressClient(new WordPressClientOptions { SiteUri = new Uri("https://example.test") }, http);

        var response = await client.SendJsonAsync(HttpMethod.Delete, "custom/v1/resource/1");

        Assert.Equal(JsonValueKind.Null, response.ValueKind);
    }

    private static HttpResponseMessage JsonResponse(string json, IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (headers is not null)
            foreach (var header in headers) response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return response;
    }

    private static HttpResponseMessage RestDiscoveryResponse(string restRoot)
    {
        var response = JsonResponse("<html></html>");
        response.Headers.TryAddWithoutValidation("Link", $"<{restRoot}>; rel=\"https://api.w.org/\"");
        return response;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
