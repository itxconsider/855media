using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.Resolving;

public record QueryResult(
    QueryResultKind Kind,
    string Title,
    IReadOnlyList<VideoInfo> Videos,
    string? ProfilePictureUrl = null,
    string? AuthorName = null
)
{
    public static QueryResult Aggregate(IReadOnlyList<QueryResult> results)
    {
        if (!results.Any())
            throw new ArgumentException("Cannot aggregate empty results.", nameof(results));

        return new QueryResult(
            // Single query -> inherit kind, multiple queries -> aggregate
            results.Count == 1
                ? results.Single().Kind
                : QueryResultKind.Aggregate,
            // Single query -> inherit title, multiple queries -> aggregate
            results.Count == 1
                ? results.Single().Title
                : $"{results.Count} queries",
            // Combine all videos, deduplicate by ID
            results.SelectMany(q => q.Videos).DistinctBy(v => (v.Source, v.Id)).ToArray(),
            results
                .FirstOrDefault(r => !string.IsNullOrEmpty(r.ProfilePictureUrl))
                ?.ProfilePictureUrl,
            results.FirstOrDefault(r => !string.IsNullOrEmpty(r.AuthorName))?.AuthorName
        );
    }
}
