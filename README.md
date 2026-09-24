# Fantasy Sentiment Terminal

A local MVP web app for tracking fantasy-football discussion sentiment, player buzz, and fantasy-specific intent from Reddit-style data.

The app runs fully in mock mode by default, so Reddit credentials are not required to see a populated dashboard.

## Architecture

- `backend/`: ASP.NET Core Web API on .NET 10.
- `backend/FantasySentiment.Api`: EF Core data model, PostgreSQL integration, REST endpoints, Reddit provider abstraction, VADER/fantasy-rule sentiment logic, hosted Reddit discovery/analysis workers, and snapshot aggregation.
- `backend/FantasySentiment.Tests`: focused tests for alias matching, sentiment, intent, buzz, and aggregation.
- `frontend/`: Next.js, React, TypeScript dashboard.
- `docker-compose.yml`: local PostgreSQL.

The backend intentionally avoids microservices. Reddit access is behind `IRedditDataProvider` with:

- `MockRedditDataProvider`
- `RedditApiDataProvider`

## Sentiment Pipeline

Sentiment is target-aware. `PlayerMentionDetector` finds player aliases, then `MentionContextExtractor` isolates the relevant sentence or clause around each player mention. A comment such as `CMC looked great but Purdy was awful` is scored once for CMC and once for Purdy.

The default `SENTIMENT_MODE` is `Vader`. `VaderSentimentAnalyzer` uses VADER as the lightweight general sentiment score and blends it with `FantasySentimentAnalyzer`, which reads fantasy-specific terms from `SeedData/sentiment-terms.json`. This is what lets the app treat fantasy phrases like `disgusting`, `washed`, and `league winner` correctly even when the general-purpose sentiment score disagrees or is neutral.

Each analyzed `PlayerMention` stores the pieces separately:

- `RawSentimentScore`: VADER compound score.
- `ModelSentimentScore`: VADER compound score, preserved for model/debug comparison.
- `RuleBasedSentimentScore`: fantasy-football lexicon score.
- `FantasyAdjustment`: final score minus VADER score.
- `FinalSentimentScore`: blended score used by dashboards and snapshots.
- `SentimentDiagnostics`: compact VADER/rule details for inspection.

Other analyzer modes still exist for experimentation:

- `RuleBased`: target-aware fantasy-rule scoring only.
- `Roberta`: adapter placeholder for local RoBERTa inference assets.
- `Hybrid`: RoBERTa score plus fantasy adjustment when RoBERTa is available; otherwise falls back to rules.
- `Comparison`: legacy development mode for comparing rule/model outputs.

Sentiment and intent remain separate. `FantasyIntentClassifier` classifies Add, Drop, Start, Sit, Buy, Sell, Hold, Injury, Hype, Panic, and General independently of whether the comment is positive, neutral, or negative.

## Reddit Ingestion

Reddit ingestion is split into two hosted workers:

- `RedditDiscoveryWorker` periodically discovers candidate posts and stores cheap post metadata: title, score, comment count, post type, and analysis status.
- `RedditAnalysisWorker` fetches comments only for posts marked `ReadyForAnalysis`, then analyzes new or changed comments in small batches.

`RedditPostAnalysisDecider` controls when a post becomes ready:

- Normal discussion posts wait for enough comments, or a lower comment count plus enough post score.
- Important news posts use lower comment thresholds when titles include injury/trade/depth-chart style keywords.
- Game threads wait for minimum age, minimum comments, and a cooldown window so the app does not analyze huge active threads while comments are still flying in.
- Already-analyzed posts become ready again only when the incremental new-comment threshold is reached.

For each analyzed comment, the stored context is bounded and includes thread title, parent comment when available, full current comment, target-specific context, and target player. Unchanged comments are not rescored on later runs; score-only updates refresh Reddit score, vote weight, and weighted contribution.

## Reddit Vote Weighting

Each `PlayerMention` stores:

- `RawSentimentScore`: base text sentiment before community weighting.
- `FinalSentimentScore`: sentiment after the configured analyzer/fantasy adjustment.
- `RedditScore`: latest comment score from Reddit/mock Reddit.
- `VoteWeight`: bounded visibility/agreement weight.
- `WeightedSentimentContribution`: `FinalSentimentScore * VoteWeight`.

Vote weighting is logarithmic and capped:

```text
score = 0: weight = 1
score > 0: weight = clamp(1 + ln(1 + score) / 2.5, 1, 4)
score < 0: weight = clamp(1 / (1 + ln(1 + abs(score)) / 2.5), 0.25, 1)
```

Negative Reddit scores reduce a comment's influence; they do not reverse sentiment.

Player snapshots now keep both unweighted sentiment and community-weighted sentiment:

```text
CommunitySentiment = SUM(FinalSentimentScore * VoteWeight) / SUM(VoteWeight)
```

Snapshots also expose total Reddit score, total/average vote weight, sample-strength score, and Low/Medium/High confidence.

## Dashboard Highlights

The top dashboard summary is served by `GET /api/highlights?window=24h`. It selects the biggest riser and faller by sentiment change for the selected window, not by absolute sentiment. Players must meet the configured sample threshold before they can appear as highlights, so one extremely positive or negative comment does not automatically take over the page.

