# Sign-in setup (Microsoft Entra ID)

The family tree uses **Microsoft Entra ID** (formerly Azure Active Directory) for sign-in. Entra ID is Microsoft's identity service. It stores the users, handles the sign-in pages and MFA, and issues the tokens the API checks.

Everything here lives in the Entra **tenant** (directory) that owns the Azure subscription: *Default Directory*, `andrewmacleodcomgmail.onmicrosoft.com`. You set it up once, and again only when the web app's addresses or the roles change.

## What gets created

[`infra/entra.bicep`](../infra/entra.bicep) defines it all, using Bicep's Microsoft Graph extension (Microsoft Graph is the API for managing Entra).

- **An app registration, "Family Tree".** This is the app's definition. Its **client ID** (a GUID) is how the web app and the API identify it. It holds:
  - **Redirect URIs**: the web app addresses Entra may send users back to after signing in. Entra refuses any other address.
  - **A scope, `Family.Read`**: the permission the web app asks for when it requests a token to call the API.
  - **Two app roles, `Family.Viewer` and `Family.Admin`**: what a user may do. Entra puts a user's roles in their access token, and the API checks them on every request.
- **An enterprise application** (technically a *service principal*). This is the app's presence in the tenant. It has **Assignment required** turned on, so Entra refuses to sign in anyone who hasn't been given a role. Uninvited people never get as far as the API.
- **Consent grants on everyone's behalf.** Entra normally asks each user to approve an app's permissions the first time they sign in. These grants pre-approve sign-in and `Family.Read`, so family members never see that prompt.
- **A role assignment** making you a `Family.Admin`.

One registration covers both the web app (which signs users in) and the API (which the web app calls). Big systems usually register them separately. For a single web app talking to its own API, one is simpler, and assigning someone a role both lets them sign in and decides what they can do.

The GitHub deploy pipeline does **not** deploy this file. Changing Entra needs directory permissions that the deploy identity deliberately doesn't have, so you deploy it yourself with your own account.

## 1. Deploy the app registration

In Git Bash:

```sh
export MSYS_NO_PATHCONV=1
cd /c/DATA/000-Development/andrewmacleod-family-tree
az login --tenant d7ca0a01-3090-41f1-bb26-431ec7b60471

ADMIN_ID=$(az ad signed-in-user show --query id -o tsv)

az deployment group create \
  --resource-group rg-familytree \
  --name familytree-entra \
  --template-file infra/entra.bicep \
  --parameters \
    webOrigins='["http://localhost:5173","https://family.andrewmacleod.com","https://wonderful-wave-0f4b00e0f.6.azurestaticapps.net"]' \
    adminPrincipalId="$ADMIN_ID" \
  --query properties.outputs -o json
```

`webOrigins` lists every address the web app is served from: local development, the custom domain, and the Static Web App's own address. If any of them changes, update the list and run the command again. Redeploying is safe, because it updates the same registration.

The output includes `clientId`. Keep it for the next two steps. The client ID and tenant ID are identifiers, not secrets, but they stay out of this public repo.

## 2. Tell the deployment about it

In GitHub, go to **Settings → Environments → production** and add a variable:

| Name | Value |
|---|---|
| `ENTRA_CLIENT_ID` | the `clientId` from step 1 |

Do this **before** merging the change that needs it. The Deploy workflow stops with an error if the variable is missing, because the API refuses to start without it.

## 3. Configure local development

The API reads its sign-in settings from the `AzureAd` configuration section, and won't start without them. Locally, keep them in .NET **user secrets**. They're stored in your Windows profile, outside the repo, and are only used in the Development environment.

```sh
cd /c/DATA/000-Development/andrewmacleod-family-tree
dotnet user-secrets set "AzureAd:TenantId" "d7ca0a01-3090-41f1-bb26-431ec7b60471" --project src/FamilyTree.Api
dotnet user-secrets set "AzureAd:ClientId" "<clientId from step 1>" --project src/FamilyTree.Api
```

The web app reads its sign-in settings at build time from `VITE_*` environment variables. Locally, Vite loads them from `web/.env.local`, which git ignores. Copy [`web/.env.example`](../web/.env.example) to `web/.env.local` and fill in:

```sh
VITE_ENTRA_CLIENT_ID=<clientId from step 1>
VITE_ENTRA_TENANT_ID=d7ca0a01-3090-41f1-bb26-431ec7b60471
```

Restart `npm run dev` after changing it. Without the file, the app still runs, but shows "Sign-in isn't set up in this copy of the app" instead of a **Sign in** button.

Local sign-in works because `http://localhost:5173` is one of the `webOrigins` in step 1. Run the dev server on that port.

## 4. Check it

In the Azure portal, go to **Microsoft Entra ID → Enterprise applications → Family Tree**:

- **Properties**: *Assignment required?* is **Yes**.
- **Users and groups**: you're listed with the role **Family admin**.

## Giving someone access

> **Don't invite relatives yet.** Wait until the privacy rules are in place (see the README's "Where it's heading").

When the time comes, there are two steps per person:

1. **Invite them as a guest.** Go to **Microsoft Entra ID → Users → New user → Invite external user** and enter their email address. They sign in with their existing Microsoft or Google account, or with a one-time code emailed to them. No new password needed.
2. **Give them a role.** Go to **Enterprise applications → Family Tree → Users and groups → Add user/group**, pick the person, and choose **Family viewer** (or **Family admin**).

Assign roles to people directly, not to groups. Assigning app roles to groups needs a paid Entra ID P1 licence.

Someone without a role who tries to sign in sees a Microsoft error page saying they aren't assigned to the application (`AADSTS50105`).
