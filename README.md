# Fantasy Sentiment Terminal

Fantasy Sentiment Terminal analyzes Reddit fantasy-football discussion to show which players are drawing attention, how fans feel about them, and how Reddit voting shapes the conversation.

Explore discussion snapshots through a dashboard of player rankings, positive and negative highlights, waiver interest, and team sentiment. Open a player to inspect the comments behind their scores, including the context used to identify the player and interpret the comment.

The app runs locally with an ASP.NET Core API, a Next.js frontend, and PostgreSQL. Bundled JSON datasets let you analyze collected Reddit discussions without Reddit credentials.

## Features

- **Discussion rankings:** compare players by discussion impact, mention count, waiver interest, and drop, sit, or sell activity.
- **Sentiment highlights:** see the players with the strongest positive and negative impact, alongside influential comments and their source threads.
- **Player analysis:** inspect sentiment, voting effects, fantasy actions, sample confidence, and the comments contributing to each result.
- **Context-aware attribution:** resolve player aliases and use reply chains and thread titles to interpret comments that refer to a player indirectly.
- **Sentiment coverage:** distinguish scored opinions from mentions that lack enough context or confidence for a sentiment score.
- **Team summaries:** explore team-level discussion and follow links to individual players.

## Quick Start

Install the .NET 10 SDK, Docker Desktop, Node.js, and pnpm. Start Docker Desktop before running the database.

From the repository root, create the local configuration and start PostgreSQL:

```powershell
Copy-Item .env.example .env
Copy-Item frontend/.env.example frontend/.env.local
docker compose up -d postgres
```

Run the backend:

```powershell
cd backend
dotnet restore .\FantasySentiment.slnx
dotnet run --project .\FantasySentiment.Api\FantasySentiment.Api.csproj
```

On startup, the API applies database migrations, seeds NFL players and aliases, and processes the configured dataset if it has not already been processed.

In a second terminal, start the frontend from the repository root:

```powershell
cd frontend
pnpm install
pnpm dev
```

