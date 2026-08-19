namespace JorgenHoc.DataAccess.EfCoreOneToMany;

/// <summary>
/// The article's model, verbatim — the "one" side. Navigations are <c>virtual</c> so the
/// same entities work for the lazy-loading measurement (proxies require it); that is the
/// only difference from the article's opening snippet, and the article itself introduces
/// <c>virtual</c> in its lazy-loading section.
/// </summary>
public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Collection navigation property (the "many" side)
    public virtual List<Product> Products { get; set; } = [];
}
