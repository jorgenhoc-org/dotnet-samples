namespace JorgenHoc.DataAccess.EfCoreVsDapper;

/// <summary>
/// The article's shared model, verbatim. Dapper maps the four columns and leaves
/// <see cref="Category"/> null unless multi-mapping fills it; EF Core fills it via
/// <c>Include</c> or projection.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}
