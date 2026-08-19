# EF Core: many-to-many relationships

Runnable statement counts for both relationship shapes in
[EF Core Many-to-Many Relationships](https://www.jorgenhoc.org/en/blog/ef-core-many-to-many):
the implicit junction (`Post` ↔ `Tag`, no entity class) and the explicit join entity with
payload (`Student` ↔ `Course` via `StudentCourse` with `EnrolledAt`/`FinalGrade`).

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and SQL Server LocalDB
(installed with Visual Studio, or via the SQL Server Express installer).

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
dotnet run
```

Point `appsettings.json` somewhere else if you prefer another server — any SQL Server
will do. Add `-- --sql` to print every statement as it executes:

```bash
dotnet run -- --sql
```

The demos add and remove junction/enrollment rows on purpose; the program resets them to
the seeded state at startup, so it is safe to re-run without reseeding.

## Expected output

First, proof that the conventional junction table exists with the conventional columns —
read back from `INFORMATION_SCHEMA`, not asserted in prose:

```
Junction table PostTag, no entity class: (PostsId, TagsId)
```

Then the counts:

```
| Strategy                                      | SQL statements |
|-----------------------------------------------|----------------|
| Include tags (implicit junction, one query)   |              1 |
| Filtered Include (active tags only)           |              1 |
| Where Tags.Any(slug == 'dotnet') + Include    |              1 |
| Join entity with payload (grades + Include)   |              1 |
| Aggregate: average grade per course           |              1 |
| Add tag: Include collection + Find tag + save |              3 |
| Add tag: Attach() stubs, nothing loaded       |              1 |
| Remove tag (Include + save)                   |              2 |
| Bulk: ExecuteDelete on the junction rows      |              1 |
| Enroll: exists-check + insert join entity     |              2 |
```

And the collection-replacement experiment:

```
Replacing the collection instance (post.Tags = new list):
  Junction rows for post 2 afterwards: [entity-framework]
```

You should get these exact numbers — statement counts do not depend on hardware or
provider. If yours differ, something is genuinely different and worth an issue.

## What the numbers mean

**Reads cost one statement in both shapes.** The junction table disappears into the JOIN
whether it has an entity class or not; payload columns on the explicit join entity ride
along for free.

**The `Attach()` stub trick is the cheapest write in the file.** One INSERT into the
junction table, zero reads. The loaded-collection alternative costs three statements —
two of them just to fetch what the write never needed. One wrinkle the article's original
snippet missed: with `required` members on the entities, stubs need `Title = null!`-style
placeholders, because `required` is enforced at construction even when only the key
matters.

**Replacing the collection instance IS tracked** — this experiment exists because the
opposite claim is widely repeated (and an earlier version of the article repeated it).
Post 2 started with three tags; after `post.Tags = [efTag]` and `SaveChangesAsync()`, the
junction holds exactly one row. EF Core's `DetectChanges` diffs collection *contents*
against its snapshot, so it deletes three junction rows and inserts one. The real trap is
different: replace a collection you never loaded and EF Core sees only additions —
existing junction rows survive, and re-adding one of them is a duplicate-key error.

## Notes

The entities and Fluent API configuration live in
[`shared/JorgenHoc.DataAccess/EfCoreManyToMany`](../../shared/JorgenHoc.DataAccess/EfCoreManyToMany)
and mirror the article's snippets. `Post` ↔ `Tag` is deliberately left with zero
configuration so the conventional `PostTag` (`PostsId`, `TagsId`) table is what EF Core
itself derives; `seed.sql` creates that exact shape. `StudentCourse` uses the article's
composite key + `UsingEntity<StudentCourse>()` skip-navigation setup, with the `Grade`
enum stored as `int`.
