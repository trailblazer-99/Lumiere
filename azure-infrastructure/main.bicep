// Bicep template for Lumière Media Player Azure Infrastructure
// Provisions Storage Account, App Service Plan, Application Insights, and Azure Function App

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Prefix for resource names.')
param appNamePrefix string = 'lumiere'

@description('Environment name (e.g. dev, prod).')
param environment string = 'prod'

@description('Secret App Token for client-proxy header validation.')
@secure()
param appToken string

@description('Watchmode API Key for movie/show provider data.')
@secure()
param watchmodeApiKey string = ''

@description('TMDB API Key for movie/show metadata.')
@secure()
param tmdbApiKey string = ''

@description('Movie of the Night API Key.')
@secure()
param motnApiKey string = ''

@description('MusicAPI Bearer Token.')
@secure()
param musicApiKey string = ''

var uniqueSuffix = uniqueString(resourceGroup().id)
var storageAccountName = take('${appNamePrefix}st${uniqueSuffix}', 24)
var appServicePlanName = '${appNamePrefix}-plan-${environment}'
var functionAppName = '${appNamePrefix}-proxy-${environment}-${uniqueSuffix}'
var appInsightsName = '${appNamePrefix}-insights-${environment}'

// 1. Storage Account for Azure Functions
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: false
    minimumTlsVersion: 'TLS1_2'
  }
}

// 2. Application Insights for Telemetry
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// 3. App Service Plan (Consumption Y1)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

// 4. Azure Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'APP_TOKEN'
          value: appToken
        }
        {
          name: 'WATCHMODE_API_KEY'
          value: watchmodeApiKey
        }
        {
          name: 'TMDB_API_KEY'
          value: tmdbApiKey
        }
        {
          name: 'MOTN_API_KEY'
          value: motnApiKey
        }
        {
          name: 'MUSIC_API_KEY'
          value: musicApiKey
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}/api'