Open the dashboard at [localhost:3000](http://localhost:3000). The API health endpoint is [localhost:5044/api/health](http://localhost:5044/api/health).

If frontend dependencies are installed but `pnpm` is unavailable in PowerShell, run `.\dev.ps1` from `frontend/`.

## Data and Processing

Datasets live in [`backend/FantasySentiment.Api/SeedData/Snapshots/`](backend/FantasySentiment.Api/SeedData/Snapshots). Each JSON file contains snapshot metadata, source posts, comments, reply relationships, and Reddit scores.

| Dataset key | Contents |
| --- | --- |
| `2026-week2-reddit-sample` | Manually collected comments from three Week 2 game and postgame threads. Selected by `.env.example`. |
| `2026-combined-real-handpicked` | Collected Reddit samples spanning Weeks 1-3. Selected by `appsettings.Development.json` when no environment override is set. |
| `2026-week-4` | Synthetic discussion data for exploring the app. |

To use the combined dataset, set this value in the root `.env` file and restart the backend:

```env
SNAPSHOT_DEFAULT_KEY=2026-combined-real-handpicked
```

The processor imports the discussion, resolves player references, analyzes sentiment and fantasy actions, and builds player aggregates. Mentions with insufficient attribution or semantic confidence count toward discussion volume but are excluded from sentiment and impact calculations.

To process a dataset manually:

```http
POST /api/snapshots/2026-combined-real-handpicked/process
```

Processing replaces that snapshot's imported data and aggregates, so reprocessing does not duplicate its results. Use `?skipIfProcessed=true` to retain an already completed snapshot. The startup processor uses this option automatically.

The dashboard uses the configured dataset once it has been processed, falling back to the latest completed snapshot if needed. To view a specific one, use a URL such as `http://localhost:3000/?snapshot=2026-combined-real-handpicked`.

These datasets are collected samples of Reddit discussion. The combined dataset spans multiple weeks; its results describe the included comments. Timestamps in the Week 2 sample are fixed metadata because original comment timestamps were not collected.

## Understanding the Scores

The analyzer combines VADER sentiment with fantasy-football language rules and player-specific context. A comment such as "CMC looked great but Purdy was awful" can produce separate sentiment for each player. Fantasy actions such as Add, Drop, Start, Sit, Buy, Sell, and Hold are tracked alongside discussion signals such as injury concerns.

The formulas below use scored player mentions. Mentions without enough evidence for sentiment remain visible as mention-only records. Sentiment coverage reports the share of a player's mentions that received a score, and sample confidence helps indicate how much evidence supports the result. A player with no scored mentions is shown as having insufficient sentiment evidence.

### Sentiment and Community Sentiment

**Sentiment Score** gives every scored mention equal influence:

```text
SentimentScore = average(FinalSentimentScore)
```

**Community Sentiment** weights those opinions by the displayed Reddit score of each comment:

```text
CommunitySentiment = sum(FinalSentimentScore * VoteWeight) / sum(VoteWeight)
```

Both scores range from `-1` to `+1`. Negative values indicate negative sentiment, positive values indicate positive sentiment, and values near zero indicate neutral or mixed discussion.

### Reddit Vote Weight

Positive Reddit scores increase influence logarithmically, with diminishing returns. Negative Reddit scores reduce a comment's influence without reversing its sentiment. The default weights are:

```text
score 0 or 1: weight = 1
score > 1:   weight = clamp(1 + ln(score) / 2.0, 1, 6)
score < 0:   weight = clamp(1 / sqrt(1 + abs(score)), 0.10, 1)
```

The source value is Reddit's displayed comment score. It does not represent a separate count of upvotes or downvotes.

**Voting Effect** shows how weighting changes the result:

```text
VotingEffect = CommunitySentiment - SentimentScore
```

A positive value means voting shifts sentiment upward; a negative value means it shifts sentiment downward.

### Discussion Impact and Share

**Discussion Impact** measures the amount of sentiment-bearing discussion around a player. It combines the magnitude of positive and negative contributions:

```text
PositiveImpact = sum(max(FinalSentimentScore * VoteWeight, 0))
NegativeImpact = sum(min(FinalSentimentScore * VoteWeight, 0))
DiscussionImpact = PositiveImpact + abs(NegativeImpact)
```

A high impact score can come from praise, criticism, or both. The dashboard shows each player's impact rank and percentile within the selected snapshot.

**Discussion Share** is the player's share of all detected player mentions, including mention-only records:

```text
DiscussionShare = PlayerMentionCount / TotalPlayerMentionsInSnapshot
```

For an individual scored comment, its contribution to the player's Community Sentiment is:

```text
CommunityContribution = (FinalSentimentScore * VoteWeight) / TotalScoredVoteWeightForPlayer
```

These contributions sum to the player's Community Sentiment, subject to rounding.

## Configuration

The backend loads `.env` from the repository root. Environment overrides take precedence over application JSON settings. The frontend reads `frontend/.env.local`.

The root `.env.example` includes these settings:

```env
SNAPSHOT_DEFAULT_KEY=2026-week2-reddit-sample
SNAPSHOT_PROCESS_ON_STARTUP=true
SNAPSHOT_FIXTURE_DIRECTORY=SeedData/Snapshots
SENTIMENT_MODE=Vader
DASHBOARD_HIGHLIGHT_MINIMUM_MENTIONS=5
DASHBOARD_HIGHLIGHT_MINIMUM_SAMPLE_STRENGTH=0.25
DASHBOARD_HIGHLIGHT_EXCLUDE_LOW_CONFIDENCE=true
DASHBOARD_HIGHLIGHT_COMMENT_EXCERPT_LENGTH=190
```

Highlight thresholds determine which players have enough evidence to appear in the positive and negative cards. `SENTIMENT_MODE=RuleBased` selects the lightweight rule-based analyzer; unrecognized values use VADER.

Set the frontend API address in `frontend/.env.local`:

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:5044
```

PostgreSQL uses host port `55432` and container port `5432`. If you change database credentials or ports, keep `DATABASE_URL` and the `FANTASY_POSTGRES_*` settings consistent. Keep local `.env` files uncommitted.

## Project Structure

| Path | Purpose |
| --- | --- |
| `backend/FantasySentiment.Api/` | .NET 10 API, EF Core models and migrations, discussion processing, and analytics. |
| `backend/FantasySentiment.Api/SeedData/` | Player data, language rules, and discussion datasets. |
| `backend/FantasySentiment.Tests/` | Tests for attribution, sentiment, processing, aggregation, and benchmark samples. |
| `frontend/` | Next.js, React, and TypeScript dashboard. |
| `docker-compose.yml` | Local PostgreSQL service and persistent data volume. |
| `scripts/clean.ps1` | Development cache and build-output cleanup. |

## API

Use a dataset key or `latest` for `{key}` in snapshot read endpoints.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/api/health` | API status and active configuration. |
| GET | `/api/snapshots` | List available snapshots. |
| GET | `/api/snapshots/{key}` | Snapshot metadata and processing totals. |
| POST | `/api/snapshots/{key}/process` | Import and analyze a dataset by its key. |
| GET | `/api/snapshots/{key}/highlights` | Positive and negative player highlights. |
| GET | `/api/snapshots/{key}/players?sort=impact&limit=10` | Ranked player summaries. |
| GET | `/api/snapshots/{key}/players/{id}` | Player detail and score explanations. |
| GET | `/api/snapshots/{key}/players/{id}/mentions` | Paginated player mentions. |
| GET | `/api/snapshots/{key}/teams` | Team summaries. |
| GET | `/api/snapshots/{key}/teams/{team}` | Team detail. |
| GET | `/api/search?q={name}` | Player search. |

Player rankings support `impact`, `positive`, `negative`, `waivers`, `panic`, and `most-discussed` sorting. The mentions endpoint accepts `disposition`, `sort`, `page`, and `pageSize` query parameters.

## Development

Run backend checks from the repository root:

```powershell
dotnet build .\backend\FantasySentiment.Api\FantasySentiment.Api.csproj
dotnet test .\backend\FantasySentiment.Tests\FantasySentiment.Tests.csproj
```

Run frontend checks:

```powershell
cd frontend
pnpm typecheck
pnpm build
```

EF Core migrations live in `backend/FantasySentiment.Api/Data/Migrations/` and are applied automatically on API startup. With `dotnet-ef` installed, you can also apply them from the repository root:

```powershell
dotnet ef database update --project .\backend\FantasySentiment.Api\FantasySentiment.Api.csproj --startup-project .\backend\FantasySentiment.Api\FantasySentiment.Api.csproj
```

To remove generated build output and caches, run:

```powershell
.\scripts\clean.ps1
```

The cleanup preserves source files, datasets, migrations, local configuration, PostgreSQL data, and canonical benchmark reports. Add `-Full` to also remove frontend dependencies, then run `pnpm install` before starting the frontend again.
