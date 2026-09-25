namespace WordPressSharp;

/// <summary>Configuration for a WordPress REST API client.</summary>
public sealed class WordPressClientOptions
{
    /// <summary>The site root URL, for example https://example.com/.</summary>
    public required Uri SiteUri { get; init; }

    /// <summary>Optional REST API root override. Defaults to the site's discovered REST root.</summary>
    public Uri? RestRoot { get; init; }

    /// <summary>Application Password username. Set both credentials to enable Basic authentication.</summary>
    public string? Username { get; init; }

    /// <summary>WordPress Application Password. Set both credentials to enable Basic authentication.</summary>
    public string? ApplicationPassword { get; init; }

    /// <summary>Timeout applied to the internally-created HttpClient.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    internal void Validate()
    {
        if (!SiteUri.IsAbsoluteUri || !SiteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !SiteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("SiteUri must be an absolute HTTP or HTTPS URL.", nameof(SiteUri));

        if ((Username is null) != (ApplicationPassword is null))
            throw new ArgumentException("Username and ApplicationPassword must either both be set or both be omitted.");

        if (Username is not null && !SiteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Application Password authentication requires an HTTPS site URL.", nameof(SiteUri));

        if (RestRoot is not null && !RestRoot.IsAbsoluteUri)
            throw new ArgumentException("RestRoot must be an absolute URL.", nameof(RestRoot));

        if (RestRoot is not null && !RestRoot.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !RestRoot.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("RestRoot must use HTTP or HTTPS.", nameof(RestRoot));

        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive or infinite.");
    }
}
