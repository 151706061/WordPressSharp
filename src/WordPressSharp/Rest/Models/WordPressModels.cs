using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordPressSharp.Models;

/// <summary>Base representation of a WordPress content object.</summary>
public class WordPressContent
{
    public int Id { get; set; }
    public DateTimeOffset? Date { get; set; }
    public DateTimeOffset? DateGmt { get; set; }
    public GuidValue? Guid { get; set; }
    public DateTimeOffset? Modified { get; set; }
    public DateTimeOffset? ModifiedGmt { get; set; }
    public string? Slug { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? Link { get; set; }
    public RenderedField? Title { get; set; }
    public RenderedField? Content { get; set; }
    public RenderedField? Excerpt { get; set; }
    public int? Author { get; set; }
    public int? FeaturedMedia { get; set; }
    public string? CommentStatus { get; set; }
    public string? PingStatus { get; set; }
    public bool? Sticky { get; set; }
    public string? Template { get; set; }
    public int? Parent { get; set; }
    public Dictionary<string, JsonElement>? Meta { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Rendered value wrapper used by WordPress for title, content, and excerpt.</summary>
public sealed class RenderedField
{
    public string? Rendered { get; set; }
    public bool? Protected { get; set; }
    public bool? Raw { get; set; }
}

public sealed class GuidValue
{
    public string? Rendered { get; set; }
    public bool? Raw { get; set; }
}

/// <summary>WordPress category or tag representation.</summary>
public class WordPressTerm
{
    public int Id { get; set; }
    public int? Count { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Taxonomy { get; set; }
    public int? Parent { get; set; }
    public JsonElement Meta { get; set; }
}

/// <summary>WordPress media representation.</summary>
public sealed class WordPressMedia : WordPressContent
{
    public string? MediaType { get; set; }
    public string? MimeType { get; set; }
    public string? SourceUrl { get; set; }
    public RenderedField? Caption { get; set; }
    public RenderedField? Description { get; set; }
    public Dictionary<string, MediaSize>? MediaDetails { get; set; }
}

public sealed class MediaSize
{
    public string? File { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? MimeType { get; set; }
    public string? SourceUrl { get; set; }
}

/// <summary>WordPress comment representation.</summary>
public sealed class WordPressComment
{
    public int Id { get; set; }
    public DateTimeOffset? Date { get; set; }
    public DateTimeOffset? DateGmt { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public int? Post { get; set; }
    public int? Parent { get; set; }
    public int? Author { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorEmail { get; set; }
    public string? AuthorUrl { get; set; }
    public string? AuthorIp { get; set; }
    public RenderedField? Content { get; set; }
    public string? Link { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>WordPress user representation.</summary>
public sealed class WordPressUser
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public string? Slug { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Nickname { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Locale { get; set; }
    public string?[]? Roles { get; set; }
    public Dictionary<string, JsonElement>? Capabilities { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>A registered WordPress post type.</summary>
public sealed class WordPressPostType
{
    public string? Slug { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? Hierarchical { get; set; }
    public bool? Viewable { get; set; }
    public string? RestBase { get; set; }
    public string? RestNamespace { get; set; }
    public Dictionary<string, JsonElement>? Supports { get; set; }
}

/// <summary>A registered taxonomy.</summary>
public sealed class WordPressTaxonomy
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public bool? Hierarchical { get; set; }
    public string? RestBase { get; set; }
    public string? RestNamespace { get; set; }
    public string[]? Types { get; set; }
}

/// <summary>A registered WordPress status.</summary>
public sealed class WordPressStatus
{
    public string? Name { get; set; }
    public string? Label { get; set; }
    public bool? Public { get; set; }
    public bool? Private { get; set; }
    public bool? Protected { get; set; }
    public bool? Internal { get; set; }
    public bool? PubliclyQueryable { get; set; }
    public bool? ShowInList { get; set; }
}

/// <summary>REST API discovery index. Route schemas are preserved as JSON for plugin extensibility.</summary>
public sealed class WordPressApiIndex
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? Home { get; set; }
    public JsonElement GmtOffset { get; set; }
    public string? TimezoneString { get; set; }
    public string[]? Namespaces { get; set; }
    public Dictionary<string, JsonElement>? Routes { get; set; }
}

/// <summary>Options for paged collection requests.</summary>
public sealed class WordPressListOptions
{
    public int? Page { get; set; }
    public int? PerPage { get; set; }
    public string? Search { get; set; }
    public string? Order { get; set; }
    public string? OrderBy { get; set; }
    public string? Context { get; set; }
    public string? Status { get; set; }
    public Dictionary<string, string?> AdditionalParameters { get; } = new(StringComparer.Ordinal);

    internal IEnumerable<KeyValuePair<string, string?>> ToQueryParameters()
    {
        if (Page is not null) yield return new("page", Page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (PerPage is not null) yield return new("per_page", PerPage.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Search is not null) yield return new("search", Search);
        if (Order is not null) yield return new("order", Order);
        if (OrderBy is not null) yield return new("orderby", OrderBy);
        if (Context is not null) yield return new("context", Context);
        if (Status is not null) yield return new("status", Status);
        foreach (var parameter in AdditionalParameters) yield return parameter;
    }
}

/// <summary>A payload for creating or updating content, terms, users, comments, or other REST resources.</summary>
public sealed class WordPressWriteRequest
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Returns serialized payload properties in a convenient initializer-friendly dictionary.</summary>
public static class WordPressPayload
{
    public static WordPressWriteRequest Create(object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var json = JsonSerializer.SerializeToElement(values, WordPressJson.DefaultOptions);
        var properties = json.Deserialize<Dictionary<string, JsonElement>>(WordPressJson.DefaultOptions)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return new WordPressWriteRequest { Properties = properties };
    }
}
