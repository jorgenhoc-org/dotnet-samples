# JorgenHoc.DataAccess

The data layer for every EF Core article: one folder per article, each holding that
article's entities and its `DbContext`.

## Why one folder per article

Each article deliberately models a different domain — `Order`/`Customer` for the N+1
article, `Post`/`Tag` for many-to-many, soft-deletable entities for global query filters.
Names collide across articles by design, so each folder gets its own namespace:

```
EfCoreNPlusOne/   →  JorgenHoc.DataAccess.EfCoreNPlusOne
EfCoreManyToMany/ →  JorgenHoc.DataAccess.EfCoreManyToMany
```

A sample project imports only the namespace for its own article and sees only the three
or four types that article discusses.

## Why one DbContext per article, not one shared context

A single context with every article's `DbSet` would mean running the N+1 sample creates
the many-to-many article's tables as well. Each article gets its own context so its
schema contains exactly what the article talks about.

## Adding an article

1. Create `<ArticleName>/` using the article's PascalCase name.
2. Put the entities and the `DbContext` in it, namespaced `JorgenHoc.DataAccess.<ArticleName>`.
3. Reference this project from `samples/<article-slug>/`.

Entities live next to their context rather than in the sample project on purpose: the
sample would otherwise have to be referenced *by* this project to supply the entity
types, while also referencing it *for* the context — a circular dependency.
