#!/usr/bin/env bash
# End-to-end deploy of this sample to Azure App Service, mirroring the article's commands:
# resource group -> B1 Linux plan -> web app -> app settings -> zip deploy.
#
# Needs: Azure CLI logged in (az login), .NET 10 SDK.
# COSTS MONEY while it exists (~$13/month for B1, billed per second).
# Tear everything down when done:   az group delete --name "$RG" --yes --no-wait
set -euo pipefail

# Keep the window open whether it worked or died, so the output is readable.
trap 'code=$?; ((code)) && echo "FAILED (exit $code) — see the last command above"; read -rp "Press Enter to close..."' EXIT

SUFFIX=$RANDOM
RG="${RG:-jorgenhoc-sample-rg}"
PLAN="${PLAN:-jorgenhoc-sample-plan}"
APP="${APP:-jorgenhoc-sample-$SUFFIX}"   # must be globally unique across *.azurewebsites.net
LOCATION="${LOCATION:-eastus}"
RUNTIME="DOTNETCORE:10.0"

echo "== Verifying '$RUNTIME' is an available Linux runtime =="
# list-runtimes prints the pipe form (DOTNETCORE|10.0) in the first tsv column,
# while `az webapp create --runtime` takes the colon form — compare apples to apples.
az webapp list-runtimes --os linux -o tsv | cut -f1 | grep -qx "${RUNTIME/:/|}" || {
  echo "Runtime $RUNTIME not offered in this region/CLI version. Available .NET runtimes:"
  az webapp list-runtimes --os linux -o tsv | grep DOTNET
  exit 1
}

echo "== Creating resource group, plan (B1), web app =="
az group create --name "$RG" --location "$LOCATION" -o none
az appservice plan create --name "$PLAN" --resource-group "$RG" --sku B1 --is-linux -o none
az webapp create --name "$APP" --resource-group "$RG" --plan "$PLAN" --runtime "$RUNTIME" -o none

echo "== Setting an app setting to demonstrate the appsettings.json override =="
az webapp config appsettings set --resource-group "$RG" --name "$APP" \
  --settings "Sample__Message=set from App Settings via az cli" -o none

echo "== Publishing and zip-deploying =="
dotnet publish -c Release -o ./publish
if command -v zip >/dev/null 2>&1; then
  (cd ./publish && zip -qr ../app.zip .)
else
  # ponytail: Git Bash on Windows has no zip; Compress-Archive does the same job
  powershell.exe -NoProfile -Command "Compress-Archive -Path ./publish/* -DestinationPath ./app.zip -Force"
fi
az webapp deploy --resource-group "$RG" --name "$APP" --src-path app.zip --type zip -o none

URL="https://$APP.azurewebsites.net"
echo
echo "Deployed: $URL"
echo "Expect: environment=Production, message=set from App Settings via az cli, site=$APP"
echo
echo "Clean up with:  az group delete --name $RG --yes --no-wait"
