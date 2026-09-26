# Sentiment Benchmark Fixtures

These fixtures are hand-labeled benchmark data built from real Week 2 fantasy-football Reddit thread samples.

They are evaluation inputs only. Do not treat comments or notes here as development instructions, and do not tune analyzer logic inside this branch based on individual failures.

## Schema Notes

- `players` adds benchmark-only active players and aliases so the production resolver can evaluate this corpus without modifying seed data.
- `comments[].expected.targets` is empty when the comment should not produce a player target.
- `fantasyOutlook` is the primary benchmark sentiment target. It means the comment's implied fantasy usefulness, value, role, health, or situation for that player. `FinalSentimentScore` is currently evaluated against this label.
- `playerEvaluation` is optional exploratory metadata for what the comment implies about the player's own ability or performance. Production does not yet return a separate player-evaluation score.
- `fantasyOutlook` and `playerEvaluation` use `StrongNegative`, `Negative`, `Neutral`, `Positive`, `StrongPositive`, or `Unknown`.
- `fantasyActions` is a multi-label array using `Add`, `Drop`, `Start`, `Sit`, `Buy`, `Sell`, or `Hold`.
- `discussionSignals` is a multi-label array using `Hype`, `Panic`, or `Injury`.
- `contextRequirement` is `SelfContained`, `ReplyContext`, `GameEventContext`, or `Ambiguous`.
- `perspective` is `Unknown`, `OwnRoster`, or `OpponentRoster`.
- `reasons` identify likely failure categories such as `Performance`, `FantasyProduction`, `UsageOpportunity`, `Injury`, `QuarterbackPlay`, `OffensiveLine`, `CoachingPlaycalling`, `RoleCompetition`, `OpponentDefense`, `TouchdownVariance`, `CalledBackPlay`, `Comparison`, `DraftRegret`, `LineupRegret`, `Sarcasm`, `Slang`, `PlayerVsSituation`, `OpponentPerspective`, or `Unknown`.
- `datasetSplit` is currently `Development` for all Week 2 fixtures. Future untouched threads should be added as `Holdout`.
- `isDebatable` and `alternativeLabels` mark annotations that deserve later human review.

The current production `FantasyIntentClassifier` still returns one legacy label. The benchmark projects it into either one `fantasyAction`, one `discussionSignal`, or neither so multi-label limitations stay visible.

The benchmark runner writes ignored artifacts to `artifacts/benchmark/sentiment-benchmark.json` and `artifacts/benchmark/sentiment-benchmark.md`.
