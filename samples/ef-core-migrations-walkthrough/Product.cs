namespace JorgenHoc.MigrationsWalkthrough;

/// <summary>
/// The entity the walkthrough evolves one migration at a time. Its history is the point:
/// InitialCreate ships Name/Price/CreatedAt, AddProductDescription adds Description,
/// AddIndexOnProductName indexes Name, and AddProductSlug is a HAND-EDITED migration that
/// backfills Slug from Name before making it non-nullable. Read the files under
/// Migrations/ to see each delta.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }

    // Added by the AddProductDescription migration.
    public string? Description { get; set; }

    // Added (nullable) by AddProductSlug, then made non-nullable in the same migration
    // after a data backfill — so the column is non-null here to match the final schema.
    public string Slug { get; set; } = string.Empty;
}
