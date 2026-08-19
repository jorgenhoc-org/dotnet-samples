/*
================================================================================
  Sample schema and data for: "EF Core One-to-Many Relationships"
  https://www.jorgenhoc.org/en/blog/ef-core-one-to-many

  Run this against the JorgenHocSamples database, then run the console sample
  in this folder.

  5 categories x 4 products each = 20 products. Prices within each category are
  25 / 50 / 75 / 100, so the article's filtered-Include example (Price > 50)
  keeps exactly 2 products per category.

  Tables live in their own [OneToMany] schema — dbo.Products/dbo.Categories
  belong to the ef-core-vs-dapper sample. Safe to re-run: tables are dropped
  first. Re-run it whenever you want to reset what the console sample writes.
================================================================================
*/

SET NOCOUNT ON;

IF SCHEMA_ID('OneToMany') IS NULL
    EXEC('CREATE SCHEMA OneToMany');

DROP TABLE IF EXISTS OneToMany.Products;
DROP TABLE IF EXISTS OneToMany.Categories;

CREATE TABLE OneToMany.Categories
(
    Id   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OneToMany_Categories PRIMARY KEY,
    Name nvarchar(100)     NOT NULL
);

CREATE TABLE OneToMany.Products
(
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OneToMany_Products PRIMARY KEY,
    Name       nvarchar(200)     NOT NULL,
    Price      decimal(18,2)     NOT NULL,
    CategoryId int               NOT NULL
        CONSTRAINT FK_OneToMany_Products_Categories REFERENCES OneToMany.Categories (Id)
);

CREATE INDEX IX_OneToMany_Products_CategoryId ON OneToMany.Products (CategoryId);

INSERT INTO OneToMany.Categories (Name)
VALUES ('Keyboards'), ('Mice'), ('Monitors'), ('Cables'), ('Audio');

-- 4 products per category, priced 25 / 50 / 75 / 100.
;WITH N AS
(
    SELECT c.Id AS CategoryId, c.Name AS CategoryName, v.n
    FROM OneToMany.Categories c
    CROSS JOIN (VALUES (1), (2), (3), (4)) v(n)
)
INSERT INTO OneToMany.Products (Name, Price, CategoryId)
SELECT CONCAT(CategoryName, ' - Product ', n), n * 25.00, CategoryId
FROM N
ORDER BY CategoryId, n;

SELECT (SELECT COUNT(*) FROM OneToMany.Categories)                 AS Categories,
       (SELECT COUNT(*) FROM OneToMany.Products)                   AS Products,
       (SELECT COUNT(*) FROM OneToMany.Products WHERE Price > 50)  AS ProductsOver50;
