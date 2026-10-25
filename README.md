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

Both halves need the sign-in settings (the Entra tenant ID and the app's client ID). Set them once; see [setup-entra.md, step 3](docs/setup-entra.md#3-configure-local-development):

- **API:** .NET user secrets. The API won't start without them.
- **Web app:** `web/.env.local`. Without it, the page runs but sign-in is turned off.

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
   │ 2. The app's JavaScript calls https://api.family.andrewmacleod.com
   │    from the browser (CORS allows the web app's origin)
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

**DNS and the custom domains.** GoDaddy hosts DNS for andrewmacleod.com, and both halves have their own name there. Each points at the hostname Azure generated, and Azure issues and renews a free HTTPS certificate for each.

- **`family.andrewmacleod.com`** (the web app) is a CNAME to the Static Web App's `*.azurestaticapps.net` hostname. Static Web Apps checks the CNAME exists before accepting the domain.
- **`api.family.andrewmacleod.com`** (the API) is a CNAME to the Container App's `*.azurecontainerapps.io` hostname.
  - It also needs a TXT record, `asuid.api.family`, holding a verification ID for the Container Apps environment. That proves to Azure you control the domain.
  - Azure only issues the certificate once the hostname is attached to the app, so the first deploy after adding the domain runs Bicep twice: once to attach the hostname and get the certificate, then again to bind it (see [setup guide section 10](docs/setup-azure.md#10-custom-domain-for-the-api)).

The generated addresses still work, but nothing uses them. Your own names stay the same even if the Azure resources are ever recreated.

### How the browser is allowed to call the API (CORS)

Browsers block JavaScript from reading responses from a different *origin* (scheme + host + port) unless that server explicitly allows it. This is the **same-origin policy**, and the opt-in is **CORS** (Cross-Origin Resource Sharing): the browser sends an `Origin` header, and the server replies with `Access-Control-Allow-Origin` if that origin is allowed.

The front end and API are on different origins, so the API has to allow the front end's address. The API reads the allowed origins from configuration (`Cors:AllowedOrigins`, in [`Program.cs`](src/FamilyTree.Api/Program.cs)). In Azure, Bicep sets them as environment variables on the Container App:

- `Cors__AllowedOrigins__0`: the `azurestaticapps.net` address.
- `Cors__AllowedOrigins__1`: the custom domain, if one is set.

The double underscore is how ASP.NET Core maps environment variables onto nested configuration keys and array indexes. Any other website gets no CORS header, so a browser won't let its JavaScript read the API's responses. [`CorsTests.cs`](tests/FamilyTree.Api.Tests/CorsTests.cs) pins that behaviour.

Locally, none of this is needed. The Vite dev server proxies `/health` (and later API paths) to `localhost:5080`, so the browser only ever talks to one origin.

### How the front end finds the API

Vite replaces `import.meta.env.VITE_*` references with literal values **at build time**. The deploy workflow builds the front end with `VITE_API_BASE_URL` set to the API's address (taken from the Bicep deployment's outputs), so the address is baked into the published JavaScript. Locally it's unset, and [`apiUrl.ts`](web/src/shared/api/apiUrl.ts) falls back to the page's own origin, which is where the Vite proxy picks calls up.

### Sign-in and access control

Only invited family members can use the app. Sign-in is handled by **Microsoft Entra ID**, Microsoft's identity service, using the tenant that came with the Azure subscription. The API never sees a password: it only checks tokens that Entra has signed.

**The flow:**

1. **Sign in** sends the browser to Microsoft's sign-in page (a full-page redirect). Users sign in with their own Microsoft or Google account, or a one-time code by email. Relatives are added to the tenant as *guests*.
2. Entra checks the user has been given a role in the app. If not, it refuses them right there (*Assignment required*, error `AADSTS50105`), and the page shows a message saying they haven't been given access.
3. Entra redirects back to the web app with a one-time **authorisation code**. The web app exchanges the code for tokens. The **access token** is a signed JWT saying who the user is (`name`), which app it's for (`aud`, the client ID), and their roles (`roles`, such as `Family.Viewer`). It's valid for about an hour.
4. The web app sends the access token with each API call, in an `Authorization: Bearer …` header. That's why CORS allows the `Authorization` header.
5. The API validates the token: Entra's signature, issuer, audience and expiry. Then it checks the user has a family role.

**In the web app** ([`web/src/shared/auth/`](web/src/shared/auth)):

- **MSAL** (`@azure/msal-browser`, the Microsoft Authentication Library) handles all of steps 1 to 4.
- It uses the **authorisation code flow with PKCE** ("pixy"), the standard for browser apps. A browser can't keep a client secret, so instead each sign-in creates a one-off random secret, and only its hash goes into the sign-in request. The code from step 3 is useless without the original. That stops anyone who intercepts the code from redeeming it.
- MSAL keeps tokens in `sessionStorage`, so they're forgotten when the tab closes. It renews the access token behind the scenes until the sign-in session ends. After that, the next API call sends the user back through sign-in.
- Sign-in uses full-page redirects, not pop-ups (pop-ups are unreliable on phones). So the signed-in user can only change on a page load. [`main.tsx`](web/src/main.tsx) works out who's signed in once, before rendering, and also completes a sign-in when the page loads on the way back from Microsoft.
- **Components never use MSAL directly.** They use a small `Auth` interface ([`auth.ts`](web/src/shared/auth/auth.ts)): the user, sign in, sign out, get a token. [`msalAuth.ts`](web/src/shared/auth/msalAuth.ts) is the only file that knows about MSAL, and it's provided to the app through React context. That keeps MSAL replaceable, and lets tests supply a pretend user ([`renderApp.tsx`](web/src/test/renderApp.tsx)).
- **Settings:** the client ID and tenant ID come from `VITE_ENTRA_CLIENT_ID` and `VITE_ENTRA_TENANT_ID` at build time. The API scope (`api://<tenant>/familytree/Family.Read`) is derived from the tenant ID and must match `infra/entra.bicep`. If the settings are missing, the app runs with sign-in turned off.
- The header shows **Sign in**. Once signed in, it shows "Signed in as **Andrew MacLeod** · andrewmacleod.com@gmail.com · Family admin", confirmed by calling `/api/v1/me`, which proves the whole chain works.
  - The username matters because many people have several Microsoft accounts (work, personal, an old Hotmail). "Which account am I signed in with?" is the first question when someone can't get in, and the username is what you look up in Entra.
  - Profiles don't always have a display name. The header then shows just the username, and plain "Signed in" if there's neither.
  - If a signed-in account has no family role, the message names that account.
  - The name and username come from sign-in itself, so they're shown immediately and never change; only the role, from the API, fades in. "Checking your access…" appears only if the API takes over a second (for example, a cold start). On a fast connection it would just flash up and vanish, which looks like a glitch.

**The Entra setup** is defined in [`infra/entra.bicep`](infra/entra.bicep): one app registration, "Family Tree", with a `Family.Read` scope and two app roles, `Family.Viewer` and `Family.Admin`. You deploy it by hand rather than through the pipeline, so the GitHub deploy identity never needs permission to change the directory. [docs/setup-entra.md](docs/setup-entra.md) explains each part and covers deploying it, configuring local development, and giving someone access.

**In the API** ([`Program.cs`](src/FamilyTree.Api/Program.cs)):

- **Microsoft.Identity.Web** is Microsoft's library for validating Entra tokens in ASP.NET Core. It reads the tenant and client IDs from the `AzureAd` configuration section. In Azure, Bicep sets these as environment variables, `AzureAd__TenantId` and `AzureAd__ClientId`. Locally they come from user secrets.
- **Deny by default.** A *fallback authorisation policy* applies to every endpoint that doesn't say otherwise. It requires a signed-in user with `Family.Viewer` or `Family.Admin`. New endpoints are therefore protected automatically. `/health` opts out with `.AllowAnonymous()`.
- **Status codes.** No valid token gives **401 Unauthorized**. A valid token without a family role gives **403 Forbidden**.
- **Fail fast.** If the `AzureAd` settings are missing, the API refuses to start, with a message naming the missing setting. Otherwise it would start and then fail on every request.
- **`GET /api/v1/me`** returns the signed-in user's `name`, `username` and family `roles`. It's the first protected endpoint, and the web app uses it to show who's signed in.
  - `name` comes from the token's `name` claim, the display name from their profile.
  - `username` comes from `preferred_username`: the account they signed in with, usually an email address. It's for display only. It can change, so never use it to decide access; that's what roles are for.
  - Either is `null`, not an empty string, when the token doesn't carry it.

**In the tests**, there's no way to get a real Entra token, so [`TestUsers.cs`](tests/FamilyTree.Api.Tests/TestUsers.cs) swaps in a test-only sign-in scheme.
- It creates the user described by a small `TestUser` record (name, username, roles, any of which can be missing), with the same `name`, `preferred_username` and `roles` claims a real token has.
- Requests with no user still go through the real Microsoft.Identity.Web pipeline, so the 401 test exercises the real thing.
- The test host runs in a `Testing` environment, not `Development`, so it never picks up your user secrets. They once made a test pass in CI and fail on this machine.

### What happens when you merge to `main`

Pull requests run [`ci.yml`](.github/workflows/ci.yml): it builds, lints and tests the API and the front end, and deploys nothing. Merging to `main` runs [`deploy.yml`](.github/workflows/deploy.yml). It reuses `ci.yml` as its first job and runs these jobs in order, stopping at the first failure:

1. **Test**: the same checks as a pull request, so nothing untested reaches Azure.
2. **Infrastructure**: deploys `infra/main.bicep`. Before that, it looks up which API image is *currently running* and passes it in. Otherwise the template's placeholder image would replace the real API on every deploy. It also checks whether the API custom domain's certificate exists yet; if not, it runs Bicep a second time to bind the new certificate. This job collects Bicep's outputs (registry name, API and web addresses) for the later jobs.
3. **API**: builds the image, pushes it to the registry tagged with the commit SHA, and points the Container App at it. Container Apps then starts a new *revision* (an immutable snapshot of the app's configuration and image). Once that revision is up, Container Apps moves the traffic to it.
   - The job waits until the new revision is healthy and taking 100% of the traffic, polling for up to 5 minutes.
   - It fails if the revision doesn't get there. Otherwise a revision that fails to start would leave the *old* one quietly serving, and the smoke tests would pass against the wrong version.
   - Smoke tests then check:
     - `/health` responds, retrying to allow for a cold start.
     - `/api/v1/me` refuses an anonymous request with 401, which proves sign-in is enforced.
     - If a custom domain is set, the API returns the right CORS header for it.
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

The non-secret identifiers the workflow needs live as **variables on the `production` environment** in GitHub (Settings → Environments → production): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `ENTRA_CLIENT_ID`, and optionally `WEB_CUSTOM_DOMAIN` and `API_CUSTOM_DOMAIN`. Don't confuse the two client IDs:
- `AZURE_CLIENT_ID` is the *deploy identity* that GitHub signs in to Azure as.
- `ENTRA_CLIENT_ID` is the *Family Tree app registration* that family members sign in to.

The one-off setup for all of this is in [docs/setup-azure.md](docs/setup-azure.md): resource group, deploy identity, federated credential, role assignments, GitHub environment and DNS.

### Where settings live

| Setting | Where | Notes |
|---|---|---|
| Resource names, regions, sizes | [`infra/main.bicep`](infra/main.bicep) | Defaults are in the file. Nothing secret. |
| Which Azure account to deploy to | GitHub `production` environment variables (see above) | Identifiers, not credentials, but kept out of this public repo. |
| Custom domain for the web app | GitHub `production` variable `WEB_CUSTOM_DOMAIN` | Empty or missing means no custom domain. Set it only once the DNS CNAME exists, because Azure validates the domain during the deploy. |
| Custom domain for the API | GitHub `production` variable `API_CUSTOM_DOMAIN` | Same rules, but it needs both the CNAME and the `asuid` TXT record first. When set, the web app is built to call it. |
| API's allowed browser origins | Container App environment variables, set by Bicep | Derived from the Static Web App's address and the custom domain. Don't edit them in the portal: the next deploy overwrites them. |
| API address used by the front end | Baked into the front-end build | From Bicep's outputs during the deploy; unset locally. |
| Sign-in app registration, roles, redirect URIs | [`infra/entra.bicep`](infra/entra.bicep), deployed by hand | Redeploy when the web app's addresses change. See [docs/setup-entra.md](docs/setup-entra.md). |
| API's sign-in settings in Azure | Container App environment variables `AzureAd__TenantId` and `AzureAd__ClientId`, set by Bicep | The client ID comes from the GitHub `production` variable `ENTRA_CLIENT_ID`; the tenant is the subscription's own. |
| API's sign-in settings locally | .NET user secrets (`dotnet user-secrets list --project src/FamilyTree.Api`) | Stored in your Windows profile, never in the repo. |
| Web app's sign-in settings | Build-time `VITE_ENTRA_CLIENT_ID` and `VITE_ENTRA_TENANT_ID`. In Azure they come from the GitHub variables `ENTRA_CLIENT_ID` and `AZURE_TENANT_ID`; locally, from `web/.env.local` (git-ignored; template in `web/.env.example`). | Baked into the published JavaScript, which is fine: neither is a secret. |
| Who can sign in, and with which role | Azure portal → Entra ID → Enterprise applications → Family Tree → Users and groups | Not in code: these are people. |
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
- **A workflow step fails with only "exit code 1" and no message of its own.** GitHub runs `run:` steps with `bash -e`, which ends the script at the first command that returns non-zero, even one whose failure looks harmless.
  - We hit this with `read`. Given input without a trailing newline, `read` still sets the variables but returns 1, which killed the revision-wait step on its first check.
  - Feed `read` from a here-string (`<<< "$var"`), which supplies the newline.
  - Test step scripts locally with `bash -e script.sh`, not plain `bash`.
  - Output from `az` on Windows ends lines with `\r\n`, so scripts that parse it should strip `\r` too.
- **Vulnerable packages fail the build.** [`Directory.Build.props`](Directory.Build.props) turns NuGet's audit warnings for moderate, high and critical vulnerabilities into errors. Every restore checks direct and *transitive* packages against GitHub's advisory database.
  - So a newly published advisory can suddenly break an unchanged build. That's intended: fix it, don't suppress it.
  - Find the culprit with `dotnet list package --vulnerable --include-transitive`, and why it's there with `dotnet nuget why <project> <package>`. Usually the fix is to pin the patched version with a direct `PackageReference` and a comment saying when to remove it.
  - Microsoft.Identity.Web 4.16.1 currently needs two such pins, `System.Security.Cryptography.Pkcs` in the API project and `System.Security.Cryptography.Xml` in the test project. Packages that are part of the ASP.NET Core shared framework, such as `.Xml`, can't be pinned in the web project (warning NU1510); they're patched by updating the runtime.
- **Can't sign in to Azure CLI: `AADSTS50076 … you must use multi-factor authentication`.** The tenant enforces MFA (Entra *security defaults*), and the CLI's session has expired. Run `az login --tenant d7ca0a01-3090-41f1-bb26-431ec7b60471` again.
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
- **Add or change a custom domain:** see setup guide sections [9 (web app)](docs/setup-azure.md#9-custom-domain) and [10 (API)](docs/setup-azure.md#10-custom-domain-for-the-api).

### Where it's heading

The app is being built in small iterations, one pull request each, so the PR history is the record of what changed and why. Coming next, roughly in order:

- Hardening: security headers (including a Content Security Policy), rate limiting, consistent error responses.
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
