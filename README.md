# andrewmacleod-family-tree

A private, invite-only family tree for the MacLeod family, served at `family.andrewmacleod.com`.

- `src/FamilyTree.Api` — ASP.NET Core (.NET 10) minimal API.
- `web/` — React + TypeScript single-page app built with Vite.
- `tests/` — behaviour-focused API tests.
- `infra/` — Azure infrastructure as Bicep.

> **This repository is public. Never commit real family data.**
> People, photos, documents and GEDCOM files live only in Azure (SQL Database and private Blob Storage).
> Test and seed data must be fictional. `.gitignore` excludes common genealogy file types as a safety net.

## Prerequisites

- .NET 10 SDK (pinned in `global.json`)
- Node.js 22.12 or later

## Run locally

Start the API (http://localhost:5080):

```sh
dotnet run --project src/FamilyTree.Api
```

In a second terminal, start the web app (http://localhost:5173):

```sh
cd web
npm install
npm run dev
```

The Vite dev server proxies API paths such as `/health` to the local API. The home page shows whether the API is healthy.

To run the API as a container instead (needs Docker), build the image and run it on port 8080:

```sh
dotnet publish src/FamilyTree.Api -c Release -t:PublishContainer -p:ContainerImageTag=local
docker run --rm -p 8080:8080 familytree-api:local
```

## Test

```sh
dotnet test

cd web
npm test
```

Tests describe behaviour (HTTP requests and responses, what a user sees), not implementation details.

## How it works

A refresher for when you come back to this after a while. It assumes general engineering experience but not recent hands-on time with the specific Azure services and tools, so each piece explains what it is and why it was chosen.

### The big picture

The app has two halves that are built, hosted and deployed separately: a static front end (the React app) and a back-end API (.NET). The browser downloads the front end from one place, and the front end's JavaScript calls the API at another address.

```
 Browser
   │ 1. https://family.andrewmacleod.com
   ▼
 Azure Static Web App  (familytree-web, Free tier, East US 2)
   Serves the built React app: HTML, JavaScript and CSS only. No family data.
   │
   │ 2. The app's JavaScript calls the API from the browser (CORS allows this origin)
   ▼
 Azure Container App  (familytree-api, UK South, scales to zero)
   Runs the .NET API as a container image.
   │ pulls its image using a managed identity (no passwords)
   ▼
 Azure Container Registry  (familytree<random>, Basic tier, UK South)
```

Everything lives in one Azure **resource group**, `rg-familytree` (UK South). A resource group is a named container for related resources, which makes it easy to see costs, set permissions, or delete everything in one go.

### The pieces, and why each was chosen

**Azure Static Web Apps** (`familytree-web`) hosts the front end. It's a managed host for static files, served over HTTPS from Azure's global edge network. It has a free tier and handles custom domains and certificates for you. "Static" is the point: the files contain no family data, so they can be served to anyone. Everything private will come from the API, which will require sign-in from iteration 3 onwards. Static Web Apps can also set HTTP security headers (iteration 4), which GitHub Pages, used by the main andrewmacleod.com site, can't.

**Azure Container Apps** (`familytree-api`) runs the API. It's a serverless container platform, built on Kubernetes without exposing any of it. You give it a container image and it runs it behind an HTTPS endpoint, scaling the number of running copies (*replicas*) with traffic. Ours scales between 0 and 1 replicas. At zero it costs nothing, but the first request after a quiet spell waits a few seconds for a replica to start (a *cold start*). Container Apps run inside a **Container Apps environment** (`familytree-env`), a shared boundary for networking and logging. Logs go to a **Log Analytics workspace** (`familytree-logs`), which you can query in the portal or stream with the CLI (see [Everyday tasks](#everyday-tasks)).

**Azure Container Registry** (`familytree…`; the suffix is a stable hash because registry names must be globally unique) is a private Docker registry. Each deploy pushes a new API image there, tagged with the git commit SHA, so you can always tell which commit is running and roll back to any earlier one.

**The API's container image** is built by the .NET SDK itself (`dotnet publish -t:PublishContainer`), so there's no Dockerfile. The image settings are in [`FamilyTree.Api.csproj`](src/FamilyTree.Api/FamilyTree.Api.csproj). It uses Microsoft's *chiseled* base image: a stripped-down Ubuntu with no shell or package manager that runs as a non-root user. That leaves far less to attack and patch. The `-extra` variant the SDK picks includes the globalisation and time-zone data needed for culture-aware formatting.

**Bicep** ([`infra/main.bicep`](infra/main.bicep)) is Azure's infrastructure-as-code language, a friendlier syntax that compiles to ARM templates. It's *declarative*: it describes the resources that should exist, and Azure works out what to create or change. Deploying the same file twice is safe; the second run changes nothing. Bicep deployments only add or update resources, never delete them, so removing something from the file doesn't remove it from Azure.

**DNS and the custom domain.** `family.andrewmacleod.com` is a CNAME record at GoDaddy, which hosts DNS for andrewmacleod.com. It points to the Static Web App's generated `*.azurestaticapps.net` hostname. Static Web Apps checks that the CNAME exists, then issues and renews a free HTTPS certificate for the domain. The API keeps its generated `*.azurecontainerapps.io` address, because visitors never see it.

### How the browser is allowed to call the API (CORS)

Browsers block JavaScript from reading responses from a different *origin* (scheme + host + port) unless that server explicitly allows it. This is the **same-origin policy**, and the opt-in is **CORS** (Cross-Origin Resource Sharing): the browser sends an `Origin` header, and the server replies with `Access-Control-Allow-Origin` if that origin is allowed.

The front end and API are on different origins, so the API has to allow the front end's address. The API reads the allowed origins from configuration (`Cors:AllowedOrigins`, in [`Program.cs`](src/FamilyTree.Api/Program.cs)). In Azure, Bicep sets them as environment variables on the Container App:

- `Cors__AllowedOrigins__0`: the `azurestaticapps.net` address.
- `Cors__AllowedOrigins__1`: the custom domain, if one is set.

The double underscore is how ASP.NET Core maps environment variables onto nested configuration keys and array indexes. Any other website gets no CORS header, so a browser won't let its JavaScript read the API's responses. [`CorsTests.cs`](tests/FamilyTree.Api.Tests/CorsTests.cs) pins that behaviour.

Locally, none of this is needed. The Vite dev server proxies `/health` (and later API paths) to `localhost:5080`, so the browser only ever talks to one origin.

### How the front end finds the API

Vite replaces `import.meta.env.VITE_*` references with literal values **at build time**. The deploy workflow builds the front end with `VITE_API_BASE_URL` set to the API's address (taken from the Bicep deployment's outputs), so the address is baked into the published JavaScript. Locally it's unset, and [`apiUrl.ts`](web/src/shared/api/apiUrl.ts) falls back to the page's own origin, which is where the Vite proxy picks calls up.

### What happens when you merge to `main`

Pull requests run [`ci.yml`](.github/workflows/ci.yml): it builds, lints and tests the API and the front end, and deploys nothing. Merging to `main` runs [`deploy.yml`](.github/workflows/deploy.yml). It reuses `ci.yml` as its first job and runs these jobs in order, stopping at the first failure:

1. **Test**: the same checks as a pull request, so nothing untested reaches Azure.
2. **Infrastructure**: deploys `infra/main.bicep`. Before that, it looks up which API image is *currently running* and passes it in. Otherwise the template's placeholder image would replace the real API on every deploy. This job also collects Bicep's outputs (registry name, API and web addresses) for the later jobs.
3. **API**: builds the image, pushes it to the registry tagged with the commit SHA, and points the Container App at it. Container Apps then starts a new *revision* (an immutable snapshot of the app's configuration and image) and moves traffic to it. A smoke test calls `/health`, retrying to allow for a cold start. If a custom domain is set, a second test checks the API returns the right CORS header for it.
4. **Web app**: builds the front end with the API's address baked in, uploads `web/dist` to the Static Web App, then checks the page loads and has the expected title.

The run's summary page lists the addresses it deployed to. Deploys are queued rather than run in parallel (`concurrency: deploy-production`), so two quick merges can't overlap.

### How GitHub deploys to Azure without stored secrets

There are no passwords, keys or connection strings anywhere: not in the repo and not in GitHub secrets.

**GitHub → Azure uses OpenID Connect (OIDC) workload identity federation.** On each run, GitHub issues the job a short-lived signed token that says which repo, branch and environment it's running for. The `azure/login` action presents that token to Microsoft Entra ID (formerly Azure AD), which exchanges it for an Azure access token. This works because a **federated credential** on the managed identity `familytree-github-deploy` says: trust GitHub tokens whose *subject* is exactly

```
repo:MacLeodDevelopment@82574641/andrewmacleod-family-tree@1424382484:environment:production
```

Only jobs in this repo's `production` GitHub environment match. The numbers are GitHub's internal owner and repo IDs, which GitHub now includes so that a deleted and re-created repo with the same name isn't trusted. The `production` environment only accepts the `main` branch, so pull requests can't deploy.

A **managed identity** is an Azure-managed service account. It has no password; Azure issues tokens for it to whatever is allowed to use it.

What the deploy identity can do is set by **Azure RBAC** (role-based access control) role assignments. The first two are scoped to the resource group only:

- **Contributor**: create and change resources.
- **Role Based Access Control Administrator**, restricted by a *condition* so that the only role it can grant or remove is **AcrPull**. Bicep needs this to let the API's identity pull images. The condition stops a compromised pipeline from granting itself anything more.

The third is at subscription scope, because that's where Azure puts the thing it needs:

- **Web Operation Status Reader**, a custom role allowing only `Microsoft.Web/locations/*/read`. Some Static Web Apps operations, such as adding a custom domain, are long-running. Azure tracks their progress in a status resource under the *subscription*, not the resource group, and the deployment polls it to know when the operation has finished.

**The Container App pulls from the registry** using a second managed identity, `familytree-api-identity`, which has AcrPull on the registry. So there's no registry password.

**The Static Web App's upload token** is a secret the upload action requires. The workflow fetches it from Azure during each run (using the OIDC sign-in) and masks it in the logs, rather than storing it in GitHub.

The non-secret identifiers the workflow needs live as **variables on the `production` environment** in GitHub (Settings → Environments → production): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, and optionally `WEB_CUSTOM_DOMAIN`.

The one-off setup for all of this is in [docs/setup-azure.md](docs/setup-azure.md): resource group, deploy identity, federated credential, role assignments, GitHub environment and DNS.

### Where settings live

| Setting | Where | Notes |
|---|---|---|
| Resource names, regions, sizes | [`infra/main.bicep`](infra/main.bicep) | Defaults are in the file. Nothing secret. |
| Which Azure account to deploy to | GitHub `production` environment variables (see above) | Identifiers, not credentials, but kept out of this public repo. |
| Custom domain | GitHub `production` variable `WEB_CUSTOM_DOMAIN` | Empty or missing means no custom domain. Set it only once the DNS CNAME exists, because Azure validates the domain during the deploy. |
| API's allowed browser origins | Container App environment variables, set by Bicep | Derived from the Static Web App's address and the custom domain. Don't edit them in the portal: the next deploy overwrites them. |
| API address used by the front end | Baked into the front-end build | From Bicep's outputs during the deploy; unset locally. |
| .NET SDK version and test runner | [`global.json`](global.json) | Pins .NET 10 and opts in to Microsoft.Testing.Platform (see gotchas). |
| npm package versions | `web/package.json` (exact versions) and `web/package-lock.json` | `web/.npmrc` has `save-exact=true`, so new packages are pinned too. |

### Costs

About £4–5 a month, almost all of it the container registry's Basic tier.

- The Static Web App is on the Free tier.
- The Container App costs nothing at this usage, because it scales to zero and the monthly free allowance covers the rest.
- Logs fit in Log Analytics' free monthly allowance.

A budget alert on the resource group is worth having (setup guide, section 8).

### Decisions and gotchas

These are mostly lessons learned the hard way.

- **The first visit after a quiet spell is slow.** The API scales to zero, so it takes a few seconds to start, and the page shows "Checking the API…" meanwhile. Setting `minReplicas: 1` in Bicep would remove the delay, but costs a few pounds a month for an always-on replica.
- **The Static Web App is in East US 2, not the UK.**
  - On this subscription, Static Web Apps is only offered in a handful of regions: Central US, East US 2, West US 2, West Europe and East Asia. West Europe wasn't accepting new customers in October 2026.
  - The region only holds the app's configuration and the built files. Pages are served from the global edge, and all data is meant to stay in UK South.
  - To move it, delete and recreate it (Azure can't change a resource's region), then repoint the GoDaddy CNAME at the new `azurestaticapps.net` hostname.
  - The alternative considered was serving the front end from the API container in UK South. That would mean one origin and no CORS, but it would add a cold start to page loads.
- **Deploy fails at "Azure login" with `AADSTS700213: No matching federated identity record found`.** The federated credential's subject doesn't match the token GitHub sent. The error message includes the subject GitHub actually presented; the credential must match it exactly, IDs included.
- **Infrastructure step fails with only "Process completed with exit code 1".** The real error is in the step's log, which you need to be signed in to GitHub to see. Or reproduce it locally: `validate` runs Azure's pre-flight checks without changing anything.

  ```sh
  az deployment group validate -g rg-familytree --template-file infra/main.bicep --parameters apiImage=mcr.microsoft.com/dotnet/samples:aspnetapp
  ```

- **The Infrastructure step runs for ages (over 15 minutes) without finishing.**
  - Find what it's waiting on: `az deployment operation group list -g rg-familytree -n familytree-<run number> --query "[?properties.provisioningState!='Succeeded']"`, and read the `statusMessage`.
  - We hit this when adding the custom domain. The deploy identity lacked the subscription-level **Web Operation Status Reader** role, so every status check got `403 AuthorizationFailed` on `staticSitesOperationStatuses/read`, and Azure kept retrying for hours. The domain itself had been ready for an hour.
  - Fix the permission, cancel the stuck deployment (`az deployment group cancel -g rg-familytree -n familytree-<run number>`) and the workflow run, then deploy again.
  - Legitimately, a first-time custom domain can take 15–30 minutes while Azure validates it and issues the certificate.
- **`az deployment group what-if` shows "Modify" on most resources even when nothing has changed.** That's noise from properties Azure fills in itself, which aren't in the template. Look at the property-level differences before worrying.
- **Running `az` in Git Bash.** Git Bash's Unix emulation gets in the way in three ways:
  - It rewrites arguments that look like paths. Azure resource IDs start with `/`, so they're mangled and you get `MissingSubscription`. Fix: `export MSYS_NO_PATHCONV=1`.
  - It treats `!` as history expansion, giving `event not found`. Fix: `set +H`.
  - Arguments full of brackets and quotes get mangled. Put the value in a file and pass `@filename`, which `az` reads directly.
- **`dotnet test` on .NET 10 needs Microsoft.Testing.Platform.** .NET 10's `dotnet test` dropped the old VSTest mode for test frameworks like xUnit v3. [`global.json`](global.json) opts in to the new runner. The test project must also be an executable (`<OutputType>Exe</OutputType>`), which xUnit v3 requires.
- **Front-end tests that wait for async UI should use `waitFor`, not `findBy…` followed by a text assertion.** The status element exists immediately ("Checking the API…"), so `findByRole('status')` returns before the fetch completes. One test passed locally and failed on CI's slower runners until it was fixed.
- **This repo is public.** Code is fine; family data, exports and secrets are not. `.gitignore` blocks common genealogy file types as a safety net.

### Everyday tasks

```sh
# Stream the API's logs (Ctrl+C to stop)
az containerapp logs show -g rg-familytree -n familytree-api --follow

# List everything that's deployed
az resource list -g rg-familytree -o table

# Which API image is running (the tag is the commit SHA it was built from)
az containerapp show -g rg-familytree -n familytree-api --query "properties.template.containers[0].image" -o tsv
```

- **Redeploy without a code change:** GitHub → Actions → Deploy → Run workflow.
- **Roll back:**
  - The clean way: revert the commit on `main`. The Deploy run that follows rebuilds and deploys the previous code.
  - In an emergency: point the API at an older image directly, `az containerapp update -g rg-familytree -n familytree-api --image <registry>.azurecr.io/familytree-api:<older commit SHA>`. The next normal deploy carries on from there.
- **Add or change the custom domain:** see [setup guide section 9](docs/setup-azure.md#9-custom-domain).

### Where it's heading

The app is being built in small iterations, one pull request each, so the PR history is the record of what changed and why. Coming next, roughly in order:

- Sign-in with Microsoft Entra ID, so only invited family members can use it. Relatives will be invited as guests, and the API will check a role on every request.
- A database (Azure SQL) for people, families and life events.
- An editing UI that only Andrew can use.
- Search, profiles and tree views for viewers.
- A privacy rule: details of living people are hidden from viewers.
- Maps.

Relatives should not be invited until the privacy rule is deployed.

## Continuous integration and deployment

- `.github/workflows/ci.yml` builds, lints and tests the API and the web app on every pull request.
- `.github/workflows/deploy.yml` runs on every push to `main`: it runs the same tests, then deploys the infrastructure, the API and the web app to Azure, with a smoke test after each. See [How it works](#how-it-works).

Before the first deployment, follow the one-off [Azure setup](docs/setup-azure.md).
