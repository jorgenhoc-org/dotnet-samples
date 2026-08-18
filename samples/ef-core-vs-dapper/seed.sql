/*
================================================================================
  Sample schema and data for: "EF Core vs Dapper — Which ORM Should You Use?"
  https://www.jorgenhoc.org/en/blog/ef-core-vs-dapper

  Run this against the JorgenHocSamples database, then run the console sample in
  this folder and/or the EfCoreVsDapperBenchmark in benchmarks/.

  Row counts are chosen to match the article's benchmark scenarios exactly:

    - 20 categories
    - 1,000 products              (the "simple SELECT, 1,000 rows" scenario)
    - exactly 500 with Price > 10 (the "JOIN projection, 500 rows" scenario —
                                   odd rows are 5.99, even rows are 15.99)

  Target: SQL Server 2016 or later. Safe to re-run: tables are dropped first.
  Coexists with the other articles' tables in the same database — nothing here
  touches Customers/Orders/OrderLines.
================================================================================
*/

SET NOCOUNT ON;

DROP TABLE IF EXISTS dbo.Products;
DROP TABLE IF EXISTS dbo.Categories;

CREATE TABLE dbo.Categories
(
    Id   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
    Name nvarchar(100)     NOT NULL
);

CREATE TABLE dbo.Products
(
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    Name       nvarchar(200)     NOT NULL,
    Price      decimal(18,2)     NOT NULL,
    CategoryId int               NOT NULL
        CONSTRAINT FK_Products_Categories REFERENCES dbo.Categories (Id)
);

CREATE INDEX IX_Products_CategoryId ON dbo.Products (CategoryId);

-------------------------------------------------------------------------------
-- 20 categories
-------------------------------------------------------------------------------
;WITH N AS
(
    SELECT TOP (20) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.objects
)
INSERT INTO dbo.Categories (Name)
SELECT CONCAT('Category ', RIGHT(CONCAT('0', n), 2))
FROM N;

-------------------------------------------------------------------------------
-- 1,000 products: odd ids 5.99, even ids 15.99 → exactly 500 rows > 10
-------------------------------------------------------------------------------
;WITH N AS
(
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.objects a CROSS JOIN sys.objects b
)
INSERT INTO dbo.Products (Name, Price, CategoryId)
SELECT CONCAT('Product ', RIGHT(CONCAT('000', n), 4)),
       CASE WHEN n % 2 = 0 THEN 15.99 ELSE 5.99 END,
       ((n - 1) % 20) + 1
FROM N;

SELECT (SELECT COUNT(*) FROM dbo.Categories)                     AS Categories,
       (SELECT COUNT(*) FROM dbo.Products)                      AS Products,
       (SELECT COUNT(*) FROM dbo.Products WHERE Price > 10)     AS ProductsOver10;
