# One-off Azure setup

Everything in Azure is created by `infra/main.bicep` through the **Deploy** workflow, except the few things the workflow needs before it can run: a resource group, an identity GitHub can sign in as, and the GitHub settings that point at them. This page sets those up. You only do it once.

GitHub signs in to Azure with **OpenID Connect** (a federated credential). There are no passwords or deployment secrets stored anywhere.

## What gets deployed

| Resource | Purpose | Approximate cost |
|---|---|---|
| Static Web App (Free) | Hosts the web app | Free |
| Container App (consumption, scales to zero) | Runs the API | Free at this usage |
| Container Apps environment + Log Analytics | Hosting and logs for the API | Pennies (first 5 GB of logs a month free) |
| Container Registry (Basic) | Stores API images | About £4 a month |
| User-assigned managed identity | Lets the API pull its image without a password | Free |

## Prerequisites

- Azure CLI 2.60 or later, and an Azure subscription where you are an **Owner**.
- Run the commands below in **Git Bash**, all in the **same window**, because later steps use variables set in earlier ones.
  - `export MSYS_NO_PATHCONV=1` stops Git Bash rewriting Azure resource IDs (which start with `/`) as Windows paths. Without it, `az` fails with `MissingSubscription`.
  - `set +H` turns off history expansion, so the `!` in the role condition in step 5 isn't treated as a history command (`event not found`).

## 1. Sign in and choose names

```sh
export MSYS_NO_PATHCONV=1
set +H
az login
az account set --subscription "<your subscription name or ID>"

RESOURCE_GROUP=rg-familytree
LOCATION=uksouth
REPO=MacLeodDevelopment/andrewmacleod-family-tree
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
```

## 2. Register the resource providers

New subscriptions don't always have these enabled.

```sh
for provider in Microsoft.App Microsoft.ContainerRegistry Microsoft.OperationalInsights Microsoft.ManagedIdentity Microsoft.Web; do
  az provider register --namespace "$provider"
done
```

## 3. Create the resource group

```sh
az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
```

## 4. Create the identity GitHub deploys as

A user-assigned managed identity with a federated credential. It trusts tokens from GitHub Actions only when they come from this repository's `production` environment.

GitHub's token *subject* includes the numeric IDs of the owner and the repository, as well as their names: `repo:<owner>@<owner-id>/<repo>@<repo-id>:environment:production`. The IDs mean a deleted and re-created repository with the same name is not trusted. For this repository they are `82574641` (MacLeodDevelopment) and `1424382484` (andrewmacleod-family-tree); you can see them at `https://api.github.com/repos/MacLeodDevelopment/andrewmacleod-family-tree` (`owner.id` and `id`).

```sh
az identity create --resource-group "$RESOURCE_GROUP" --name familytree-github-deploy
CLIENT_ID=$(az identity show --resource-group "$RESOURCE_GROUP" --name familytree-github-deploy --query clientId -o tsv)
PRINCIPAL_ID=$(az identity show --resource-group "$RESOURCE_GROUP" --name familytree-github-deploy --query principalId -o tsv)

az identity federated-credential create \
  --resource-group "$RESOURCE_GROUP" \
  --identity-name familytree-github-deploy \
  --name github-production \
  --issuer https://token.actions.githubusercontent.com \
  --subject "repo:MacLeodDevelopment@82574641/andrewmacleod-family-tree@1424382484:environment:production" \
  --audiences api://AzureADTokenExchange
```

If the deploy fails with `AADSTS700213: No matching federated identity record found`, compare the subject in the error with the one above. The credential's subject must match it exactly.

## 5. Give it permission to deploy

Three role assignments. The first two are scoped to the resource group only:

- **Contributor**: create and update the resources.
- **Role Based Access Control Administrator**, restricted by a condition so the only role it can grant or remove is **AcrPull**. Bicep needs this to let the API's identity pull images.

The third is a small custom role at subscription scope:

- **Web Operation Status Reader**: read-only access to `Microsoft.Web/locations/*`. Azure tracks long-running Static Web Apps operations, such as adding a custom domain, in a status resource at subscription level, outside the resource group. Without this role the deployment can't check that status. It gets `403 AuthorizationFailed` on `staticSitesOperationStatuses/read` and retries indefinitely, so the Infrastructure step appears to hang. The role grants nothing beyond reading those statuses.

