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
backend/FantasySentiment.Api/SeedData/Snapshots/2026-week2-reddit-sample.json
```

That fixture is built from the manually collected Week 2 Reddit samples and contains three source posts:

- `OFFICIAL WEEK 2 SUNDAY MORNING GAME THREAD`
- `OFFICIAL WEEK 2 SUNDAY MORNING POSTGAME THREAD`
- `OFFICIAL WEEK 2 SUNDAY AFTERNOON GAME THREAD`

The older synthetic demo fixture is still available at:

```text
backend/FantasySentiment.Api/SeedData/Snapshots/2026-week-4.json
```

To switch back temporarily, set `SNAPSHOT_DEFAULT_KEY=2026-week-4` in `.env` or `appsettings.Development.json`.

On startup the backend:

- Applies EF Core migrations.
- Seeds sample NFL players and aliases.
- In Development, processes the configured mock snapshot once if it is not already processed.

You can manually process or reprocess a snapshot:

```http
POST /api/snapshots/2026-week2-reddit-sample/process
POST /api/snapshots/2026-week2-reddit-sample/process?skipIfProcessed=false
```

Processing is idempotent. Reprocessing a snapshot removes that snapshot's prior imported posts, comments, mentions, and aggregates before rebuilding them from the JSON fixture.

## Score Definitions

`PlayerMentionDetector` finds player aliases in each comment. The analyzer then scores target-specific context so comments like `CMC looked great but Purdy was awful` can produce separate sentiment for each player.

The production analyzer is VADER plus fantasy-football semantic corrections. `RuleBased` remains available as a lightweight baseline/debug mode through `SENTIMENT_MODE=RuleBased`; unknown modes fall back to `Vader`.

Each `PlayerMention` stores:

- `RawSentimentScore`: base text sentiment.
- `FinalSentimentScore`: sentiment after fantasy-football adjustment.
- `Intent`: Add, Drop, Start, Sit, Buy, Sell, Hold, Injury, Hype, Panic, or General.
- `RedditScore`: displayed source comment score.
- `VoteWeight`: diminishing-return score weight.
- `WeightedSentimentContribution`: `FinalSentimentScore * VoteWeight`.

### Sentiment Score

```text
SentimentScore = average(FinalSentimentScore)
```

This answers: "What do the analyzed comments say about this player if every mention counts equally?"

Range: `-1` to `+1`.

### Community Sentiment

```text
CommunitySentiment = SUM(FinalSentimentScore * VoteWeight) / SUM(VoteWeight)
```

This answers: "What does the community appear to think after Reddit voting changes the influence of each comment?"

Range: `-1` to `+1`.

### Reddit Vote Weight

Vote weighting is intentionally logarithmic, so a Reddit score of `500` matters substantially more than `50`, but it does not count as 10x more influential. Reddit score is Reddit's displayed score value, not a literal count of downvotes.

```text
score 0 or 1: weight = 1
score > 1: weight = clamp(1 + ln(score) / 2.0, 1, 6)
score < 0: weight = clamp(1 / sqrt(1 + abs(score)), 0.10, 1)
```

Scores `0` and `1` are treated as neutral weight because Reddit comments generally begin around score `1`.

Rejected comments are not inverted. A negative comment with Reddit score `-8` remains a negative comment; it simply receives less influence in Community Sentiment.

### Voting Effect

```text
VotingEffect = CommunitySentiment - SentimentScore
```

This explains what Reddit voting did to the aggregate result. Positive values mean voting made sentiment more positive; negative values mean voting pulled it downward.

### Discussion Impact

```text
PositiveImpact = SUM(max(FinalSentimentScore * VoteWeight, 0))
NegativeImpact = SUM(min(FinalSentimentScore * VoteWeight, 0))
DiscussionImpact = PositiveImpact + ABS(NegativeImpact)
```

Discussion Impact is not a positive/negative rating. It means: "How much weighted sentiment-bearing discussion exists around this player?"

The UI emphasizes Discussion Impact percentile and rank within the selected snapshot, such as `94th percentile` and `#3 of 47 players`.

For individual comments:

```text
CommunityContribution = (FinalSentimentScore * VoteWeight) / TotalVoteWeightForPlayerInSnapshot
```

Those comment contributions sum approximately to the player's Community Sentiment.

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

- Discussion Impact
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
SNAPSHOT_DEFAULT_KEY=2026-week2-reddit-sample
SNAPSHOT_PROCESS_ON_STARTUP=true
SNAPSHOT_FIXTURE_DIRECTORY=SeedData/Snapshots
SENTIMENT_MODE=Vader
NEXT_PUBLIC_API_BASE_URL=http://localhost:5044
```

Snapshot posts can optionally include `subreddit`, `postType`, and `threadType` fields. If `subreddit` is omitted, the importer defaults to `fantasyfootball`; if present, the value is retained on `RedditPost.Subreddit` so future fantasy-community and team-subreddit analysis can be separated. Fixture timestamps in the Week 2 Reddit sample are deterministic metadata because the original Reddit timestamps were not collected.

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
dotnet build .\FantasySentiment.Api\FantasySentiment.Api.csproj
dotnet test .\FantasySentiment.Tests\FantasySentiment.Tests.csproj
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
dotnet build .\backend\FantasySentiment.Api\FantasySentiment.Api.csproj
dotnet test .\backend\FantasySentiment.Tests\FantasySentiment.Tests.csproj
cd frontend
pnpm typecheck
pnpm build
```

Then start the backend and confirm:

- `GET /api/health` returns `snapshotMode: true`.
- `GET /api/snapshots/latest` returns the processed fixture.
- `GET /api/snapshots/latest/highlights` returns positive and negative impact cards.
- Reprocessing with `POST /api/snapshots/2026-week2-reddit-sample/process?skipIfProcessed=false` does not duplicate snapshot data.
