namespace JorgenHoc.DataAccess.EfCoreManyToMany;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;

    // Many-to-many: a tag appears on many posts
    public List<Post> Posts { get; set; } = [];
}
