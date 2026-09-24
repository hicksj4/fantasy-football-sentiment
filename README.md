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

## Sentiment Pipeline

Sentiment is target-aware. `PlayerMentionDetector` finds player aliases, then `MentionContextExtractor` sends the relevant sentence or clause around each player mention into the configured sentiment analyzer. A comment such as `CMC looked great but Purdy was awful` is scored once for CMC and once for Purdy.

The original `FantasySentimentAnalyzer` is still present and still reads `SeedData/sentiment-terms.json`. It acts as the rule-based fantasy-football language layer used by these analyzer modes:

- `RuleBased`: target-aware rule scoring only.
- `Roberta`: local ONNX Runtime inference with CardiffNLP target sentiment.
- `Hybrid`: RoBERTa base score plus a bounded fantasy adjustment when RoBERTa is available; otherwise falls back to the rule score.
- `Comparison`: default development mode. Stores rule score, model score when available, adjustment, diagnostics, and final score on each `PlayerMention`.

The RoBERTa implementation uses `cardiffnlp/twitter-roberta-base-topic-sentiment-latest`. It is target-aware and receives input shaped like:

```text
<context> </s> <target>
```

The backend tokenizes that string with `Tokenizers.HuggingFace`, runs `model.onnx` with `Microsoft.ML.OnnxRuntime`, applies a stable softmax over the five logits, and maps probabilities to a normalized score with these label weights:

```text
strongly negative = -1.0
negative = -0.5
negative or neutral = 0.0
positive = 0.5
strongly positive = 1.0
```

The final model score is the probability-weighted expected value clamped to `[-1, 1]`. Diagnostics include the winning label, winning probability, all five label probabilities, sequence length, and model input string.

The API health endpoint reports the active sentiment mode, whether RoBERTa is enabled/loaded, which assets were found, max sequence length, and ONNX input/output names. `RuleBased` mode works without model assets. `Roberta` mode fails startup clearly when enabled assets are missing.

Sentiment and intent remain separate. `FantasyIntentClassifier` still classifies Add, Drop, Start, Sit, Buy, Sell, Hold, Injury, Hype, Panic, and General independently of sentiment.

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

## Required Software

- .NET SDK 10
- Docker Desktop
- Node.js
- pnpm
- Python 3.12+ if you need to export or validate RoBERTa assets

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
SENTIMENT_MODE=Comparison
SENTIMENT_ROBERTA_ENABLED=false
SENTIMENT_ROBERTA_MODEL_PATH=Models/Sentiment/model.onnx
SENTIMENT_ROBERTA_ONNX_MODEL_PATH=
SENTIMENT_ROBERTA_TOKENIZER_PATH=Models/Sentiment/tokenizer.json
SENTIMENT_ROBERTA_CONFIG_PATH=Models/Sentiment/config.json
SENTIMENT_ROBERTA_TOKENIZER_CONFIG_PATH=Models/Sentiment/tokenizer_config.json
SENTIMENT_ROBERTA_MAX_SEQUENCE_LENGTH=512
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

## RoBERTa Assets

The repository does not commit generated model assets. They are written to:

```text
backend/FantasySentiment.Api/Models/Sentiment/
```

Expected generated files include:

- `model.onnx`
- `tokenizer.json`
- `config.json`
- `tokenizer_config.json`
- tokenizer sidecar files such as `vocab.json` and `merges.txt`

Export the model from the repository root:

```powershell
.\tools\sentiment-model\export-model.ps1 -Python "C:\Path\To\python.exe"
```

The exporter installs its Python dependencies into `tools/sentiment-model/.venv`, uses Hugging Face Optimum to export ONNX, and stores Hugging Face cache files under `tools/sentiment-model/.cache`. Both folders are ignored by Git.

Reference and parity tools:

```powershell
.\tools\sentiment-model\.venv\Scripts\python.exe .\tools\sentiment-model\reference_inference.py --output .\tools\sentiment-model\python-reference.json
dotnet run --project .\tools\sentiment-model\dotnet-parity\FantasySentiment.RobertaParity.csproj -- --repeat 100
.\tools\sentiment-model\.venv\Scripts\python.exe .\tools\sentiment-model\compare_runtime_parity.py
```

The comparison checks exact `input_ids` and `attention_mask` parity, then compares Python Transformers probabilities to .NET ONNX probabilities with a default `0.01` absolute tolerance.

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
SENTIMENT_MODE=Comparison
SENTIMENT_ROBERTA_ENABLED=false
SENTIMENT_ROBERTA_MODEL_PATH=Models/Sentiment/model.onnx
SENTIMENT_ROBERTA_TOKENIZER_PATH=Models/Sentiment/tokenizer.json
SENTIMENT_ROBERTA_CONFIG_PATH=Models/Sentiment/config.json
SENTIMENT_ROBERTA_TOKENIZER_CONFIG_PATH=Models/Sentiment/tokenizer_config.json
SENTIMENT_ROBERTA_MAX_SEQUENCE_LENGTH=512
```

The app does not scrape Reddit HTML. It uses API-style JSON requests through `RedditApiDataProvider`.

## Database Migrations

The initial migration is included under:

```text
backend/FantasySentiment.Api/Data/Migrations
```

The `SentimentPipelineImprovements` migration adds the raw/final/model sentiment fields, vote-weight fields, community sentiment snapshot fields, and confidence/sample-strength fields. It backfills old mentions from existing `SentimentScore` and existing `RedditComment.Score` values so existing data is preserved.

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

- Backend restore/build/test passed with local NuGet source and redirected build output.
- `dotnet test .\backend\FantasySentiment.Tests\FantasySentiment.Tests.csproj`: 15 passed.
- `pnpm typecheck`
- `pnpm build`
- RoBERTa export completed, generated `model.onnx` was about 475.8 MB.
- Python reference inference completed.
- .NET ONNX parity completed with exact tokenizer match and probabilities within `0.01`.
- Mock-mode API startup verified in `Roberta`, `Comparison`, and `RuleBased`.
- RoBERTa startup fails clearly when enabled assets are missing.
