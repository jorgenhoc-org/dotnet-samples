namespace JorgenHoc.DataAccess.EfCoreNPlusOne;

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
