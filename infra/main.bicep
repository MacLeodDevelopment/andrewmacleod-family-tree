// Azure resources for the family tree, deployed into one resource group.
// Deployed by .github/workflows/deploy.yml; see docs/setup-azure.md for the one-off setup.

@description('Location for most resources.')
param location string = resourceGroup().location

// Static Web Apps is only offered in a few regions (not UK South), and West Europe is not
// accepting new customers. The location only holds the app's metadata: the site itself is
// served from Azure's global edge, and it contains no family data.
@description('Location for the Static Web App, which has its own short list of regions.')
param staticWebAppLocation string = 'eastus2'

@description('Prefix for resource names. Lowercase letters only.')
param namePrefix string = 'familytree'

@description('API container image. The deploy workflow passes the image that is currently running, so redeploying the infrastructure does not roll the API back. The default is only used on the very first deployment.')
param apiImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Custom domain for the web app, such as family.example.com. Leave empty until a DNS CNAME record for it points at the Static Web App\'s default hostname, or the deployment fails validation.')
param webCustomDomain string = ''

// Built-in role: AcrPull.
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource logs 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  // Registry names are global and alphanumeric only.
  name: '${namePrefix}${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
  }
}

// The API pulls its image with this identity, so no registry passwords exist.
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${namePrefix}-api-identity'
  location: location
}

resource apiIdentityCanPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, apiIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource web 'Microsoft.Web/staticSites@2024-04-01' = {
  name: '${namePrefix}-web'
  location: staticWebAppLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// Azure checks the CNAME record, then issues and renews a free certificate for the domain.
resource webDomain 'Microsoft.Web/staticSites/customDomains@2024-04-01' = if (!empty(webCustomDomain)) {
  parent: web
  name: webCustomDomain
  properties: {
    validationMethod: 'cname-delegation'
  }
}

// CORS origins for the API: the web app's default address, plus its custom domain if it
// has one. ASP.NET Core reads Cors__AllowedOrigins__0, __1, ... as the Cors:AllowedOrigins array.
var customDomainOriginSettings = empty(webCustomDomain)
  ? []
  : [
      {
        name: 'Cors__AllowedOrigins__1'
        value: 'https://${webCustomDomain}'
      }
    ]

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: apiIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat(
            [
              {
                name: 'Cors__AllowedOrigins__0'
                value: 'https://${web.properties.defaultHostname}'
              }
            ],
            customDomainOriginSettings
          )
        }
      ]
      // Scale to zero when idle to keep costs near nothing; the first request after
      // a quiet spell takes a few seconds while a replica starts.
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    apiIdentityCanPull
  ]
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output apiName string = api.name
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output webName string = web.name
output webUrl string = 'https://${web.properties.defaultHostname}'
output webCustomUrl string = empty(webCustomDomain) ? '' : 'https://${webCustomDomain}'
