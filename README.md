# andrewmacleod-family-tree

A private, invite-only family tree for the MacLeod family, served at `family.andrewmacleod.com`.

- `src/FamilyTree.Api` — ASP.NET Core (.NET 10) minimal API.
- `src/FamilyTree.Domain` — the family tree's concepts and rules, such as `Person`. It depends on nothing else.
- `src/FamilyTree.Infrastructure` — the database (EF Core), its migrations, and how it's deployed.
- `src/FamilyTree.DbMigrator` — a small tool that brings the database up to date (run by the pipeline, and locally).
- `web/` — React + TypeScript single-page app built with Vite.
- `tests/` — behaviour-focused API tests.
- `infra/` — Azure infrastructure as Bicep.
- `docker-compose.yml` — SQL Server for local development.

> **This repository is public. Never commit real family data.**
> People, photos, documents and GEDCOM files live only in Azure (SQL Database and private Blob Storage).
> Test and seed data must be fictional. `.gitignore` excludes common genealogy file types as a safety net.

## Prerequisites

- .NET 10 SDK (pinned in `global.json`)
- Node.js 22.12 or later
- Docker Desktop, for the local database and the database tests

## Run locally

Both halves need the sign-in settings (the Entra tenant ID and the app's client ID). Set them once; see [setup-entra.md, step 3](docs/setup-entra.md#3-configure-local-development):

- **API:** .NET user secrets. The API won't start without them.
- **Web app:** `web/.env.local`. Without it, the page runs but sign-in is turned off.

Start the local database (SQL Server in Docker, on localhost port 14333), and create or update its schema. Run the migrator again after pulling changes that include new migrations:

```sh
docker compose up -d
dotnet run --project src/FamilyTree.DbMigrator -- --connection "Server=127.0.0.1,14333;Database=familytree;User Id=sa;Password=Local-dev-only-Pa55;TrustServerCertificate=True"
```

Use `127.0.0.1`, not `localhost`: the container only listens on IPv4, and SqlClient tries `localhost`'s IPv6 address first and times out (error 258). The connection string and password are for the local container only. The API's copy is in `appsettings.Development.json`. In Azure there are no database passwords at all (see [The database](#the-database)).

Start the API (http://localhost:5080). http://localhost:5080/health/ready should say `Healthy` once it can reach the database:

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

The dev server doesn't send the [security headers](#security-headers-on-the-web-app). To try the production build *with* them, including signing in under the Content Security Policy, stop `npm run dev` and run:

```sh
cd web
npm run build
npm run preview    # http://localhost:5173, same port, so sign-in still works
```

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

Everything lives in one Azure **resource group**, `rg-familytree` (UK South), although a resource group's region is only where its metadata is kept: the database is in Sweden Central and the web app's host is in East US 2. A resource group is a named container for related resources, which makes it easy to see costs, set permissions, or delete everything in one go.

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

### The database

The family tree's data lives in **Azure SQL Database**, Microsoft's managed SQL Server, accessed through **Entity Framework Core** (EF Core), .NET's standard ORM.

**The resources** ([`infra/main.bicep`](infra/main.bicep)):
- A SQL **server** (`familytree-sql-<random>`, **Sweden Central**): a logical container that holds the database, its firewall and who administers it.
- A **database**, `familytree`. It's **serverless**: it scales its compute with load, and **pauses** after an hour with no use.
  - It's on Azure SQL's **free offer**: 100,000 vCore-seconds of compute and 32 GB of storage a month, at no cost. If the allowance ever runs out, it pauses until the next month rather than charging.
  - **Waking up takes time.** The first request after a pause waits while the database resumes, up to a minute. The API's connection retries (`EnableRetryOnFailure` in [`SqlServerOptions.cs`](src/FamilyTree.Infrastructure/SqlServerOptions.cs)) and its generous connect timeout cover this.

**No passwords: Microsoft Entra authentication only.** The server accepts no SQL logins (`azureADOnlyAuthentication`). Everything signs in with an Entra identity:
- **The deploy identity** (`familytree-github-deploy`) is the server's **administrator**. The pipeline uses it to apply schema changes.
- **The API's identity** (`familytree-api-identity`, the same managed identity it uses to pull images) is a **database user** with `db_datareader` and `db_datawriter`. It can read and write data, but not change tables, permissions or anything else. If the API were ever compromised, it couldn't drop the database.
  - Its connection string ends `Authentication=Active Directory Managed Identity;User Id=<its client ID>`. Bicep sets it as `ConnectionStrings__FamilyTree`, and it contains no secret.
- Your own Gmail-based Microsoft account can't be a SQL administrator; Azure SQL doesn't accept personal Microsoft accounts there. To look at the data directly, the usual route would be a cloud-only member account in the tenant (not set up).

**Schema changes go through EF Core migrations, applied by the pipeline, never by the API.** A *migration* is a C# file, generated by EF Core from changes to the model, that alters the database schema (creates a table, adds a column). Migrations live in the Infrastructure project and are applied in order; the database records which it has.
- [`DatabaseDeployment.cs`](src/FamilyTree.Infrastructure/DatabaseDeployment.cs) does two things, and the [`FamilyTree.DbMigrator`](src/FamilyTree.DbMigrator/Program.cs) tool runs it:
  1. **`MigrateAsync`** applies any migrations the database doesn't have yet. Locally it also creates the database.
  2. **`GrantReadWriteAsync`** makes sure the API's identity is a database user with read/write. It creates the user by its SID, which is derived from the identity's client ID, rather than `FROM EXTERNAL PROVIDER`. That avoids giving the SQL server permission to read your Entra directory.
- **In the pipeline**, the **Database** job runs between Infrastructure and API, so the new API never meets an old schema:
  - It signs in as the administrator (`Authentication=Active Directory Default`, which uses the Azure CLI's login).
  - It opens the SQL firewall to the runner's own IP address for that job only, and always closes it, even if a step fails.
  - It runs the migrator, with retries, because new firewall rules take a moment and a paused database takes a minute to resume.
- **Locally**, you run the migrator yourself (see [Run locally](#run-locally)).

**Network.** The SQL firewall admits Azure services (the `AllowAllWindowsAzureIps` rule), which is how the Container App reaches it. That rule technically admits *any* Azure customer's services, but the firewall is only the first gate: every connection still needs a valid Entra token for a user that exists in the database. The stricter alternative, a private network with a private endpoint, costs extra and isn't justified yet.

**Health checks.** `/health` says the API process is up, without touching the database, so a paused database doesn't make the API look broken. `/health/ready` also checks the database (EF Core's `CanConnectAsync`); it returns 503 if the database can't be reached.

**Adding or changing a table:**
1. Change the model in `FamilyTree.Domain`, and its mapping in [`FamilyTreeDbContext.cs`](src/FamilyTree.Infrastructure/FamilyTreeDbContext.cs).
2. Generate the migration, then read what it produced before committing:

   ```sh
   dotnet tool restore     # once, to get dotnet-ef at the version pinned in dotnet-tools.json
   dotnet ef migrations add <DescriptiveName> --project src/FamilyTree.Infrastructure
   ```

3. Apply it locally with the migrator (see [Run locally](#run-locally)). The Database job applies it in Azure when you merge.

The API has no permission to change the schema, so a migration can only ever reach Azure through the pipeline. `MigrateAsync` also refuses to run if the model has changes that no migration covers.

**In the tests**, the database tests use **Testcontainers**: each run starts a real SQL Server in Docker, migrates it with the same `DatabaseDeployment` code, and throws it away afterwards ([`SqlServerContainer.cs`](tests/FamilyTree.Api.Tests/SqlServerContainer.cs)). Other tests get a connection string that points nowhere, so they stay fast. CI's Linux runners have Docker; locally, Docker Desktop must be running.

### People

A **person** ([`Person.cs`](src/FamilyTree.Domain/Person.cs)) has a recorded **sex** (`Female`, `Male` or `Unknown`, as written in the records, which are often incomplete) and one or more **names**. Each name has a given name, an optional surname and a type (`Birth`, `Married`, `Alias` or `Nickname`). The first name in the list is the person's main name. In the database that's a `People` table and a `PersonNames` table.

**The rules live in the domain.** `Person.Create` is the only way to make a person, and it enforces that they have at least one name, that every name has a given name, and that names fit in their columns. It returns a `Result` rather than throwing, because bad input is an expected outcome, not an exception. Errors are keyed by field (`names[0].givenName`), and the API returns them as a 400 *validation problem*: the same problem-details shape as other errors, plus an `errors` object the edit screens can show next to each field.

**The endpoints** ([`PeopleEndpoints.cs`](src/FamilyTree.Api/People/PeopleEndpoints.cs)):

| Request | What it does |
|---|---|
| `POST /api/v1/admin/people` | Adds a person. Returns 201 with the person, and their address in `Location`. |
| `GET /api/v1/admin/people/{id}` | Reads a person, or 404. |
| `GET /api/v1/admin/people?skip=0&take=50` | Lists people a page at a time (at most 200), with the total. |
| `DELETE /api/v1/admin/people/{id}` | Permanently removes a person, with their names and events. Returns 204, or 404. |

- **Possible duplicates.** Adding someone whose main name matches a name on an existing person (ignoring case) stops with **409 Conflict**, listing the matches in `duplicates`. If it's a different person, send it again with `?allowDuplicate=true`.
  - It's a prompt, not a rule. Families reuse names, so the same name is often a different person. Birth years can't help here, because a person is created before their events.
  - That's why there's no unique constraint in the database. Two identical requests sent at the same instant could both succeed.
  - Merging two records comes later, once people have relationships to carry across. Deleting will need more thought then too.
- **Admins only.** These sit under `/admin` and require the `FamilyAdmin` policy, so a `Family.Viewer` gets 403. That's deliberate: they return everything about a person. Viewers will read people through separate endpoints that apply the privacy rules, once those exist.
- **IDs** are version 7 GUIDs generated by the code. They begin with the time, so new rows land at the end of the table's index rather than scattering across it.
- **Why there's no Application project yet.** The design has one for use cases, but with this little logic the endpoints can call the domain and EF Core directly. It'll arrive when something needs it, such as the privacy rules.
- **Trying them:** there's no screen yet. [`FamilyTree.Api.http`](src/FamilyTree.Api/FamilyTree.Api.http) has ready-made requests, for VS Code's REST Client extension (`humao.rest-client`) or Visual Studio. It reads your token from `src/FamilyTree.Api/.env`, which git ignores, and explains how to copy one from the browser. Use fictional people for now: the privacy rules don't exist yet.

### Dates and life events

**Family history dates are rarely exact**, so they aren't stored as a `DateTime`. [`GenealogicalDate.cs`](src/FamilyTree.Domain/GenealogicalDate.cs) understands how records actually write them, and shows each in British style:

| Entered | Shown |
|---|---|
| `12 Mar 1901`, `12/03/1901`, `1901-03-12` | 12 March 1901 |
| `Mar 1901`, `1901` | March 1901, 1901 |
| `abt 1870`, `c. 1870`, `circa 1870` | c. 1870 |
| `est 1870`, `calc 1870` | est. 1870, calc. 1870 |
| `bef Mar 1901`, `aft 1880` | before March 1901, after 1880 |
| `bet 1870 and 1875`, `1870-75` | between 1870 and 1875 |
| `Q2 1880` (a registration quarter) | between April 1880 and June 1880 |
| `1650/51`, `11 Feb 1731/2` (dual years) | 1650/51, 11 February 1731/32 |

- **The text as entered is what's stored** (`Date` in `PersonEvents`), and it's parsed again on load. It's the source record: "abt 1870" and 1870 mean different things, and if the parser improves, old entries benefit.
- **Ordering** uses a calculated `SortKey`, so "before 1880" sorts ahead of 1880 and "after 1880" behind it. It's worked out in memory, because a person only has a handful of events. A query across everyone, such as "the 1900s" on the map, will need a stored sort column; that's a migration when it's needed.
- **Dates before 1752 follow the rules of the time.** The column is text, so the database has no earliest date (SQL Server's old `datetime` type stops at 1753), and the tree can go back as far as the records do.
  - **Dual years.** Until 1752 the legal year in England, Wales and Ireland began on 25 March, so a date we'd call February 1651 was still 1650 then, and was often written `1650/51`. Scotland moved its new year to 1 January in 1600. These are accepted up to 1752, for January to March when a month is given, and the later year must follow the first. They sort by the later year, which is the year by today's reckoning.
  - **Leap years.** Britain used the Julian calendar until 1752, where every fourth year was a leap year, so 29 February 1700 is valid. After 1752 the modern rules apply, so 29 February 1800 is refused.
  - **The missing days.** The 11 days dropped in September 1752 are accepted, because other countries changed calendar at other times.
- **What it doesn't understand yet:** years before 1000, because years are four digits, and non-English month names. They're refused with a message listing what works, not guessed at.

**Events.** A person has a timeline of **life events** ([`Person.cs`](src/FamilyTree.Domain/Person.cs)), each with a type, an optional date and an optional place. For now the types are `Birth` and `Death`, and a place is free text; proper places come later. `POST /api/v1/admin/people/{id}/events` adds one, and a person's record returns them in date order, undated last, with `date` for display and `dateAsEntered` for editing. A person can only have one birth and one death. Conflicting sources will need handling when sources arrive.

### Errors and rate limiting in the API

**Every error has the same shape.** The API returns errors as **problem details** (RFC 9457), a standard JSON body with content type `application/problem+json`:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "traceId": "00-…" }
```

- **Where it comes from:** `AddProblemDetails()` registers the format. `UseStatusCodePages()` gives a body to responses that would otherwise be empty: the 401 from authentication, the 403 from authorisation, a 404 for an unknown route. `UseExceptionHandler()` turns any unhandled exception into a plain 500 in the same shape.
- **What it hides:** outside the Development environment, the 500 says nothing about the code. No exception type, message or stack trace, which can leak things like connection strings.
- **`traceId`:** matches the request in the logs, so an error a user reports can be found.
- **Why it matters:** the edit screens to come will need to show validation errors, and they'll use the same shape.

**Rate limiting** ([`RateLimitingExtensions.cs`](src/FamilyTree.Api/RateLimiting/RateLimitingExtensions.cs)) stops a script, a buggy page or a runaway loop from overwhelming the API or running up the Azure bill.

- **Who it limits:**
  - Each **signed-in user** has their own allowance, 120 requests a minute, keyed by their Entra object ID.
  - **Anonymous callers** (only `/health` is open to them) get 30 a minute per IP address.
- **When it says no:** over the limit, the API replies **429 Too Many Requests**, with a `Retry-After` header saying how many seconds to wait and a problem-details body. CORS exposes `Retry-After`, so the web app can read it.
- **How it counts:** it's a *fixed window*. The count resets every minute and nothing is queued. Family use is nowhere near these numbers.
- **Changing the limits:** they come from configuration (`RateLimiting:SignedInPermitsPerMinute` and `RateLimiting:AnonymousPermitsPerMinute`), so they can be changed without code. The tests use tiny ones.
- **Real addresses behind the proxy.** In Azure every request reaches the API through the Container Apps ingress proxy (Envoy). Without help, every anonymous caller would appear to have the proxy's address and share one allowance.
  - The proxy passes the caller's real address in `X-Forwarded-For`, and `UseForwardedHeaders()` puts it into `RemoteIpAddress`.
  - The proxy is trusted because the app can't be reached any other way.
  - Only the last address in the header is used, the one the proxy added, so a caller can't claim to be someone else by sending their own `X-Forwarded-For`.

**The middleware order in [`Program.cs`](src/FamilyTree.Api/Program.cs) matters.** Each step wraps the ones after it:
1. Forwarded headers, so everything after sees the real address.
2. The exception handler and status-code pages, so every error below gets a body.
3. CORS, so preflights are answered before authentication, and error responses still carry CORS headers the browser lets the app read.
4. Authentication.
5. The rate limiter, after authentication so it knows who's calling.
6. Authorisation.

### How the front end finds the API

Vite replaces `import.meta.env.VITE_*` references with literal values **at build time**. The deploy workflow builds the front end with `VITE_API_BASE_URL` set to the API's address (taken from the Bicep deployment's outputs), so the address is baked into the published JavaScript. Locally it's unset, and [`apiUrl.ts`](web/src/shared/api/apiUrl.ts) falls back to the page's own origin, which is where the Vite proxy picks calls up.

### Security headers on the web app

Static Web Apps sends HTTP response headers that tell the browser what the page may and may not do. They're defined in [`web/build/staticWebApp.ts`](web/build/staticWebApp.ts).

**How they reach Azure.** A small plugin in [`vite.config.ts`](web/vite.config.ts) writes them into `dist/staticwebapp.config.json` during `npm run build`. Static Web Apps reads that file from the uploaded site.
- They're generated rather than hand-written because the Content Security Policy must name the API's address, and that's only known at build time: it's the custom domain, or the generated address if there isn't one.
- The same file sets a **navigation fallback**: any path that isn't a real file, such as `/people/123`, serves the app instead of a 404. The app decides what each path shows.

**What they do:**
- **Content Security Policy (CSP).** The main one. The browser refuses anything the policy doesn't list. So if an attacker ever got script onto the page (cross-site scripting), it couldn't run inline, load code from elsewhere, or send data anywhere except our own API and Microsoft sign-in.
  - `script-src 'self'`: scripts only from this site, never inline. Vite's production build has no inline scripts or styles, so nothing needs loosening.
  - `connect-src 'self' <API origin> https://login.microsoftonline.com`: the only places the page may make requests to. MSAL talks to Microsoft for tokens; the app talks to the API.
  - `frame-src https://login.microsoftonline.com`: MSAL can renew tokens in a hidden iframe on Microsoft's sign-in page.
  - `frame-ancestors 'none'`: no other site may show this one inside a frame (click-jacking).
  - `object-src 'none'`, `base-uri 'self'` and `form-action 'self'` close the less obvious holes.
- **`Strict-Transport-Security`**: browsers use HTTPS for this site for a year, even if someone types `http://`.
- **`X-Content-Type-Options: nosniff`**: files are only treated as the type the server says, never guessed.
- **`Referrer-Policy: strict-origin-when-cross-origin`**: other sites can see that a visitor came from here, but not which page (paths could one day contain people's IDs).
- **`Permissions-Policy`** turns off camera, microphone, location, payments and USB, which the app never needs.
- **`Cross-Origin-Opener-Policy: same-origin`**: windows from other sites can't get a handle on this one.
- **`X-Robots-Tag: noindex, nofollow`**: asks search engines not to list this private site.

**Trying them locally.** The Vite dev server (`npm run dev`) doesn't send them, because its hot reloading relies on inline scripts that the policy forbids. `npm run build` then `npm run preview` serves the production build on port 5173 with the *exact* headers from `dist/staticwebapp.config.json`, so you can sign in and click around under the real policy. If the browser blocks something, its console shows a "Refused to …" message naming the rule.

**If something is blocked in production** (for example, after adding a map library that loads tiles from another domain), add that origin to the relevant directive in `staticWebApp.ts`. Never add `'unsafe-inline'` to `script-src`, because that would undo most of the protection.

The deploy checks the headers are really being served (see below), and [`staticWebApp.test.ts`](web/build/staticWebApp.test.ts) pins the important rules.

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
- The header shows **Sign in**. Once signed in, it shows a two-line block: the name in bold, then the account (username) and a role badge such as **Family admin**. The role is confirmed by calling `/api/v1/me`, which proves the whole chain works.
  - The username matters because many people have several Microsoft accounts (work, personal, an old Hotmail). "Which account am I signed in with?" is the first question when someone can't get in, and the username is what you look up in Entra.
  - Profiles don't always have a display name. The block then shows just the username, and plain "Signed in" if there's neither.
  - If the account has no family role, the badge says **No access**. If the API can't be reached, it says **Unavailable**. Both show in red, and the full explanation (naming the account) is in a tooltip and read out by screen readers.
- **Nothing in the header moves while it loads, or afterwards.** Three things make that so; each was added after the one before it still let something jump:
  1. **The name and username come from sign-in only** (MSAL's account, from the ID token), never from the API's response. They're known from the first frame, so they can't change. The API supplies only the role. Its identity fields could differ (for example, a token without `preferred_username`), and showing them made the line change length when it answered. Access is enforced by the API either way.
  2. **The block has a fixed size**: 17rem wide on desktop. On phones (420px and below) the header becomes two rows, with the title and **Sign out** on top and the block across the full width below, so its width depends only on the screen. Long names and emails are cut short with "…", and the full text is in a tooltip. Within the second line, the badge is pinned to the right, so it doesn't move with the email's length.
  3. **The role badge has its final size from the first frame.** CSS sizes it from an invisible copy of the widest label it can show (`::before` with `content: attr(data-widest)`), so it isn't part of the page's text. While loading it's empty. If the API takes more than half a second (for example, a cold start), a small spinner appears in it, with "Checking your access…" for screen readers, and then the role fades in, in the same box.
  - These were checked in headless Edge by measuring every state at desktop, 375px and 320px widths: identical positions and sizes. Headless Edge won't size a window below about 500px, so phone widths were measured inside iframes of exactly those widths, which have their own viewport.

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

**In the tests**, there's no way to get a real Entra token, so [`TestUsers.cs`](tests/FamilyTree.Api.Tests/TestUsers.cs) adds a test-only sign-in scheme.
- It creates the user described by a small `TestUser` record (name, username, roles, any of which can be missing), with the same `name`, `preferred_username` and `roles` claims a real token has.
- A *policy scheme* chooses for each request. Requests carrying a test user (an `X-Test-User` header) are signed in as that user. Everything else goes through the real Microsoft.Identity.Web pipeline, so the 401 tests exercise the real thing.
- Every client from one factory talks to the same in-memory API, which is what lets the rate-limiting tests check that two users have separate allowances.
- The test host runs in a `Testing` environment, not `Development`, so it never picks up your user secrets. They once made a test pass in CI and fail on this machine.

### What happens when you merge to `main`

Pull requests run [`ci.yml`](.github/workflows/ci.yml): it builds, lints and tests the API and the front end, and deploys nothing. Merging to `main` runs [`deploy.yml`](.github/workflows/deploy.yml). It reuses `ci.yml` as its first job and runs these jobs in order, stopping at the first failure:

1. **Test**: the same checks as a pull request, so nothing untested reaches Azure.
2. **Infrastructure**: deploys `infra/main.bicep`. Before that, it looks up which API image is *currently running* and passes it in. Otherwise the template's placeholder image would replace the real API on every deploy. It also checks whether the API custom domain's certificate exists yet; if not, it runs Bicep a second time to bind the new certificate. This job collects Bicep's outputs (registry name, API and web addresses) for the later jobs.
3. **Database**: applies any new migrations and makes sure the API's identity can read and write data, signed in as the SQL administrator, through a firewall opening for this runner only (see [The database](#the-database)).
4. **API**: builds the image, pushes it to the registry tagged with the commit SHA, and points the Container App at it. Container Apps then starts a new *revision* (an immutable snapshot of the app's configuration and image). Once that revision is up, Container Apps moves the traffic to it.
   - The job waits until the new revision is healthy and taking 100% of the traffic, polling for up to 5 minutes.
   - It fails if the revision doesn't get there. Otherwise a revision that fails to start would leave the *old* one quietly serving, and the smoke tests would pass against the wrong version.
   - Smoke tests then check:
     - `/health` responds, retrying to allow for a cold start.
     - `/health/ready` responds, which proves the API can reach the database as its own identity. It retries for a few minutes, in case the database is resuming.
     - `/api/v1/me` refuses an anonymous request with 401, which proves sign-in is enforced.
     - If a custom domain is set, the API returns the right CORS header for it.
5. **Web app**: builds the front end with the API's address baked in (which also generates `staticwebapp.config.json`), and uploads `web/dist` to the Static Web App. Smoke tests then check:
   - The page loads and has the expected title.
   - The security headers are being served: the CSP forbids framing and allows exactly this build's API origin, plus `nosniff` and `noindex`.
   - A deep link (`/people/123`) serves the app.

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
| Database connection (API) | Azure: `ConnectionStrings__FamilyTree` on the Container App, set by Bicep (managed identity, no password). Locally: `appsettings.Development.json` (the Docker container's throwaway password). | The API refuses to start without one. |
| API rate limits | `RateLimiting:SignedInPermitsPerMinute` (default 120) and `RateLimiting:AnonymousPermitsPerMinute` (default 30), in [`RateLimitingExtensions.cs`](src/FamilyTree.Api/RateLimiting/RateLimitingExtensions.cs) | To change them in Azure without a code change, add `RateLimiting__SignedInPermitsPerMinute` (and so on) to the Container App's environment variables in `infra/main.bicep`. |
| Which Azure account to deploy to | GitHub `production` environment variables (see above) | Identifiers, not credentials, but kept out of this public repo. |
| Custom domain for the web app | GitHub `production` variable `WEB_CUSTOM_DOMAIN` | Empty or missing means no custom domain. Set it only once the DNS CNAME exists, because Azure validates the domain during the deploy. |
| Custom domain for the API | GitHub `production` variable `API_CUSTOM_DOMAIN` | Same rules, but it needs both the CNAME and the `asuid` TXT record first. When set, the web app is built to call it. |
| API's allowed browser origins | Container App environment variables, set by Bicep | Derived from the Static Web App's address and the custom domain. Don't edit them in the portal: the next deploy overwrites them. |
| API address used by the front end | Baked into the front-end build | From Bicep's outputs during the deploy; unset locally. |
| Web app's security headers and navigation fallback | [`web/build/staticWebApp.ts`](web/build/staticWebApp.ts), written to `dist/staticwebapp.config.json` at build time | The CSP's API origin comes from `VITE_API_BASE_URL`. Edit the `.ts` file, never the generated JSON. |
| Sign-in app registration, roles, redirect URIs | [`infra/entra.bicep`](infra/entra.bicep), deployed by hand | Redeploy when the web app's addresses change. See [docs/setup-entra.md](docs/setup-entra.md). |
| API's sign-in settings in Azure | Container App environment variables `AzureAd__TenantId` and `AzureAd__ClientId`, set by Bicep | The client ID comes from the GitHub `production` variable `ENTRA_CLIENT_ID`; the tenant is the subscription's own. |
| API's sign-in settings locally | .NET user secrets (`dotnet user-secrets list --project src/FamilyTree.Api`) | Stored in your Windows profile, never in the repo. |
| Web app's sign-in settings | Build-time `VITE_ENTRA_CLIENT_ID` and `VITE_ENTRA_TENANT_ID`. In Azure they come from the GitHub variables `ENTRA_CLIENT_ID` and `AZURE_TENANT_ID`; locally, from `web/.env.local` (git-ignored; template in `web/.env.example`). | Baked into the published JavaScript, which is fine: neither is a secret. |
| Who can sign in, and with which role | Azure portal → Entra ID → Enterprise applications → Family Tree → Users and groups | Not in code: these are people. |
| .NET SDK version and test runner | [`global.json`](global.json) | Pins the exact SDK (10.0.402) that CI installs, and opts in to Microsoft.Testing.Platform (see gotchas). Dependabot proposes SDK updates. |
| Dependency updates | [`.github/dependabot.yml`](.github/dependabot.yml) | Which ecosystems, how often, how PRs are grouped. |
| npm package versions | `web/package.json` (exact versions) and `web/package-lock.json` | `web/.npmrc` has `save-exact=true`, so new packages are pinned too. |

### Keeping dependencies up to date and secure

Several layers catch vulnerable or outdated dependencies, each at a different moment:

| What | When | What it does |
|---|---|---|
| **NuGet audit** ([`Directory.Build.props`](Directory.Build.props)) | Every .NET restore and build, locally and in CI | Fails the build if any .NET package, including indirect ones, has a known moderate-or-worse vulnerability. |
| **`npm audit`** (in [`ci.yml`](.github/workflows/ci.yml)) | Every CI run | The same for the web app's npm packages. |
| **Dependency review** (in `ci.yml`) | Pull requests | Fails the PR if it *adds or upgrades* a dependency with a known vulnerability, so problems are caught in review rather than after merging. |
| **Dependabot version updates** ([`.github/dependabot.yml`](.github/dependabot.yml)) | Weekly, on Mondays | Opens PRs to update NuGet and npm packages, GitHub Actions, and the .NET SDK in `global.json`. Minor and patch updates are grouped into one PR per ecosystem; each major update gets its own PR, because it may need code changes. |
| **Dependabot security updates and alerts** | As soon as an advisory is published | Opens a PR to fix a vulnerable dependency without waiting for Monday, and lists alerts under the repository's **Security** tab. |
| **CodeQL** ([`codeql.yml`](.github/workflows/codeql.yml)) | Every PR, every push to `main`, and weekly | GitHub's code scanning. It analyses the C#, TypeScript and workflow files for security problems (injection, unsafe data flows, risky workflow patterns). Findings appear as PR annotations and under **Security → Code scanning**. It reads the source directly (`build-mode: none`), so it doesn't need to build anything. |

**Handling a Dependabot PR:** CI runs on it like any other. If it's green, review the changes (the PR links each package's release notes) and merge. A grouped PR that fails usually means one package in it needs attention. Comment `@dependabot recreate` to rebuild it, or deal with that package separately.

**One-off GitHub settings.** Version updates and the workflows above work from the files in the repo. Two things are switched on in **Settings → Advanced Security** (labelled *Code security* in older versions of GitHub):
- **Dependabot alerts**: on.
- **Dependabot security updates**: on.
- Leave **CodeQL analysis → Default setup** off. `codeql.yml` is the "advanced setup", and GitHub won't run both.

Optionally, under **Settings → Rules → Rulesets**, require pull requests and passing status checks (*API build and test*, *Web lint, test and build*, *Dependency review*, *Analyse csharp*) before merging to `main`. Then nothing reaches production without passing them, even by accident.

### Costs

About £4–5 a month, almost all of it the container registry's Basic tier.

- The Static Web App is on the Free tier.
- The Container App costs nothing at this usage, because it scales to zero and the monthly free allowance covers the rest.
- Logs fit in Log Analytics' free monthly allowance.
- The database is on Azure SQL's free offer. It pauses when idle, and pauses rather than charging if the monthly allowance is ever used up.

A budget alert on the resource group is worth having (setup guide, section 8).

### Decisions and gotchas

These are mostly lessons learned the hard way.

- **The first visit after a quiet spell is slow.** The API scales to zero, so it takes a few seconds to start, and the page shows "Checking the API…" meanwhile. Setting `minReplicas: 1` in Bicep would remove the delay, but costs a few pounds a month for an always-on replica.
- **The database is in Sweden Central, not the UK.** In October 2026 Azure refused new SQL servers for this subscription in UK South (`RegionDoesNotAllowProvisioning`), and also in UK West, North Europe, West Europe and Norway East.
  - **See what's allowed:** `az rest --method GET --uri "https://management.azure.com/subscriptions/<subscription ID>/providers/Microsoft.Sql/locations/<region>/capabilities?api-version=2023-08-01&include=supportedEditions" --query "{status:status, reason:reason}"`. `Available` means you can create servers there; `Visible` with a reason means you can't. Azure's message says exceptions need a support request.
  - **What it means:** family data is held in the EU rather than the UK. The API stays in UK South, so every database call crosses regions, which adds some delay and may add a small data-transfer charge.
  - **Moving it later** means creating a new server and migrating the data, because Azure can't change a server's region. The `sqlLocation` parameter in `infra/main.bicep` sets the region.
- **The Static Web App is in East US 2, not the UK.**
  - On this subscription, Static Web Apps is only offered in a handful of regions: Central US, East US 2, West US 2, West Europe and East Asia. West Europe wasn't accepting new customers in October 2026.
  - The region only holds the app's configuration and the built files. Pages are served from the global edge, and no family data is held there.
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
- **CI fails at "Setup .NET" with HTTP 409 downloading the SDK.** The SDK version in [`global.json`](global.json) is pinned exactly, with `"rollForward": "patch"`, rather than "latest 10.0".
  - In October 2026, `actions/setup-dotnet` was turning `"rollForward": "latestFeature"` into "install the latest 10.0". That goes through an aka.ms redirect, which pointed at a Microsoft storage account refusing public downloads (`PublicAccessNotPermitted`) while a new release was being published. CI broke without any change on our side.
  - An exact version is downloaded straight from `builds.dotnet.microsoft.com`, so it isn't affected. Avoid `latestFeature`, `latestPatch`, `latestMinor` and `latestMajor`: `setup-dotnet` turns all of those into "latest" lookups.
  - Locally, `patch` uses 10.0.402 if it's installed, otherwise the newest 10.0.4xx patch.
  - Dependabot proposes newer SDKs as PRs. After merging one, install that SDK locally too; `patch` won't move you to a new feature band (for example from 10.0.4xx to 10.0.5xx) on its own.
- **`dotnet test` on .NET 10 needs Microsoft.Testing.Platform.** .NET 10's `dotnet test` dropped the old VSTest mode for test frameworks like xUnit v3. [`global.json`](global.json) opts in to the new runner. The test project must also be an executable (`<OutputType>Exe</OutputType>`), which xUnit v3 requires.
- **Front-end tests that wait for async UI should use `waitFor`, not `findBy…` followed by a text assertion.** The status element exists immediately ("Checking the API…"), so `findByRole('status')` returns before the fetch completes. One test passed locally and failed on CI's slower runners until it was fixed.
- **A workflow step fails with only "exit code 1" and no message of its own.** GitHub runs `run:` steps with `bash -e`, which ends the script at the first command that returns non-zero, even one whose failure looks harmless.
  - We hit this with `read`. Given input without a trailing newline, `read` still sets the variables but returns 1, which killed the revision-wait step on its first check.
  - Feed `read` from a here-string (`<<< "$var"`), which supplies the newline.
  - Test step scripts locally with `bash -e script.sh`, not plain `bash`.
  - Output from `az` on Windows ends lines with `\r\n`, so scripts that parse it should strip `\r` too.
  - In Git Bash on Windows, `grep --ignore-case --fixed-strings` crashes ("Aborted"). For simple "does this text contain that" checks in workflow scripts, use bash's own `[[ "$text" == *"$part"* ]]` (with `shopt -s nocasematch` to ignore case). It needs no external tool and behaves the same everywhere.
- **Vulnerable packages fail the build.** [`Directory.Build.props`](Directory.Build.props) turns NuGet's audit warnings for moderate, high and critical vulnerabilities into errors. Every restore checks direct and *transitive* packages against GitHub's advisory database.
  - So a newly published advisory can suddenly break an unchanged build. That's intended: fix it, don't suppress it.
  - Find the culprit with `dotnet list package --vulnerable --include-transitive`, and why it's there with `dotnet nuget why <project> <package>`. Usually the fix is to pin the patched version with a direct `PackageReference` and a comment saying when to remove it.
  - Microsoft.Identity.Web 4.16.1 currently needs two such pins, `System.Security.Cryptography.Pkcs` in the API project and `System.Security.Cryptography.Xml` in the test project. Packages that are part of the ASP.NET Core shared framework, such as `.Xml`, can't be pinned in the web project (warning NU1510); they're patched by updating the runtime.
- **Can't sign in to Azure CLI: `AADSTS50076 … you must use multi-factor authentication`.** The tenant enforces MFA (Entra *security defaults*), and the CLI's session has expired. Run `az login --tenant d7ca0a01-3090-41f1-bb26-431ec7b60471` again.
- **Don't take Microsoft.Data.SqlClient 7 without its Azure package.** EF Core 10 uses SqlClient 6, which has Microsoft Entra authentication built in. In SqlClient 7 that moved into a separate package, `Microsoft.Data.SqlClient.Extensions.Azure`. If a Dependabot PR (or a future EF Core) brings SqlClient 7, add that package too. Otherwise managed-identity connections fail in Azure, though nothing fails locally or in the tests, which use passwords.
- **`az` write commands fail with `AADSTS50076` and a `--claims-challenge` suggestion, even straight after `az login`.** Azure now requires MFA for create, update and delete operations from the CLI, and a plain `az login` token may not carry proof of it. Run exactly the `az logout` and `az login … --claims-challenge …` commands the error prints. (The GitHub deploy identity is a managed identity, so this doesn't affect the pipeline.)
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
- Families and children.
- An editing UI that only Andrew can use.
- Search, profiles and tree views for viewers.
- A privacy rule: details of living people are hidden from viewers.
- Maps.

Relatives should not be invited until the privacy rule is deployed.

## Continuous integration and deployment

- `.github/workflows/ci.yml` builds, lints and tests the API and the web app on every pull request.
- `.github/workflows/deploy.yml` runs on every push to `main`: it runs the same tests, then deploys the infrastructure, the API and the web app to Azure, with a smoke test after each. See [How it works](#how-it-works).
- `.github/workflows/codeql.yml` scans the code for security problems on PRs, on pushes to `main` and weekly, and `.github/dependabot.yml` keeps dependencies up to date. See [Keeping dependencies up to date and secure](#keeping-dependencies-up-to-date-and-secure).

Before the first deployment, follow the one-off [Azure setup](docs/setup-azure.md).