```sh
SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
echo "$SCOPE $PRINCIPAL_ID"   # check both are filled in before continuing

az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "$SCOPE"

# The condition is passed from a file (the CLI's @file syntax) because shells mangle
# its brackets, quotes and `!`. 7f951dda-... is the built-in AcrPull role.
cat > acr-pull-only.condition <<'EOF'
((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {7f951dda-4ed3-4680-a7ca-43fe172d538d})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {7f951dda-4ed3-4680-a7ca-43fe172d538d}))
EOF

az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role "Role Based Access Control Administrator" --scope "$SCOPE" \
  --condition-version 2.0 --condition @acr-pull-only.condition

rm acr-pull-only.condition

# A custom role, defined in a file for the same quoting reasons.
cat > web-operation-status-reader.json <<EOF
{
  "Name": "Web Operation Status Reader",
  "Description": "Read the status of long-running Microsoft.Web operations (such as Static Web Apps custom domains), which Azure tracks at subscription scope.",
  "Actions": ["Microsoft.Web/locations/*/read"],
  "NotActions": [],
  "AssignableScopes": ["/subscriptions/$SUBSCRIPTION_ID"]
}
EOF

az role definition create --role-definition @web-operation-status-reader.json
rm web-operation-status-reader.json

az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role "Web Operation Status Reader" --scope "/subscriptions/$SUBSCRIPTION_ID"
```

The CLI warns that `--condition-version` is in preview; that's expected. If the last command says the role doesn't exist, wait a minute for the new role definition to replicate, then run it again.

Check all three roles are in place:

```sh
az role assignment list --assignee "$PRINCIPAL_ID" --all --query "[].{role:roleDefinitionName, scope:scope, condition:condition != null}" -o table
```

You should see **Contributor** and **Role Based Access Control Administrator** on the resource group (condition `False` and `True`), and **Web Operation Status Reader** on the subscription.

## 6. Configure GitHub

In the repository on GitHub, go to **Settings → Environments → New environment** and create `production`. Under **Deployment branches and tags**, choose **Selected branches** and add `main`.

Then add these **environment variables** to `production` (variables, not secrets — they are identifiers, not credentials). Print the values with:

```sh
echo "AZURE_CLIENT_ID=$CLIENT_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
echo "AZURE_RESOURCE_GROUP=$RESOURCE_GROUP"
```

One more variable, `ENTRA_CLIENT_ID`, comes from the sign-in setup in [setup-entra.md](setup-entra.md). The Deploy workflow refuses to run without it.

## 7. Deploy

Merge to `main`, or run **Actions → Deploy → Run workflow**. The workflow:

1. Runs the tests.
2. Deploys `infra/main.bicep`.
3. Builds the API image, pushes it to the registry and rolls the Container App onto it, then checks `/health`.
4. Builds the web app pointing at the API's URL, uploads it to Static Web Apps, then checks the page loads.

The job summaries show the web app and API URLs (`*.azurestaticapps.net` and `*.azurecontainerapps.io`). Open the web app: it should say **API is healthy**. The first request after a quiet spell can take a few seconds while the API scales up from zero.

## 8. Recommended: a budget alert

In the Azure portal, open **Cost Management → Budgets** and add a monthly budget of about £10 on the resource group, with an email alert at 80%.

## 9. Custom domain

The web app is served on `family.andrewmacleod.com`. Azure checks the DNS record, then issues and renews a free TLS certificate. The API keeps its default `*.azurecontainerapps.io` address; visitors never see it.

The order matters: Azure refuses the domain until the DNS record exists.

1. **Find the Static Web App's default hostname.** It's the web app address in the Deploy summary, without `https://`, or:

   ```sh
   az staticwebapp show --resource-group rg-familytree --name familytree-web --query defaultHostname -o tsv
   ```

