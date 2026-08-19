/*
================================================================================
  Sample schema and data for: "EF Core Global Query Filters"
  https://www.jorgenhoc.org/en/blog/ef-core-global-query-filters

  Run this against the JorgenHocSamples database, then run the console sample
  in this folder.

  Seeded state (tenant A = 11111111-..., tenant B = 22222222-...):

    Blogs      1 active ("Engineering"), 1 soft-deleted ("Archive")
    Posts      blog 1 has 2 active posts + 1 soft-deleted
    Invoices   tenant A: 1 active + 1 soft-deleted; tenant B: 1 active
    LineItems  invoice 1 has 2 tenant-A items PLUS one row deliberately stamped
               with tenant B — corrupted on purpose, so the sample can prove the
               tenant filter keeps it out of Include() results
    Customers  1 active, 1 soft-deleted (owned Address columns inline)

  Tables live in their own [QueryFilters] schema. Safe to re-run: tables are
  dropped first. The console sample resets what it mutates on every run.
================================================================================
*/

SET NOCOUNT ON;
-- sqlcmd defaults QUOTED_IDENTIFIER to OFF, and the filtered (partial) index below
-- refuses to be created that way.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF SCHEMA_ID('QueryFilters') IS NULL
    EXEC('CREATE SCHEMA QueryFilters');

DROP TABLE IF EXISTS QueryFilters.InvoiceLineItems;
DROP TABLE IF EXISTS QueryFilters.Invoices;
DROP TABLE IF EXISTS QueryFilters.Posts;
DROP TABLE IF EXISTS QueryFilters.Blogs;
DROP TABLE IF EXISTS QueryFilters.Customers;

CREATE TABLE QueryFilters.Blogs
(
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QF_Blogs PRIMARY KEY,
    Title     nvarchar(200)     NOT NULL,
    Slug      nvarchar(200)     NOT NULL,
    IsDeleted bit               NOT NULL,
    DeletedAt datetime2         NULL,
    DeletedBy nvarchar(100)     NULL,
    CreatedAt datetime2         NOT NULL,
    UpdatedAt datetime2         NOT NULL,
    CreatedBy nvarchar(100)     NOT NULL,
    UpdatedBy nvarchar(100)     NOT NULL
);

CREATE TABLE QueryFilters.Posts
(
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QF_Posts PRIMARY KEY,
    Title     nvarchar(200)     NOT NULL,
    Body      nvarchar(max)     NOT NULL,
    BlogId    int               NOT NULL
        CONSTRAINT FK_QF_Posts_Blogs REFERENCES QueryFilters.Blogs (Id),
    IsDeleted bit               NOT NULL,
    DeletedAt datetime2         NULL,
    DeletedBy nvarchar(100)     NULL,
    CreatedAt datetime2         NOT NULL,
    UpdatedAt datetime2         NOT NULL,
    CreatedBy nvarchar(100)     NOT NULL,
    UpdatedBy nvarchar(100)     NOT NULL
);

-- The article's partial index: only active rows, far smaller than a full index.
CREATE INDEX IX_QueryFilters_Posts_Active
    ON QueryFilters.Posts (IsDeleted) WHERE IsDeleted = 0;

CREATE TABLE QueryFilters.Invoices
(
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QF_Invoices PRIMARY KEY,
    TenantId  uniqueidentifier  NOT NULL,
    Amount    decimal(18,2)     NOT NULL,
    Currency  nvarchar(10)      NOT NULL,
    IssuedAt  datetime2         NOT NULL,
    IsDeleted bit               NOT NULL,
    DeletedAt datetime2         NULL,
    DeletedBy nvarchar(100)     NULL,
    CreatedAt datetime2         NOT NULL,
    UpdatedAt datetime2         NOT NULL,
    CreatedBy nvarchar(100)     NOT NULL,
    UpdatedBy nvarchar(100)     NOT NULL
);

CREATE INDEX IX_QueryFilters_Invoices_Tenant_Active
    ON QueryFilters.Invoices (TenantId, IsDeleted);

