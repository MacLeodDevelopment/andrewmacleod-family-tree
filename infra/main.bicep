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

@description('Client (application) ID of the "Family Tree" Entra app registration, from deploying infra/entra.bicep. The API validates sign-in tokens issued for it, and refuses to start without it.')
param entraClientId string

@description('Custom domain for the API, such as api.family.example.com. Leave empty until its DNS records exist: a CNAME to the Container App\'s default hostname, and a TXT record asuid.<domain> holding the environment\'s domain verification ID.')
param apiCustomDomain string = ''

@description('Whether the API custom domain\'s managed certificate has already been issued. The deploy workflow looks this up. Azure only issues a certificate for a hostname that is already on the app, so the first deployment adds the hostname unbound and requests the certificate; a second deployment, with this set to true, binds it.')
param apiCustomDomainCertificateExists bool = false

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

// The API's custom domain and its free managed certificate. The certificate's ID is built
// as a string rather than referenced, because the certificate can only be created after
// the hostname is on the app: the app must not depend on it.
var apiCertificateName = '${namePrefix}-api-${replace(apiCustomDomain, '.', '-')}'
var apiCertificateId = '${containerAppsEnvironment.id}/managedCertificates/${apiCertificateName}'
var apiCustomDomains = empty(apiCustomDomain)
  ? []
  : [
      apiCustomDomainCertificateExists
        ? {
            name: apiCustomDomain
            bindingType: 'SniEnabled'
            certificateId: apiCertificateId
          }
        : {
            name: apiCustomDomain
            bindingType: 'Disabled'
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
        customDomains: apiCustomDomains
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
              // Sign-in: which Entra tenant and app registration access tokens must be issued by and for.
              {
                name: 'AzureAd__TenantId'
                value: tenant().tenantId
              }
              {
                name: 'AzureAd__ClientId'
                value: entraClientId
              }
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

// Azure validates the domain through its CNAME record, then issues and renews the certificate.
resource apiCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2025-01-01' = if (!empty(apiCustomDomain)) {
  parent: containerAppsEnvironment
  name: apiCertificateName
  location: location
  properties: {
    subjectName: apiCustomDomain
    domainControlValidation: 'CNAME'
  }
  dependsOn: [
    api
  ]
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output apiName string = api.name
// The address the web app and smoke tests use: the custom domain once its certificate is bound.
output apiUrl string = !empty(apiCustomDomain) && apiCustomDomainCertificateExists
  ? 'https://${apiCustomDomain}'
  : 'https://${api.properties.configuration.ingress.fqdn}'
output webName string = web.name
output webUrl string = 'https://${web.properties.defaultHostname}'
output webCustomUrl string = empty(webCustomDomain) ? '' : 'https://${webCustomDomain}'
