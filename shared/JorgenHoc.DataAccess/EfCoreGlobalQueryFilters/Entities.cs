namespace JorgenHoc.DataAccess.EfCoreGlobalQueryFilters;

// The article's entity hierarchy, verbatim: soft-deletable -> auditable -> tenant-owned.

public abstract class SoftDeletableEntity
{
    public int Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

public abstract class AuditableEntity : SoftDeletableEntity
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}

public abstract class TenantEntity : AuditableEntity
{
    public Guid TenantId { get; set; }
}

public class Blog : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class Post : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
}

public class Invoice : TenantEntity
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime IssuedAt { get; set; }
    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}

public class InvoiceLineItem : TenantEntity
{
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
}

public class Customer : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    // Owned — loaded with Customer, filter on Customer covers access
    public Address BillingAddress { get; set; } = null!;
}

public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
}

/// <summary>
/// The article's tenant abstraction. Production resolves this from the HTTP context;
/// the sample uses a mutable implementation so query-time evaluation can be observed.
/// </summary>
public interface ITenantProvider
{
    Guid TenantId { get; }
}
