namespace WordPressSharp;

/// <summary>A page of results and WordPress pagination metadata.</summary>
public sealed record WordPressPage<T>(IReadOnlyList<T> Items, int? TotalItems, int? TotalPages);
