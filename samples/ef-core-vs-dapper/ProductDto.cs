namespace JorgenHoc.EfCoreVsDapper;

/// <summary>
/// The article's projection DTO. Record equality is what lets the sample compare EF's
/// and Dapper's result sets directly with <c>SequenceEqual</c>; Dapper maps it via the
/// constructor.
/// </summary>
public record ProductDto(int Id, string Name, decimal Price, string CategoryName);
