// Microsoft Entra ID (sign-in) configuration for the family tree, using Bicep's
// Microsoft Graph extension. It creates objects in the Entra tenant that owns the
// Azure subscription, not Azure resources.
//
// Deployed by hand, not by the Deploy workflow: changing Entra needs directory
// permissions the GitHub deploy identity deliberately doesn't have. See
// docs/setup-entra.md.

extension microsoftGraphV1

@description('Browser origins the web app is served from. Each becomes a sign-in redirect URI.')
param webOrigins array

@description('Object ID of a user to make a Family.Admin (normally whoever runs the deployment). Leave empty to skip.')
param adminPrincipalId string = ''

// Fixed IDs, so redeploying updates the same scope and roles instead of creating new ones.
var readScopeId = guid('familytree', 'scope', 'Family.Read')
var viewerRoleId = guid('familytree', 'role', 'Family.Viewer')
var adminRoleId = guid('familytree', 'role', 'Family.Admin')

// Well-known app ID of Microsoft Graph, which owns the standard sign-in scopes.
var microsoftGraphAppId = '00000003-0000-0000-c000-000000000000'

// One registration for both the web app (a public client signing users in) and the
// API (the resource the web app requests tokens for).
resource app 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'familytree'
  displayName: 'Family Tree'
  // Only accounts in this tenant, which includes invited guests.
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/familytree'
  ]
  spa: {
    redirectUris: webOrigins
  }
  api: {
    // v2 tokens: the audience is the app's client ID.
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: readScopeId
        value: 'Family.Read'
        type: 'User'
        isEnabled: true
        adminConsentDisplayName: 'Use the family tree'
        adminConsentDescription: 'Lets the family tree web app call the family tree API on behalf of the signed-in user.'
        userConsentDisplayName: 'Use the family tree'
        userConsentDescription: 'Lets the family tree web app call the family tree API on your behalf.'
      }
    ]
  }
  // What a signed-in user may do. Checked by the API on every request.
  appRoles: [
    {
      id: viewerRoleId
      value: 'Family.Viewer'
      displayName: 'Family viewer'
      description: 'Can view the family tree.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
    {
      id: adminRoleId
      value: 'Family.Admin'
      displayName: 'Family admin'
      description: 'Can view and edit the family tree.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
  ]
}

// The app's identity in this tenant (shown in the portal as an "Enterprise application").
resource servicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: app.appId
  // Only users assigned a role can sign in at all; everyone else is refused by Entra.
  appRoleAssignmentRequired: true
}

resource microsoftGraph 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: microsoftGraphAppId
}

// Consent on everyone's behalf, so family members never see a permissions prompt.
resource signInConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: servicePrincipal.id
  resourceId: microsoftGraph.id
  consentType: 'AllPrincipals'
  scope: 'openid profile offline_access'
}

resource apiConsent 'Microsoft.Graph/oauth2PermissionGrants@v1.0' = {
  clientId: servicePrincipal.id
  resourceId: servicePrincipal.id
  consentType: 'AllPrincipals'
  scope: 'Family.Read'
}

resource adminAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(adminPrincipalId)) {
  appRoleId: adminRoleId
  principalId: adminPrincipalId
  resourceId: servicePrincipal.id
}

output clientId string = app.appId
output tenantId string = tenant().tenantId
output apiScope string = 'api://${tenant().tenantId}/familytree/Family.Read'
