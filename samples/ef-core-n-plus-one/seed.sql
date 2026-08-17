/*
================================================================================
  Sample schema and data for: "EF Core Performance: Solving the N+1 Query Problem"
  https://www.jorgenhoc.org/en/blog/ef-core-n-plus-one

  Run this against an empty database, point the article's AppDbContext at it, and
  you will reproduce the statement counts published in that article:

    | Strategy                             | SQL statements |
    |--------------------------------------|----------------|
    | Lazy access (N+1, customer only)     |            501 |
    | Lazy access (N+1, customer + lines)  |          1,001 |
    | Include (eager, single query)        |              1 |
    | Include + AsSplitQuery()             |              2 |
    | Select() projection                  |              1 |

  Those counts follow from the row counts below, so keep @OrderCount at 500 if you
  want the numbers to match. Change it and the counts move with it: lazy access on
  one navigation is always 1 + N, and on two navigations always 1 + 2N.

  Target: SQL Server 2016 or later (tested on 2022).
  For PostgreSQL: replace IDENTITY with GENERATED ALWAYS AS IDENTITY, NVARCHAR
  with TEXT, DATETIME2 with TIMESTAMPTZ, and GETUTCDATE() with NOW().

  Customers, Orders, and OrderLines map 1:1 onto the entity classes in the article,
  so a DbContext built from those classes reads this data with no configuration.

  Two deliberate omissions and one addition:
    - Address, Product, and Category are omitted. They appear only in a ThenInclude
      syntax example and back no measured claim.
    - Tags IS created, because the article's cartesian-explosion example queries it.
      It has no entity class in the article, so if you are following along in code
      you will need to add one to use it — or ignore the table, which is harmless.
================================================================================
*/

SET NOCOUNT ON;

-------------------------------------------------------------------------------
-- Configuration
-------------------------------------------------------------------------------
DECLARE @OrderCount     int = 500;  -- keep at 500 to match the article
DECLARE @LinesPerOrder  int = 8;
DECLARE @TagsPerOrder   int = 3;    -- second collection, for the AsSplitQuery example

-------------------------------------------------------------------------------
-- Schema (dropped and recreated, so this is safe to re-run)
-------------------------------------------------------------------------------
DROP TABLE IF EXISTS dbo.OrderLines;
DROP TABLE IF EXISTS dbo.Tags;
DROP TABLE IF EXISTS dbo.Orders;
DROP TABLE IF EXISTS dbo.Customers;

CREATE TABLE dbo.Customers
(
    Id   int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Customers PRIMARY KEY,
    Name nvarchar(200) NOT NULL
);

CREATE TABLE dbo.Orders
(
    Id         int            NOT NULL IDENTITY(1,1) CONSTRAINT PK_Orders PRIMARY KEY,
    Reference  nvarchar(50)   NOT NULL,
    CustomerId int            NOT NULL,
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id)
);

CREATE TABLE dbo.OrderLines
(
    Id          int            NOT NULL IDENTITY(1,1) CONSTRAINT PK_OrderLines PRIMARY KEY,
    OrderId     int            NOT NULL,
    ProductName nvarchar(200)  NOT NULL,
    UnitPrice   decimal(18,2)  NOT NULL,
    Quantity    int            NOT NULL,
    CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id)
);

CREATE TABLE dbo.Tags
(
    Id      int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Tags PRIMARY KEY,
    OrderId int           NOT NULL,
    Name    nvarchar(100) NOT NULL,
    CONSTRAINT FK_Tags_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id)
);

-------------------------------------------------------------------------------
-- A numbers table, so seeding is set-based rather than a row-by-row loop
-------------------------------------------------------------------------------
DECLARE @MaxRows int =
    @OrderCount * (CASE WHEN @LinesPerOrder > @TagsPerOrder
                        THEN @LinesPerOrder ELSE @TagsPerOrder END);

