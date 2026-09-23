# TODO

## Short-term

- Add authentication and role-based access only if this becomes more than a local tool.
- Add richer Reddit OAuth handling for user/password script apps if app-only auth is insufficient for target subreddits.
- Add raw Reddit data purge job while preserving derived `PlayerMention` and `PlayerSnapshot` analytics.
- Add configurable subreddit editing from the UI instead of only configuration.
- Add more NFL players and aliases from a replaceable CSV/JSON import.

## Analytics

- Improve player-specific intent assignment for multi-player trade/drop/start questions.
- Add rolling sentiment deltas across multiple windows, not just latest-vs-earliest in the selected window.
- Add sarcasm and negation edge-case handling after observing real comments.
- Add team/player baselines by day-of-week and game state once real traffic exists.

## UX

- Add sortable dashboard columns.
- Add pagination to the player list API when the player dataset grows.
- Add recent derived discussion signal examples without making raw comments the center of the product.