2. **Add a CNAME record** where the domain's DNS is hosted (for andrewmacleod.com that's GoDaddy: **My Products → Domains → andrewmacleod.com → DNS → Add New Record**):

   | Type | Name | Value | TTL |
   |---|---|---|---|
   | CNAME | `family` | the default hostname from step 1 | 1 hour |

3. **Check the record has propagated** (usually a few minutes). The answer should include the default hostname:

   ```sh
   nslookup -type=CNAME family.andrewmacleod.com 8.8.8.8
   ```

4. **Tell the deployment about the domain.** In GitHub, add a variable to the `production` environment:

   | Name | Value |
   |---|---|
   | `WEB_CUSTOM_DOMAIN` | `family.andrewmacleod.com` |

5. **Deploy** by merging to `main` or running **Actions → Deploy → Run workflow**. The infrastructure step adds the domain to the Static Web App and to the API's allowed origins (CORS). A smoke test then checks the API accepts calls from the custom domain.

Azure can take a while to issue the certificate after the domain is added (normally minutes, occasionally an hour or more). Until then, `https://family.andrewmacleod.com` may show a certificate warning. Its status is under **Static Web App → Custom domains** in the portal.

To remove the domain, delete the `WEB_CUSTOM_DOMAIN` variable and redeploy, then delete the domain under **Custom domains** in the portal (the deployment only adds it), and finally remove the CNAME record.

## 10. Custom domain for the API

The API is served on `api.family.andrewmacleod.com` instead of its generated `*.azurecontainerapps.io` address. Container Apps issues and renews a free managed TLS certificate for it.

This one needs **two** DNS records:

- a **CNAME**, which points the name at the app
- a **TXT** record, which proves to Azure that you control the domain

Azure checks both before it accepts the domain.

1. **Find the two values** (Git Bash, signed in to Azure):

   ```sh
   export MSYS_NO_PATHCONV=1
   az containerapp show -g rg-familytree -n familytree-api --query properties.configuration.ingress.fqdn -o tsv
   az containerapp env show -g rg-familytree -n familytree-env --query properties.customDomainConfiguration.customDomainVerificationId -o tsv
   ```

2. **Add the records in GoDaddy** (**My Products → Domains → andrewmacleod.com → DNS → Add New Record**). GoDaddy wants the *Name* without the `andrewmacleod.com` part.

   | Type | Name | Value | TTL |
   |---|---|---|---|
   | CNAME | `api.family` | the app's hostname from step 1 (`familytree-api.….azurecontainerapps.io`) | 1 hour |
   | TXT | `asuid.api.family` | the verification ID from step 1 | 1 hour |

3. **Check both have propagated** (usually a few minutes):

   ```sh
   nslookup -type=CNAME api.family.andrewmacleod.com 8.8.8.8
   nslookup -type=TXT asuid.api.family.andrewmacleod.com 8.8.8.8
   ```

4. **Tell the deployment about the domain.** In GitHub, add a variable to the `production` environment:

   | Name | Value |
   |---|---|
   | `API_CUSTOM_DOMAIN` | `api.family.andrewmacleod.com` |

5. **Deploy** by merging to `main` or running **Actions → Deploy → Run workflow**.

### What the deploy does with it

Azure only issues a managed certificate for a hostname that's already attached to the app. So on the first deploy after the variable is set, the Infrastructure job deploys **twice**:

1. The first pass attaches the hostname without a certificate (binding *Disabled*) and asks Azure for the certificate. Bicep waits while Azure validates the domain and issues it, usually a few minutes.
2. The second pass, named `familytree-<run number>-bind`, binds the new certificate to the hostname (binding *SniEnabled*, meaning TLS is served for that name).

On later deploys, a step before Bicep sees that the certificate already exists, and a single pass keeps it bound. Routine deploys never unbind it.

The web app is then built to call `https://api.family.andrewmacleod.com`, and the API smoke tests run against that address. Because they use HTTPS, they also prove the certificate works.

### Removing it

1. Delete the `API_CUSTOM_DOMAIN` variable and redeploy. The hostname is removed from the app and the web app goes back to the generated address.
2. Delete the certificate in the portal under **Container Apps Environment → Certificates → Managed certificate**. Bicep doesn't delete it.
3. Remove both DNS records.
