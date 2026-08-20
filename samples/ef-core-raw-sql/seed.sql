-- Seed for samples/ef-core-raw-sql (article: /blog/ef-core-raw-sql).
-- Run against the JorgenHocSamples database:
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
-- Re-running drops and recreates everything, so counts always match the README.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF SCHEMA_ID('RawSql') IS NULL
    EXEC('CREATE SCHEMA RawSql');

IF OBJECT_ID('RawSql.GetProductsByCategory', 'P') IS NOT NULL DROP PROCEDURE RawSql.GetProductsByCategory;
IF OBJECT_ID('RawSql.CountProductsAbove', 'P') IS NOT NULL DROP PROCEDURE RawSql.CountProductsAbove;
IF OBJECT_ID('RawSql.Products', 'U') IS NOT NULL DROP TABLE RawSql.Products;
IF OBJECT_ID('RawSql.Categories', 'U') IS NOT NULL DROP TABLE RawSql.Categories;

CREATE TABLE RawSql.Categories
(
    Id   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RawSql_Categories PRIMARY KEY,
    Name nvarchar(200) NOT NULL
);

CREATE TABLE RawSql.Products
(
    Id         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RawSql_Products PRIMARY KEY,
    Name       nvarchar(200) NOT NULL,
    Price      decimal(18,2) NOT NULL,
    CategoryId int NOT NULL
        CONSTRAINT FK_RawSql_Products_Categories REFERENCES RawSql.Categories (Id)
);

INSERT INTO RawSql.Categories (Name)
VALUES ('Boards'), ('Cables'), ('Displays');

-- 4 products per category; two per category priced above 50 so filtered demos have
-- predictable counts. Category ids are 1..3 (fresh IDENTITY after the drop above).
INSERT INTO RawSql.Products (Name, Price, CategoryId)
VALUES
    ('Dev Board A',    25.00, 1),
    ('Dev Board B',    45.00, 1),
    ('Dev Board Pro',  75.00, 1),
    ('Dev Board Max', 120.00, 1),
    ('Cable Basic',     5.00, 2),
    ('Cable Braided',  15.00, 2),
    ('Cable Optical',  55.00, 2),
    ('Cable Active',   85.00, 2),
    ('Display 24',     35.00, 3),
    ('Display 27',     50.00, 3),
    ('Display 32',     95.00, 3),
    ('Display Ultra', 240.00, 3);
GO

-- The article's "stored procedure that returns entities" — column shape must match the
-- Product entity exactly for FromSqlRaw to materialize it.
CREATE PROCEDURE RawSql.GetProductsByCategory
    @CategoryId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Price, CategoryId
    FROM RawSql.Products
    WHERE CategoryId = @CategoryId;
END
GO

-- The article's OUTPUT-parameter shape.
CREATE PROCEDURE RawSql.CountProductsAbove
    @MinPrice decimal(18,2),
    @TotalCount int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @TotalCount = COUNT(*)
    FROM RawSql.Products
    WHERE Price > @MinPrice;
END
GO

SELECT
    (SELECT COUNT(*) FROM RawSql.Categories) AS Categories,
    (SELECT COUNT(*) FROM RawSql.Products)   AS Products,
    (SELECT COUNT(*) FROM RawSql.Products WHERE Price > 50) AS ProductsOver50;
