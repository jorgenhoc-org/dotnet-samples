/*
================================================================================
  Sample data for: "ValueTask vs Task in C# — Performance Deep Dive"
  https://www.jorgenhoc.org/en/blog/valuetask-vs-task-csharp

  Ten users is deliberate. Nothing here scales with row count — the claim under
  test is about what a single call allocates on a cache hit versus a cache miss,
  so what matters is the hit/miss ratio, not the size of the table.

  Target: SQL Server 2016 or later. Run against the same database as the other
  samples; the table name is distinct.
================================================================================
*/

SET NOCOUNT ON;

DROP TABLE IF EXISTS dbo.Users;

CREATE TABLE dbo.Users
(
    Id    int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Users PRIMARY KEY,
    Name  nvarchar(200) NOT NULL,
    Email nvarchar(320) NOT NULL
);

SET IDENTITY_INSERT dbo.Users ON;

INSERT INTO dbo.Users (Id, Name, Email)
VALUES
    (1,  N'Ada Lovelace',      N'ada@example.com'),
    (2,  N'Grace Hopper',      N'grace@example.com'),
    (3,  N'Alan Turing',       N'alan@example.com'),
    (4,  N'Edsger Dijkstra',   N'edsger@example.com'),
    (5,  N'Barbara Liskov',    N'barbara@example.com'),
    (6,  N'Donald Knuth',      N'donald@example.com'),
    (7,  N'Margaret Hamilton', N'margaret@example.com'),
    (8,  N'Tony Hoare',        N'tony@example.com'),
    (9,  N'Leslie Lamport',    N'leslie@example.com'),
    (10, N'Katherine Johnson', N'katherine@example.com');

SET IDENTITY_INSERT dbo.Users OFF;

SELECT 'Users' AS TableName, COUNT(*) AS ActualRows, 10 AS Expected FROM dbo.Users;
