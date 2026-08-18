# Azure App Service for .NET

Runnable deployment behind
[Azure App Service for .NET](https://www.jorgenhoc.org/en/blog/azure-app-service-dotnet).

A minimal web app plus a script that walks the article's exact CLI path: resource group →
B1 Linux plan → web app → app settings → zip deploy. Each field the app returns proves one
claim from the article rather than asking you to take it on trust.

## Run it locally

```bash
cd samples/azure-app-service-dotnet
dotnet run
```

`GET /` returns:

```json
{
  "service": "JorgenHoc App Service sample",
  "runtime": "10.0.x",
  "environment": "Development",
  "message": "from appsettings.json",
  "site": "(not on App Service)",
  "instance": "(local)"
}
```

## Deploy it

Needs the Azure CLI logged in (`az login`).

```bash
./deploy.sh
```

<!-- markdownlint-disable-next-line -->
**This creates billable resources** — a B1 plan is ~$13/month, billed per second. The
script prints the cleanup command; it is one line:

```bash
az group delete --name jorgenhoc-sample-rg --yes --no-wait
```

Open the printed `https://<app>.azurewebsites.net` URL. The same endpoint now shows:

- `environment: "Production"` — App Service sets `ASPNETCORE_ENVIRONMENT` for you.
- `message: "set from App Settings via az cli"` — the App Setting the script created
  **overrides** the value in `appsettings.json`, demonstrating that App Settings become
  environment variables with higher configuration precedence.
- `site` / `instance` — populated from `WEBSITE_SITE_NAME` / `WEBSITE_INSTANCE_ID`, which
  only exist on App Service. Scale out (`az appservice plan update --number-of-workers 2`)
  and refresh: `instance` changes as requests land on different workers.

## Notes

- The script verifies `DOTNETCORE:10.0` is offered (`az webapp list-runtimes --os linux`)
  before creating anything, and lists the available .NET runtimes if not.
- `RG`, `PLAN`, `APP` and `LOCATION` can be overridden via environment variables; `APP`
  defaults to a random suffix because names are global across `*.azurewebsites.net`.
