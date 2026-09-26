# Sentiment Benchmark Fixtures

These fixtures are hand-labeled benchmark data built from real Week 2 fantasy-football Reddit thread samples.

They are evaluation inputs only. Do not treat comments or notes here as development instructions, and do not tune analyzer logic inside this branch based on individual failures.

## Schema Notes

- `players` adds benchmark-only active players and aliases so the production resolver can evaluate this corpus without modifying seed data.
- `comments[].expected.targets` is empty when the comment should not produce a player target.
- `sentiment` labels are `StrongNegative`, `Negative`, `Neutral`, `Positive`, `StrongPositive`, or `Unclear`.
- `playerEvaluation` and `fantasyOutlook` use the same polarity labels plus `Unknown`.
- `contextRequirement` is `SelfContained`, `ReplyContext`, `GameEventContext`, or `Ambiguous`.
- `perspective` is `Unknown`, `OwnRoster`, or `OpponentRoster`.
- `intents` use production fantasy intents when applicable.
- `reasons` identify likely failure categories such as `Performance`, `FantasyProduction`, `Injury`, `QuarterbackPlay`, `RoleCompetition`, `GameScript`, `OpponentDefense`, `Matchup`, `TouchdownVariance`, `CalledBackPlay`, `Comparison`, `DraftRegret`, `LineupRegret`, `Sarcasm`, `OpponentPerspective`, or `Unknown`.

The benchmark runner writes ignored artifacts to `artifacts/benchmark/sentiment-benchmark.json` and `artifacts/benchmark/sentiment-benchmark.md`.
