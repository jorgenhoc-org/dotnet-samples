// Azure App Service — az deployment group create --template-file azure-appservice.bicep
// F1 is free (60 CPU-min/day, no custom-domain SSL, no Always On) — dev/test only.
// B1 (~$13/month, 1 vCPU, 1.75 GB) is the cheapest tier with Always On + custom domains.
// A full deployable B1 walkthrough lives in samples/azure-app-service-dotnet.
@allowed(['F1', 'B1'])
param sku string = 'B1'

param location string = resourceGroup().location

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'jorgenhoc-hosting-plan'
  location: location
  sku: {
    name: sku
    tier: sku == 'F1' ? 'Free' : 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true // required for Linux
  }
}

resource appService 'Microsoft.Web/sites@2024-04-01' = {
  name: 'jorgenhoc-hosting-sample'
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: sku != 'F1' // Always On is not available on the Free tier
      http20Enabled: true
      minTlsVersion: '1.2'
      healthCheckPath: '/health'
    }
    httpsOnly: true
  }
}
