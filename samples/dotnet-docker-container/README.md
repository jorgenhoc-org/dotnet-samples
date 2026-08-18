# Containerising a .NET app

Runnable Dockerfiles behind
[Dockerizing a .NET Application](https://www.jorgenhoc.org/en/blog/dotnet-docker-container).

Three runtime base images, the same app in each, so the size difference is measured rather
than quoted.

## Run it

Needs a running Docker daemon.

```bash
cd samples/dotnet-docker-container
./compare-image-sizes.sh
```

The script builds all three variants, prints a size table you can paste straight into an
article, and then checks two things that are easy to assume and get wrong (see below).

Build one by hand instead:

```bash
docker build -t jorgenhoc-sample:debian   .
docker build -f Dockerfile.alpine   -t jorgenhoc-sample:alpine   .
docker build -f Dockerfile.chiseled -t jorgenhoc-sample:chiseled .

docker run --rm -p 8080:8080 jorgenhoc-sample:chiseled
curl http://localhost:8080/          # reports the runtime version and the effective user
curl http://localhost:8080/health
```

`GET /` returns `Environment.UserName`, which is how you confirm the image really dropped
to the non-root user rather than silently running as root.

## Two claims worth checking, not assuming

**A `wget`-based HEALTHCHECK probably does not work.** The article pattern

```dockerfile
HEALTHCHECK CMD wget -qO- http://localhost:8080/health || exit 1
```

assumes `wget` is in the runtime image. Measured on the .NET 10 images: the **Debian**
image ships neither `wget` nor `curl` (and the check does not error loudly — it just fails,
and Docker marks a perfectly healthy container `unhealthy`); **Alpine** does have BusyBox
`wget`, so the pattern happens to work there; **chiseled** has no shell at all.
`compare-image-sizes.sh` reports which tools are actually present rather than assuming.

**Chiseled images cannot have a `HEALTHCHECK` at all.** `HEALTHCHECK CMD` runs its argument
through `/bin/sh`, and chiseled images contain no shell. That is not a limitation to work
around: liveness and readiness belong to the orchestrator (Kubernetes probes, ECS health
checks, Azure Container Apps), which probes over the network and needs nothing inside the
image.

## Notes on the Dockerfiles

**Restore is its own stage.** Only the `.csproj` is copied before `dotnet restore`, so that
layer stays cached until dependencies change. Copying the whole source first would
re-download every package on any code edit — the single most common Dockerfile mistake in
.NET.

**`-p:UseAppHost=false`.** The native apphost bootstraps the runtime for `./MyApp`. In a
container you run `dotnet MyApp.dll`, so it is dead weight.

**Non-root is one line now: `USER app`.** Every .NET 8+ image — Debian, Alpine and
chiseled alike — ships a built-in non-root `app` user (UID 1654). The classic
`RUN adduser ...` pattern is not just unnecessary, it **fails with exit 127** on the .NET 10
Debian image, whose slim base no longer includes `adduser`/`addgroup` at all.

**`TargetFramework` is in the `.csproj`, not inherited.** The Docker build context is this
folder, so the repo-root `Directory.Build.props` is not visible inside the image. Every
other sample in this repo inherits its TFM; this one cannot.

**`.dockerignore` matters more than it looks.** Without it, `COPY . .` ships `bin/` and
`obj/` into the build context, busting the layer cache on every local build and risking
copying local secrets.

## .NET version

These use **.NET 10**. The article text still shows .NET 8 tags; .NET 8 leaves support in
November 2026, so the sample targets the current LTS.
