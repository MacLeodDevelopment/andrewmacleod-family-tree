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

```sh
az identity create --resource-group "$RESOURCE_GROUP" --name familytree-github-deploy
CLIENT_ID=$(az identity show --resource-group "$RESOURCE_GROUP" --name familytree-github-deploy --query clientId -o tsv)
PRINCIPAL_ID=$(az identity show --resource-group "$RESOURCE_GROUP" --name familytree-github-deploy --query principalId -o tsv)

az identity federated-credential create \
  --resource-group "$RESOURCE_GROUP" \
  --identity-name familytree-github-deploy \
  --name github-production \
  --issuer https://token.actions.githubusercontent.com \
  --subject "repo:$REPO:environment:production" \
  --audiences api://AzureADTokenExchange
```

## 5. Give it permission to deploy

Scoped to the resource group only:

- **Contributor**: create and update the resources.
- **Role Based Access Control Administrator**, restricted by a condition so the only role it can grant or remove is **AcrPull**. Bicep needs this to let the API's identity pull images.

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
```

The CLI warns that `--condition-version` is in preview; that's expected.

Check both roles are in place:

```sh
az role assignment list --assignee "$PRINCIPAL_ID" --all --query "[].{role:roleDefinitionName, condition:condition != null}" -o table
```

You should see **Contributor** (condition `False`) and **Role Based Access Control Administrator** (condition `True`).

## 6. Configure GitHub

In the repository on GitHub, go to **Settings → Environments → New environment** and create `production`. Under **Deployment branches and tags**, choose **Selected branches** and add `main`.

Then add these **environment variables** to `production` (variables, not secrets — they are identifiers, not credentials). Print the values with:

```sh
echo "AZURE_CLIENT_ID=$CLIENT_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
echo "AZURE_RESOURCE_GROUP=$RESOURCE_GROUP"
```

## 7. Deploy

Merge to `main`, or run **Actions → Deploy → Run workflow**. The workflow:

1. Runs the tests.
2. Deploys `infra/main.bicep`.
3. Builds the API image, pushes it to the registry and rolls the Container App onto it, then checks `/health`.
4. Builds the web app pointing at the API's URL, uploads it to Static Web Apps, then checks the page loads.

The job summaries show the web app and API URLs (`*.azurestaticapps.net` and `*.azurecontainerapps.io`). Open the web app: it should say **API is healthy**. The first request after a quiet spell can take a few seconds while the API scales up from zero.

## 8. Recommended: a budget alert

In the Azure portal, open **Cost Management → Budgets** and add a monthly budget of about £10 on the resource group, with an email alert at 80%.