;WITH Numbers AS
(
    SELECT TOP (@MaxRows)
           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
SELECT n INTO #Numbers FROM Numbers;

CREATE UNIQUE CLUSTERED INDEX IX_Numbers ON #Numbers (n);

-------------------------------------------------------------------------------
-- Seed: one customer per order, so the customer navigation is 1:1 with orders
-- and the N+1 count is exactly 1 + N rather than something dataset-dependent.
-------------------------------------------------------------------------------
-- Explicit Ids rather than letting IDENTITY assign them: an INSERT..SELECT with
-- ORDER BY does not guarantee the order identity values are handed out, and order 1
-- needs to belong to customer 1 for the counts below to be predictable.
SET IDENTITY_INSERT dbo.Customers ON;
INSERT INTO dbo.Customers (Id, Name)
SELECT n, CONCAT('Customer ', n)
FROM #Numbers
WHERE n <= @OrderCount;
SET IDENTITY_INSERT dbo.Customers OFF;

SET IDENTITY_INSERT dbo.Orders ON;
INSERT INTO dbo.Orders (Id, Reference, CustomerId)
SELECT n, CONCAT('ORD-', RIGHT('00000' + CAST(n AS varchar(10)), 5)), n
FROM #Numbers
WHERE n <= @OrderCount;
SET IDENTITY_INSERT dbo.Orders OFF;

INSERT INTO dbo.OrderLines (OrderId, ProductName, UnitPrice, Quantity)
SELECT o.Id,
       CONCAT('Product ', l.n),
       CAST(9.99 + l.n AS decimal(18,2)),
       l.n
FROM dbo.Orders o
CROSS JOIN (SELECT n FROM #Numbers WHERE n <= @LinesPerOrder) AS l;

INSERT INTO dbo.Tags (OrderId, Name)
SELECT o.Id, CONCAT('tag-', t.n)
FROM dbo.Orders o
CROSS JOIN (SELECT n FROM #Numbers WHERE n <= @TagsPerOrder) AS t;

DROP TABLE #Numbers;

-------------------------------------------------------------------------------
-- Foreign key indexes
--
-- Left commented out on purpose. The article has a section on adding indexes to
-- foreign key columns; run the queries without these first to see the scans in
-- the execution plan, then create them and compare. Creating them up front hides
-- the effect the article is describing.
-------------------------------------------------------------------------------
-- CREATE INDEX IX_OrderLines_OrderId ON dbo.OrderLines (OrderId);
-- CREATE INDEX IX_Tags_OrderId       ON dbo.Tags (OrderId);
-- CREATE INDEX IX_Orders_CustomerId  ON dbo.Orders (CustomerId);

-------------------------------------------------------------------------------
-- Verification
-------------------------------------------------------------------------------
SELECT 'Customers'  AS TableName, COUNT(*) AS ActualRows, @OrderCount               AS Expected FROM dbo.Customers
UNION ALL SELECT 'Orders',        COUNT(*),               @OrderCount                          FROM dbo.Orders
UNION ALL SELECT 'OrderLines',    COUNT(*),               @OrderCount * @LinesPerOrder         FROM dbo.OrderLines
UNION ALL SELECT 'Tags',          COUNT(*),               @OrderCount * @TagsPerOrder          FROM dbo.Tags;

/*
  With the defaults you should see 500 / 500 / 4000 / 1500.

  What to run next
  ----------------
  N+1 is an ORM artifact, not something you can demonstrate in plain SQL — the
  point is that one line of C# turns into N statements. To watch it happen:

    1. Turn on EF Core statement logging (the article shows the LogTo setup), or
       attach SQL Server Profiler / an Extended Events session filtered to this
       database.
    2. Run each of the five strategies from the article against this data and
       count the statements.

  The cartesian case is worth seeing directly, since it needs no ORM. Including
  two collections in one query multiplies them together:

    SELECT COUNT(*)
    FROM dbo.Orders o
    LEFT JOIN dbo.OrderLines ol ON ol.OrderId = o.Id
    LEFT JOIN dbo.Tags       t  ON t.OrderId  = o.Id;

  That returns 12,000 rows (500 x 8 x 3) to deliver 500 orders' worth of data.
  This is what AsSplitQuery() avoids, and why the row count matters as much as
  the statement count.
*/
