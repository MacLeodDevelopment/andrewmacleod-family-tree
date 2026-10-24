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

## Continuous integration and deployment

- `.github/workflows/ci.yml` builds, lints and tests the API and the web app on every pull request.
- `.github/workflows/deploy.yml` runs on every push to `main`: it runs the same tests, then deploys the infrastructure, the API and the web app to Azure, with a smoke test after each.

Before the first deployment, follow the one-off [Azure setup](docs/setup-azure.md).

The API's allowed browser origins (CORS) come from configuration, `Cors:AllowedOrigins`. In Azure the infrastructure sets them to the web app's address. Locally none are needed, because the Vite dev server proxies API calls.
