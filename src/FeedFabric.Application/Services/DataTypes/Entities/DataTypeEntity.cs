namespace FeedFabric.Application.Services.DataTypes;

// A kind of structured data the host's apps attach to posts, such as a weight reading, with the JSON Schema every
// post's data of this type must match. Saving it again bumps Version; posts keep the version they were checked against.
public class DataTypeEntity : EntityBase
{
    // "weight-reading": what posts name in data.type
    public required string Key { get; init; }

    public required string Label { get; set; }

    public string? Description { get; set; }

    // The JSON Schema, as JSON
    public required string Schema { get; set; }

    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; }

    // The external user ID of whoever saved it last
    public string? UpdatedBy { get; set; }
}
