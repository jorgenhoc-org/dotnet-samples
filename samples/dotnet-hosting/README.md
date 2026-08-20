# One .NET app, every hosting platform

The deployable app and real config files behind
[Best .NET Hosting](https://www.jorgenhoc.org/en/blog/best-dotnet-hosting) and
[Cheapest Way to Host a .NET App](https://www.jorgenhoc.org/en/blog/cheapest-dotnet-hosting).

One minimal .NET 10 API (`Program.cs`), one Dockerfile, and under `deploy/` the exact
file each platform needs — not snippets, files you can point a CLI at:

| Platform | File | Article context |
|---|---|---|
| Fly.io | [`deploy/fly.toml`](deploy/fly.toml) | ~$2/mo shared-cpu machine, scale-to-zero |
| Railway | [`deploy/railway.json`](deploy/railway.json) | Hobby $5/mo with $5 usage included |
| Render | [`deploy/render.yaml`](deploy/render.yaml) | free tier (spins down after 15 min) |
| DigitalOcean | [`deploy/digitalocean-app.yaml`](deploy/digitalocean-app.yaml) | $5/mo container |
| Azure App Service | [`deploy/azure-appservice.bicep`](deploy/azure-appservice.bicep) | F1 free vs B1 (~$13/mo) |
| Azure Container Apps | [`deploy/azure-container-apps.yaml`](deploy/azure-container-apps.yaml) | consumption free grant |
| Oracle/any VM | [`deploy/oracle-vm/`](deploy/oracle-vm) | systemd unit + Caddyfile (or nginx) |

A full Azure B1 walkthrough with CLI commands lives in
[`samples/azure-app-service-dotnet`](../azure-app-service-dotnet); per-platform deploy
articles: [Railway](https://www.jorgenhoc.org/en/blog/deploy-dotnet-railway),
[Fly.io](https://www.jorgenhoc.org/en/blog/dotnet-flyio),
[Render](https://www.jorgenhoc.org/en/blog/dotnet-render-com).

## The PORT trap this sample proves

Railway and Render inject the listen port as a `PORT` env var at **runtime**. The
often-copied Dockerfile line

```dockerfile
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}   # broken — don't copy this
```

does not work: Docker resolves `${...}` when the image is **built**, so the port is
baked to 8080 and the platform's injected value is ignored. The fix is three lines in
`Program.cs` (read `PORT`, call `UseUrls`), and `smoke-test.sh` asserts it: the app and
the container are both started with an injected `PORT` and must answer on it.

## Run it

```bash
cd samples/dotnet-hosting
./smoke-test.sh
```

No cloud account needed. With Docker running, the container assertions run too;
without it they are skipped. Deployments themselves (and the screenshots in the
articles) are done against real provider accounts.

## Benchmark

[`benchmark/k6-latency.js`](benchmark/k6-latency.js) is the k6 script behind the
latency numbers in the Render article: a fixed 30 req/s for 60 s against each deployed
platform, comparing latency distributions at equal load. Run it against any deployment:

```bash
k6 run -e TARGET=https://your-app.example.com benchmark/k6-latency.js
```

Published numbers disclose the client location — with a fixed arrival rate the
distribution is dominated by the path between the client and the provider's region,
which is exactly what a user in that location experiences.
