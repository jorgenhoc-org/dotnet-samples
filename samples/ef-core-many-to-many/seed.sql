/*
================================================================================
  Sample schema and data for: "EF Core Many-to-Many Relationships"
  https://www.jorgenhoc.org/en/blog/ef-core-many-to-many

  Run this against the JorgenHocSamples database, then run the console sample
  in this folder.

  Two relationship shapes, matching the article:

    - Posts <-> Tags        implicit: EF Core's conventional PostTag junction
                            table (PostsId, TagsId), no entity class
    - Students <-> Courses  explicit: StudentCourses join entity with payload
                            (EnrolledAt, FinalGrade — Grade enum stored as int)

  Tables live in their own [ManyToMany] schema. Safe to re-run: tables are
  dropped first. The console sample resets the junction/enrollment rows itself
  on every run, so reseeding is only needed after schema changes.
================================================================================
*/

SET NOCOUNT ON;

IF SCHEMA_ID('ManyToMany') IS NULL
    EXEC('CREATE SCHEMA ManyToMany');

DROP TABLE IF EXISTS ManyToMany.PostTag;
DROP TABLE IF EXISTS ManyToMany.StudentCourses;
DROP TABLE IF EXISTS ManyToMany.Posts;
DROP TABLE IF EXISTS ManyToMany.Tags;
DROP TABLE IF EXISTS ManyToMany.Students;
DROP TABLE IF EXISTS ManyToMany.Courses;

CREATE TABLE ManyToMany.Posts
(
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_M2M_Posts PRIMARY KEY,
    Title       nvarchar(200)     NOT NULL,
    Content     nvarchar(max)     NOT NULL,
    PublishedAt datetime2         NOT NULL
);

CREATE TABLE ManyToMany.Tags
(
    Id       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_M2M_Tags PRIMARY KEY,
    Name     nvarchar(100)     NOT NULL,
    Slug     nvarchar(100)     NOT NULL,
    IsActive bit               NOT NULL CONSTRAINT DF_M2M_Tags_IsActive DEFAULT (1)
);

-- The junction table exactly as EF Core generates it by convention: name is the two
-- entity names concatenated, columns are the navigation names + "Id", and deleting a
-- post or tag cascades to its junction rows.
CREATE TABLE ManyToMany.PostTag
(
    PostsId int NOT NULL
        CONSTRAINT FK_M2M_PostTag_Posts REFERENCES ManyToMany.Posts (Id) ON DELETE CASCADE,
    TagsId  int NOT NULL
        CONSTRAINT FK_M2M_PostTag_Tags REFERENCES ManyToMany.Tags (Id) ON DELETE CASCADE,
    CONSTRAINT PK_M2M_PostTag PRIMARY KEY (PostsId, TagsId)
);

CREATE INDEX IX_M2M_PostTag_TagsId ON ManyToMany.PostTag (TagsId);

CREATE TABLE ManyToMany.Students
(
    Id    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_M2M_Students PRIMARY KEY,
    Name  nvarchar(100)     NOT NULL,
    Email nvarchar(200)     NOT NULL
);

CREATE TABLE ManyToMany.Courses
(
    Id      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_M2M_Courses PRIMARY KEY,
    Title   nvarchar(200)     NOT NULL,
    Credits int               NOT NULL
);

CREATE TABLE ManyToMany.StudentCourses
(
    StudentId  int NOT NULL
        CONSTRAINT FK_M2M_StudentCourses_Students REFERENCES ManyToMany.Students (Id) ON DELETE CASCADE,
    CourseId   int NOT NULL
        CONSTRAINT FK_M2M_StudentCourses_Courses REFERENCES ManyToMany.Courses (Id) ON DELETE CASCADE,
    EnrolledAt datetime2 NOT NULL CONSTRAINT DF_M2M_StudentCourses_EnrolledAt DEFAULT (GETUTCDATE()),
    FinalGrade int NULL,   -- Grade enum: A=0, B=1, C=2, D=3, F=4
    CONSTRAINT PK_M2M_StudentCourses PRIMARY KEY (StudentId, CourseId)
);

INSERT INTO ManyToMany.Tags (Name, Slug, IsActive)
VALUES ('.NET',             'dotnet',           1),
       ('Entity Framework', 'entity-framework', 1),
       ('C#',               'csharp',           1),
       ('Legacy',           'legacy',           0);

INSERT INTO ManyToMany.Posts (Title, Content, PublishedAt)
VALUES ('Getting started with EF Core', 'Content 1', '2025-01-15'),
       ('Async patterns in C#',         'Content 2', '2025-02-20'),
       ('Migrations in practice',       'Content 3', '2025-03-25'),
       ('Porting WebForms',             'Content 4', '2025-04-30'),
       ('An untagged post',             'Content 5', '2025-05-05');

INSERT INTO ManyToMany.PostTag (PostsId, TagsId)
VALUES (1, 1), (1, 2), (1, 3),
       (2, 1), (2, 3),
       (3, 2),
       (4, 3), (4, 4);

INSERT INTO ManyToMany.Students (Name, Email)
VALUES ('Alice', 'alice@example.edu'),
       ('Bruno', 'bruno@example.edu'),
       ('Carla', 'carla@example.edu');

INSERT INTO ManyToMany.Courses (Title, Credits)
VALUES ('Databases',           5),
       ('Distributed Systems', 4),
       ('Compilers',           6);

INSERT INTO ManyToMany.StudentCourses (StudentId, CourseId, EnrolledAt, FinalGrade)
VALUES (1, 1, '2025-01-10', 0),     -- Alice, Databases, A
       (1, 2, '2025-01-11', 1),     -- Alice, Distributed Systems, B
       (2, 1, '2025-01-12', 0),     -- Bruno, Databases, A
       (2, 3, '2025-01-13', NULL),  -- Bruno, Compilers, in progress
       (3, 2, '2025-01-14', 2);     -- Carla, Distributed Systems, C

SELECT (SELECT COUNT(*) FROM ManyToMany.Posts)          AS Posts,
       (SELECT COUNT(*) FROM ManyToMany.Tags)           AS Tags,
       (SELECT COUNT(*) FROM ManyToMany.PostTag)        AS PostTagRows,
       (SELECT COUNT(*) FROM ManyToMany.StudentCourses) AS Enrollments;
