namespace JorgenHoc.DataAccess.EfCoreNPlusOne;

public class Order
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public List<OrderLine> Lines { get; set; } = [];
}
