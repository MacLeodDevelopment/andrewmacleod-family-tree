# andrewmacleod-family-tree

A private, invite-only family tree for the MacLeod family, served at `family.andrewmacleod.com`.

- `src/FamilyTree.Api` — ASP.NET Core (.NET 10) minimal API.
- `web/` — React + TypeScript single-page app built with Vite.
- `tests/` — behaviour-focused API tests.

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

## Test

```sh
dotnet test

cd web
npm test
```

Tests describe behaviour (HTTP requests and responses, what a user sees), not implementation details.

## Continuous integration

`.github/workflows/ci.yml` builds and tests the API and the web app on every pull request and on pushes to `main`.
