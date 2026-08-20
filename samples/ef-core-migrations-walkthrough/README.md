# ef-core-migrations-walkthrough

Backs [EF Core Migrations: The Definitive Walkthrough](https://www.jorgenhoc.org/en/blog/ef-core-migrations-walkthrough).

Unlike the other EF samples, **the deliverable here is the `Migrations/` folder**, not the
console output. Every file under it was produced by `dotnet ef migrations add`, one per
step of the walkthrough:

| Migration | What it does | Generated or edited |
|-----------|--------------|---------------------|
| `InitialCreate` | Creates `Products` (Id, Name, Price, CreatedAt) | tool-generated |
| `AddProductDescription` | Adds the nullable `Description` column | tool-generated |
| `AddIndexOnProductName` | Adds `IX_Products_Name` | tool-generated |
| `AddProductSlug` | Adds `Slug` **nullable**, backfills it from `Name`, then alters it to **NOT NULL** | tool-generated, then **hand-edited** |

`AddProductSlug` is the one to read: EF generated a single non-nullable `AddColumn` with an
empty default, which would leave every existing row blank. It is hand-edited into the
article's safe three-step pattern — add nullable, `UPDATE ... SET Slug = LOWER(REPLACE(...))`,
then `AlterColumn` to non-nullable — so real data survives the schema tightening.

## Running it

Self-contained (entity, context, and migrations live together) and pointed at its **own**
database, `JorgenHocSamples_Migrations`, so the article's destructive steps can never touch
the shared sample DB.

```bash
dotnet run
```

On a fresh database the program migrates to the pre-`Slug` schema, inserts two rows that
have a `Name` but no `Slug` column yet, then applies `AddProductSlug` so its backfill runs
on real data — you will see `Mechanical Keyboard -> mechanical-keyboard`. It then prints the
`__EFMigrationsHistory` table. Re-runs are idempotent.

## The EF commands from the article

```bash
dotnet ef migrations list                       # applied / pending status
dotnet ef migrations add <Name>                 # generate the next delta
dotnet ef database update                        # apply all pending
dotnet ef database update AddIndexOnProductName  # roll forward/back to a point
dotnet ef database update 0                      # roll everything back (drops tables)
dotnet ef migrations script --idempotent -o migration.sql   # for CI/CD
dotnet ef database drop --force                  # start over
```

`dotnet ef migrations has-pending-model-changes` returns "No changes" here — proof the
hand-edited migration and the model snapshot agree.
