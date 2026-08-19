namespace JorgenHoc.DataAccess.EfCoreOneToMany;

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }

    // Foreign key property
    public int CategoryId { get; set; }

    // Reference navigation property (the "one" side)
    public virtual Category Category { get; set; } = null!;
}