Each highlight includes the most influential comment in the direction of the move. The displayed `Impact` value is the comment's weighted contribution to the player's community sentiment:

```text
WeightedContribution = (FinalSentimentScore * VoteWeight) / TotalVoteWeightForPlayerAndWindow
```

For a riser, the API chooses the largest positive contribution. For a faller, it chooses the largest negative contribution. The math uses the same diminishing-return vote weight described above, while the UI still displays the original Reddit score.

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
REDDIT_FETCH_LOOKBACK_MINUTES=240
SENTIMENT_MODE=Vader
SENTIMENT_ROBERTA_ENABLED=false
SENTIMENT_ROBERTA_ONNX_MODEL_PATH=
SENTIMENT_ROBERTA_TOKENIZER_PATH=
REDDIT_ANALYSIS_DISCOVERY_INTERVAL_MINUTES=10
REDDIT_ANALYSIS_ANALYSIS_INTERVAL_MINUTES=2
REDDIT_ANALYSIS_NORMAL_MINIMUM_COMMENTS=100
REDDIT_ANALYSIS_NORMAL_MINIMUM_COMMENTS_WITH_SCORE=40
REDDIT_ANALYSIS_NORMAL_MINIMUM_POST_SCORE=50
REDDIT_ANALYSIS_IMPORTANT_NEWS_MINIMUM_COMMENTS=30
REDDIT_ANALYSIS_GAME_MINIMUM_COMMENTS=300
REDDIT_ANALYSIS_GAME_MINIMUM_AGE_MINUTES=120
REDDIT_ANALYSIS_GAME_COOLDOWN_WINDOW_MINUTES=15
REDDIT_ANALYSIS_GAME_MAX_NEW_COMMENTS_DURING_COOLDOWN=20
DASHBOARD_HIGHLIGHT_MINIMUM_MENTIONS=5
DASHBOARD_HIGHLIGHT_MINIMUM_SAMPLE_STRENGTH=0.25
DASHBOARD_HIGHLIGHT_EXCLUDE_LOW_CONFIDENCE=true
DASHBOARD_HIGHLIGHT_MINIMUM_ABSOLUTE_SENTIMENT_CHANGE=0.05
DASHBOARD_HIGHLIGHT_COMMENT_EXCERPT_LENGTH=190
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
- Scores VADER/fantasy sentiment and fantasy intent.
- Generates historical snapshots so charts, risers, fallers, trends, and buzz work immediately.

The mock discovery worker then cycles through deterministic post scenarios. The analysis worker fetches comments only after those posts cross their configured thresholds, including important-news fast paths, normal discussion thresholds, incremental re-analysis, and game-thread cooldown behavior.

## Reddit Credentials

To use the real Reddit provider, set:

```env
USE_MOCK_REDDIT=false
REDDIT_CLIENT_ID=your-client-id
REDDIT_CLIENT_SECRET=replace-with-reddit-client-secret
REDDIT_USER_AGENT=FantasySentimentTerminal/0.1 by your-reddit-name
REDDIT_FETCH_LOOKBACK_MINUTES=240
SENTIMENT_MODE=Vader
SENTIMENT_ROBERTA_ENABLED=false
SENTIMENT_ROBERTA_ONNX_MODEL_PATH=
SENTIMENT_ROBERTA_TOKENIZER_PATH=
```

The app does not scrape Reddit HTML. It uses API-style JSON requests through `RedditApiDataProvider`.

## Database Migrations

The initial migration is included under:

```text
backend/FantasySentiment.Api/Data/Migrations
```

The `SentimentPipelineImprovements` migration adds the raw/final/model sentiment fields, vote-weight fields, community sentiment snapshot fields, and confidence/sample-strength fields. It backfills old mentions from existing `SentimentScore` and existing `RedditComment.Score` values so existing data is preserved.

The `RedditPostAnalysisState` migration adds post score, discovery/analysis timestamps, post type, analysis status, and analysis-status reason fields used by the split discovery/analysis workers.

Useful commands:

```powershell
cd backend
dotnet ef migrations add MigrationName --project .\FantasySentiment.Api\FantasySentiment.Api.csproj --startup-project .\FantasySentiment.Api\FantasySentiment.Api.csproj --output-dir Data\Migrations
dotnet ef database update --project .\FantasySentiment.Api\FantasySentiment.Api.csproj --startup-project .\FantasySentiment.Api\FantasySentiment.Api.csproj
```

The API also applies migrations automatically on startup.

## API Endpoints

- `GET /api/health`
- `GET /api/debug/reddit-posts` development only
- `GET /api/players`
- `GET /api/players?search=CMC`
- `GET /api/players/{id}`
- `GET /api/players/{id}/sentiment`
- `GET /api/players/{id}/history?window=24h`
- `GET /api/trending?window=6h`
- `GET /api/highlights?window=24h`
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
- Mock-mode API startup, EF migration application, seeding, ingestion, `GET /api/health`, `GET /api/trending?limit=1`, and `GET /api/highlights?window=24h`
