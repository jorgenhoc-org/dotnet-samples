namespace JorgenHoc.DataAccess.EfCoreManyToMany;

/// <summary>
/// The article's implicit many-to-many pair: no join entity class, EF Core manages the
/// PostTag junction table on its own.
/// </summary>
public class Post
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public DateTime PublishedAt { get; set; }

    // Many-to-many: a post has many tags
    public List<Tag> Tags { get; set; } = [];
}