CREATE TABLE QueryFilters.InvoiceLineItems
(
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QF_InvoiceLineItems PRIMARY KEY,
    TenantId    uniqueidentifier  NOT NULL,
    Description nvarchar(200)     NOT NULL,
    UnitPrice   decimal(18,2)     NOT NULL,
    Quantity    int               NOT NULL,
    InvoiceId   int               NOT NULL
        CONSTRAINT FK_QF_LineItems_Invoices REFERENCES QueryFilters.Invoices (Id),
    IsDeleted   bit               NOT NULL,
    DeletedAt   datetime2         NULL,
    DeletedBy   nvarchar(100)     NULL,
    CreatedAt   datetime2         NOT NULL,
    UpdatedAt   datetime2         NOT NULL,
    CreatedBy   nvarchar(100)     NOT NULL,
    UpdatedBy   nvarchar(100)     NOT NULL
);

CREATE TABLE QueryFilters.Customers
(
    Id                        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_QF_Customers PRIMARY KEY,
    Name                      nvarchar(200)     NOT NULL,
    BillingAddress_Street     nvarchar(200)     NULL,
    BillingAddress_City       nvarchar(100)     NULL,
    BillingAddress_PostalCode nvarchar(20)      NULL,
    IsDeleted                 bit               NOT NULL,
    DeletedAt                 datetime2         NULL,
    DeletedBy                 nvarchar(100)     NULL,
    CreatedAt                 datetime2         NOT NULL,
    UpdatedAt                 datetime2         NOT NULL,
    CreatedBy                 nvarchar(100)     NOT NULL,
    UpdatedBy                 nvarchar(100)     NOT NULL
);

DECLARE @now datetime2 = '2025-03-01',
        @tenantA uniqueidentifier = '11111111-1111-1111-1111-111111111111',
        @tenantB uniqueidentifier = '22222222-2222-2222-2222-222222222222';

INSERT INTO QueryFilters.Blogs (Title, Slug, IsDeleted, DeletedAt, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
VALUES ('Engineering', 'engineering', 0, NULL, @now, @now, 'seed', 'seed'),
       ('Archive',     'archive',     1, @now, @now, @now, 'seed', 'seed');

INSERT INTO QueryFilters.Posts (Title, Body, BlogId, IsDeleted, DeletedAt, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
VALUES ('Post One', 'Body 1', 1, 0, NULL, @now, @now, 'seed', 'seed'),
       ('Post Two', 'Body 2', 1, 0, NULL, @now, @now, 'seed', 'seed'),
       ('Post Old', 'Body 3', 1, 1, @now, @now, @now, 'seed', 'seed');

INSERT INTO QueryFilters.Invoices (TenantId, Amount, Currency, IssuedAt, IsDeleted, DeletedAt, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
VALUES (@tenantA, 100.00, 'USD', @now, 0, NULL, @now, @now, 'seed', 'seed'),
       (@tenantA, 200.00, 'USD', @now, 1, @now, @now, @now, 'seed', 'seed'),
       (@tenantB, 300.00, 'USD', @now, 0, NULL, @now, @now, 'seed', 'seed');

INSERT INTO QueryFilters.InvoiceLineItems (TenantId, Description, UnitPrice, Quantity, InvoiceId, IsDeleted, DeletedAt, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
VALUES (@tenantA, 'Item A1',              50.00, 1, 1, 0, NULL, @now, @now, 'seed', 'seed'),
       (@tenantA, 'Item A2',              50.00, 1, 1, 0, NULL, @now, @now, 'seed', 'seed'),
       (@tenantB, 'Leaked from tenant B', 99.00, 1, 1, 0, NULL, @now, @now, 'seed', 'seed');

INSERT INTO QueryFilters.Customers (Name, BillingAddress_Street, BillingAddress_City, BillingAddress_PostalCode, IsDeleted, DeletedAt, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
VALUES ('Contoso',  '1 Main St', 'Springfield', '12345', 0, NULL, @now, @now, 'seed', 'seed'),
       ('Old Corp', '2 Elm St',  'Shelbyville', '67890', 1, @now, @now, @now, 'seed', 'seed');

SELECT (SELECT COUNT(*) FROM QueryFilters.Blogs)            AS Blogs,
       (SELECT COUNT(*) FROM QueryFilters.Posts)            AS Posts,
       (SELECT COUNT(*) FROM QueryFilters.Invoices)         AS Invoices,
       (SELECT COUNT(*) FROM QueryFilters.InvoiceLineItems) AS LineItems,
       (SELECT COUNT(*) FROM QueryFilters.Customers)        AS Customers;
