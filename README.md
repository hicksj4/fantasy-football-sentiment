# Fantasy Sentiment Terminal

A local MVP web app for tracking fantasy-football discussion sentiment, player buzz, and fantasy-specific intent from Reddit-style data.

The app runs fully in mock mode by default, so Reddit credentials are not required to see a populated dashboard.

## Architecture

- `backend/`: ASP.NET Core Web API on .NET 10.
- `backend/FantasySentiment.Api`: EF Core data model, PostgreSQL integration, REST endpoints, Reddit provider abstraction, rule-based sentiment/intent logic, hosted ingestion and snapshot workers.
- `backend/FantasySentiment.Tests`: focused tests for alias matching, sentiment, intent, buzz, and aggregation.
- `frontend/`: Next.js, React, TypeScript dashboard.
- `docker-compose.yml`: local PostgreSQL.

The backend intentionally avoids microservices. Reddit access is behind `IRedditDataProvider` with:

- `MockRedditDataProvider`
- `RedditApiDataProvider`

## Required Software

- .NET SDK 10
- Docker Desktop
- Node.js
- pnpm

This machine had `pnpm` available but not `npm`, so the documented frontend commands use `pnpm`.

## Quick Start

From the repo root:

```powershell
cd fantasy-sentiment
Copy-Item .env.example .env
docker compose up -d postgres
```

Keep `.env` local and uncommitted. You can leave the placeholder password for local-only development or replace it in both `FANTASY_POSTGRES_PASSWORD` and `DATABASE_URL`.

Run the backend:

```powershell
cd backend
dotnet restore .\FantasySentiment.slnx
dotnet run --project .\FantasySentiment.Api\FantasySentiment.Api.csproj
```

Run the frontend in another terminal:

```powershell
cd frontend
pnpm install
pnpm dev
```

If `pnpm` is not available in your PowerShell PATH but dependencies have already been installed, use the local wrapper:

```powershell
cd frontend
.\dev.ps1
```

That runs Next directly through `node_modules`.
If PowerShell script execution is restricted, use the underlying command directly:

```powershell
node .\node_modules\next\dist\bin\next dev
```

Open:

- Frontend: `http://localhost:3000`
- API health: `http://localhost:5044/api/health`

## Configuration

`.env.example` includes the local defaults:

```env
USE_MOCK_REDDIT=true
FANTASY_POSTGRES_PORT=55432
FANTASY_POSTGRES_DB=fantasy_sentiment
FANTASY_POSTGRES_USER=fantasy
FANTASY_POSTGRES_PASSWORD=replace-with-local-password
DATABASE_URL=Host=localhost;Port=55432;Database=fantasy_sentiment;Username=fantasy;Password=replace-with-local-password
NEXT_PUBLIC_API_BASE_URL=http://localhost:5044
```

The backend reads environment variables and also loads `.env` from the repo root for local development.
The database maps container port `5432` to host port `55432` by default to avoid colliding with an existing local PostgreSQL server.

If you previously started the database with the older `5432` mapping and see `password authentication failed for user "fantasy"`, recreate the Compose container with the current config:

```powershell
docker compose down
docker compose up -d postgres
```

This repo now uses a dedicated Docker volume named `fantasy-sentiment-postgres-data`; the old Compose-managed `postgres-data` volume is left alone.

## Mock Mode

`USE_MOCK_REDDIT=true` is the default.

On first backend startup, the app:

- Applies EF Core migrations.
- Seeds sample NFL players and aliases.
- Seeds realistic historical Reddit-style comments.
- Detects player mentions.
- Scores sentiment and fantasy intent.
- Generates historical snapshots so charts, risers, fallers, trends, and buzz work immediately.

The mock ingestion worker continues adding new comments every configured interval.

## Reddit Credentials

To use the real Reddit provider, set:

```env
USE_MOCK_REDDIT=false
REDDIT_CLIENT_ID=your-client-id
REDDIT_CLIENT_SECRET=replace-with-reddit-client-secret
REDDIT_USER_AGENT=FantasySentimentTerminal/0.1 by your-reddit-name
```

The app does not scrape Reddit HTML. It uses API-style JSON requests through `RedditApiDataProvider`.

## Database Migrations

The initial migration is included under:

```text
backend/FantasySentiment.Api/Data/Migrations
```

Useful commands:

```powershell
cd backend
dotnet ef migrations add MigrationName --project .\FantasySentiment.Api\FantasySentiment.Api.csproj --startup-project .\FantasySentiment.Api\FantasySentiment.Api.csproj --output-dir Data\Migrations
dotnet ef database update --project .\FantasySentiment.Api\FantasySentiment.Api.csproj --startup-project .\FantasySentiment.Api\FantasySentiment.Api.csproj
```

The API also applies migrations automatically on startup.

## API Endpoints

- `GET /api/health`
- `GET /api/players`
- `GET /api/players?search=CMC`
- `GET /api/players/{id}`
- `GET /api/players/{id}/sentiment`
- `GET /api/players/{id}/history?window=24h`
- `GET /api/trending?window=6h`
- `GET /api/sentiment/risers`
- `GET /api/sentiment/fallers`
- `GET /api/buzz`
- `GET /api/waivers`
- `GET /api/panic`
- `GET /api/most-discussed`
- `GET /api/teams`
- `GET /api/teams/{team}`

Supported dashboard windows: `1h`, `6h`, `24h`, `7d`.

## Developer Commands

Backend:

```powershell
cd backend
dotnet build .\FantasySentiment.slnx
dotnet test .\FantasySentiment.slnx
```

Frontend:

```powershell
cd frontend
pnpm typecheck
pnpm build
```

## Verification Done

- `dotnet build .\FantasySentiment.slnx`
- `dotnet test .\FantasySentiment.slnx`
- `pnpm typecheck`
- `pnpm build`
