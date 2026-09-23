namespace Zenith.Core.Registry;

public enum RegistrySourceType { Curated, OfficialDocumentation, ObservedDependency, UserAdded, Community }

/// <summary>Recorded review information, not a trust level or authorization.</summary>
public sealed record RelationshipReview
{
    public RelationshipReview(string reviewedBy, DateTimeOffset reviewedAt)
    {
        ReviewedBy = RegistryFields.Text(reviewedBy, nameof(ReviewedBy), 200);
        if (reviewedAt < DateTimeOffset.UnixEpoch)
            throw new ArgumentException("Review date must be on or after the Unix epoch.", nameof(reviewedAt));
        ReviewedAt = reviewedAt;
    }

    public string ReviewedBy { get; }
    public DateTimeOffset ReviewedAt { get; }
}

/// <summary>Evidence about one relationship. References are inert text, never fetched.</summary>
public sealed record RelationshipProvenance
{
    public RelationshipProvenance(RegistrySourceType sourceType, string? sourceReference = null,
        RelationshipReview? review = null, string? explanation = null)
    {
        if (!Enum.IsDefined(sourceType)) throw new ArgumentException("Unknown registry source type.", nameof(sourceType));
        SourceType = sourceType;
        SourceReference = sourceReference is null ? null : RegistryFields.Text(sourceReference, nameof(SourceReference));
        Review = review;
        Explanation = explanation is null ? null : RegistryFields.Text(explanation, nameof(Explanation));
    }

    public RegistrySourceType SourceType { get; }
    public string? SourceReference { get; }
    public RelationshipReview? Review { get; }
    public string? Explanation { get; }
}
