namespace JorgenHoc.DataAccess.EfCoreRawSql;

// The article's Product/Category pair. Namespaced per article as usual — the raw SQL in
// the sample addresses the tables as RawSql.Products / RawSql.Categories.

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
