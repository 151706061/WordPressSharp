using System.Net;
using System.Text.Json;

namespace WordPressSharp;

/// <summary>Represents a JSON error response returned by WordPress REST.</summary>
public sealed class WordPressApiException : Exception
{
    public WordPressApiException(HttpStatusCode statusCode, string code, string message, JsonElement? data = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        ErrorData = data;
    }

    public HttpStatusCode StatusCode { get; }
    public string Code { get; }
    public JsonElement? ErrorData { get; }
}
