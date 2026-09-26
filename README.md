# Fantasy Sentiment Terminal

A local MVP web app for analyzing fantasy-football Reddit discussion as a completed weekly snapshot.

This branch intentionally runs from deterministic, editable JSON fixture data instead of live Reddit polling. That keeps the product focused on one clear workflow:

1. Load a game-week discussion fixture.
2. Batch-analyze player mentions, sentiment, intent, and vote-weighted impact.
3. Serve snapshot-level dashboard APIs to the Next.js frontend.

## Architecture

- `backend/`: ASP.NET Core Web API on .NET 10.
- `backend/FantasySentiment.Api`: EF Core data model, PostgreSQL integration, snapshot processing, sentiment/intent logic, and REST endpoints.
- `backend/FantasySentiment.Tests`: tests for sentiment logic, snapshot parsing, idempotent processing, aggregation, impact, and eligibility.
- `frontend/`: Next.js, React, TypeScript dashboard.
- `docker-compose.yml`: local PostgreSQL.

The old rolling Reddit discovery/analysis workers are registered in code for reference, but they are not started by the app on this branch.

## Snapshot Mode

The default fixture is:

```text
backend/FantasySentiment.Api/SeedData/Snapshots/2026-week-4.json
```

On startup the backend:

- Applies EF Core migrations.
- Seeds sample NFL players and aliases.
- In Development, processes the configured mock snapshot once if it is not already processed.

You can manually process or reprocess a snapshot:

```http
POST /api/snapshots/2026-week-4/process
POST /api/snapshots/2026-week-4/process?skipIfProcessed=false
```

Processing is idempotent. Reprocessing a snapshot removes that snapshot's prior imported posts, comments, mentions, and aggregates before rebuilding them from the JSON fixture.

## Sentiment And Impact

`PlayerMentionDetector` finds player aliases in each comment. The analyzer then scores target-specific context so comments like `CMC looked great but Purdy was awful` can produce separate sentiment for each player.

The production analyzer is VADER plus fantasy-football semantic corrections. `RuleBased` remains available as a lightweight baseline/debug mode through `SENTIMENT_MODE=RuleBased`; unknown modes fall back to `Vader`.

Each `PlayerMention` stores:

- `RawSentimentScore`: base text sentiment.
- `FinalSentimentScore`: sentiment after fantasy-football adjustment.
- `Intent`: Add, Drop, Start, Sit, Buy, Sell, Hold, Injury, Hype, Panic, or General.
- `RedditScore`: displayed source comment score.
- `VoteWeight`: diminishing-return score weight.
- `WeightedSentimentContribution`: `FinalSentimentScore * VoteWeight`.

Vote weighting is intentionally logarithmic, so 500 upvotes matters more than 50, but not 10x more:

```text
score = 0: weight = 1
score > 0: weight = clamp(1 + ln(1 + score) / 2.5, 1, 4)
score < 0: weight = clamp(1 / (1 + ln(1 + abs(score)) / 2.5), 0.25, 1)
```

Snapshot community sentiment is:

```text
CommunitySentiment = SUM(FinalSentimentScore * VoteWeight) / SUM(VoteWeight)
```

An influential comment's displayed impact is:

```text
Impact = (FinalSentimentScore * VoteWeight) / TotalVoteWeightForPlayerInSnapshot
```

Those comment impacts sum approximately to the player's displayed community sentiment.

## Dashboard

The homepage shows one selected snapshot rather than rolling `1h / 6h / 24h / 7d` windows.

The top cards are:

- `Top Positive Impact`: eligible player with the highest positive snapshot impact.
- `Top Negative Impact`: eligible player with the strongest negative snapshot impact.

The highlight cards include the most influential comment in the matching direction, Reddit score, comment sentiment, contribution/impact, and source thread title.

Players need enough sample strength before they can become highlights. The defaults are configurable:

```env
DASHBOARD_HIGHLIGHT_MINIMUM_MENTIONS=5
DASHBOARD_HIGHLIGHT_MINIMUM_SAMPLE_STRENGTH=0.25
DASHBOARD_HIGHLIGHT_EXCLUDE_LOW_CONFIDENCE=true
DASHBOARD_HIGHLIGHT_COMMENT_EXCERPT_LENGTH=190
```

The tables below the cards are still useful, but interpreted within the selected snapshot:

- Snapshot Impact
- Waiver / Hype Buzz
- Panic / Drop Buzz
- Most Discussed
- Team Sentiment

`DiscussionShare` replaces the old idea of buzz velocity. It is the player's share of all analyzed player mentions in the snapshot.

## Required Software

- .NET SDK 10
- Docker Desktop
- Node.js
- pnpm

## Quick Start

From the repo root:

```powershell
cd fantasy-sentiment
Copy-Item .env.example .env
docker compose up -d postgres
```

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

If `pnpm` is not available in your PowerShell PATH but dependencies have already been installed, use:

```powershell
cd frontend
.\dev.ps1
```

Open:

- Frontend: `http://localhost:3000`
- API health: `http://localhost:5044/api/health`

## Configuration

`.env.example` contains local-only placeholder values. Keep `.env` uncommitted.

Important snapshot settings:

```env
SNAPSHOT_DEFAULT_KEY=2026-week-4
SNAPSHOT_PROCESS_ON_STARTUP=true
SNAPSHOT_FIXTURE_DIRECTORY=SeedData/Snapshots
SENTIMENT_MODE=Vader
NEXT_PUBLIC_API_BASE_URL=http://localhost:5044
```

Snapshot posts can optionally include a `subreddit` field. If omitted, the importer defaults to `fantasyfootball`; if present, the value is retained on `RedditPost.Subreddit` so future fantasy-community and team-subreddit analysis can be separated.

The backend reads environment variables and also loads `.env` from the repo root for local development.

The database maps container port `5432` to host port `55432` by default to avoid colliding with an existing local PostgreSQL server.

## Database Migrations

Migrations live under:

```text
backend/FantasySentiment.Api/Data/Migrations
```

The API applies migrations automatically on startup.

Useful commands:

```powershell
cd backend
dotnet build .\FantasySentiment.slnx
dotnet test .\FantasySentiment.slnx
```

If `dotnet-ef` is installed:

```powershell
dotnet ef database update --project .\FantasySentiment.Api\FantasySentiment.Api.csproj --startup-project .\FantasySentiment.Api\FantasySentiment.Api.csproj
```

## Snapshot API

- `GET /api/snapshots`
- `GET /api/snapshots/latest`
- `GET /api/snapshots/{key}`
- `POST /api/snapshots/{key}/process`
- `GET /api/snapshots/{key}/highlights`
- `GET /api/snapshots/{key}/players?sort=impact&limit=10`
- `GET /api/snapshots/{key}/players/{id}`
- `GET /api/snapshots/{key}/teams`
- `GET /api/snapshots/{key}/teams/{team}`

The legacy rolling endpoints still exist for compatibility, but the dashboard uses the snapshot API.

## Frontend Commands

```powershell
cd frontend
pnpm typecheck
pnpm build
```

## Verification Checklist

Before committing this branch, run:

```powershell
dotnet build .\backend\FantasySentiment.slnx
dotnet test .\backend\FantasySentiment.slnx
cd frontend
pnpm typecheck
pnpm build
```

Then start the backend and confirm:

- `GET /api/health` returns `snapshotMode: true`.
- `GET /api/snapshots/latest` returns the processed fixture.
- `GET /api/snapshots/latest/highlights` returns positive and negative impact cards.
- Reprocessing with `POST /api/snapshots/2026-week-4/process?skipIfProcessed=false` does not duplicate snapshot data.
