import Link from "next/link";
import type { DashboardHighlightPlayer, DashboardHighlights } from "@/lib/api";
import { ScorePill } from "@/components/ScorePill";

type HighlightCardsProps = {
  highlights: DashboardHighlights;
};

type HighlightCardProps = {
  title: string;
  player: DashboardHighlightPlayer | null;
  tone: "positive" | "negative";
};

export function HighlightCards({ highlights }: HighlightCardsProps) {
  return (
    <section className="highlight-grid" aria-label="Sentiment highlights">
      <HighlightCard title="Top Positive Impact" player={highlights.positive} tone="positive" />
      <HighlightCard title="Top Negative Impact" player={highlights.negative} tone="negative" />
    </section>
  );
}

function HighlightCard({ title, player, tone }: HighlightCardProps) {
  if (!player) {
    return (
      <article className={`highlight-card ${tone}`}>
        <div className="highlight-kicker">{title}</div>
        <div className="highlight-empty">
          <strong>Not enough discussion yet.</strong>
          <span>Waiting for a stronger sample before calling out a player.</span>
        </div>
      </article>
    );
  }

  const influence = player.topInfluence;
  const summary = summaryFor(player, tone);

  return (
    <article className={`highlight-card ${tone}`}>
      <div className="highlight-topline">
        <div>
          <div className="highlight-kicker">{title}</div>
          <Link className="highlight-name" href={`/players/${player.playerId}`}>
            {player.name}
          </Link>
          <div className="highlight-meta">{player.team} • {player.position}</div>
        </div>
        <Avatar player={player} tone={tone} />
      </div>

      <p className="highlight-summary">{summary}</p>

      <div className="highlight-stats">
        <div>
          <span>Community</span>
          <ScorePill value={player.communitySentiment} />
        </div>
        <div>
          <span>Impact</span>
          <ScorePill value={player.impact} kind="change" />
        </div>
        <div>
          <span>Mentions</span>
          <strong>{player.mentions}</strong>
        </div>
        <div>
          <span>Confidence</span>
          <strong>{player.confidence}</strong>
          <small>{player.scoredMentionCount}/{player.totalMentionCount || player.mentions} scored</small>
        </div>
      </div>

      <div className="influence-block">
        <div className="influence-heading">Most influential</div>
        {influence ? (
          <>
            <blockquote>{influence.commentExcerpt}</blockquote>
            <div className="influence-metrics">
              <span>▲ {influence.redditScore}</span>
              <span>Sentiment {formatSigned(influence.sentiment)}</span>
              <span>
                Impact {formatSigned(influence.sentimentContribution)}
                <span
                  className="info-dot"
                  title="This is how much this comment contributes to the player's community sentiment after Reddit vote weighting."
                  aria-label="Impact explanation"
                >
                  i
                </span>
              </span>
            </div>
            <div className="thread-title">Thread: {influence.postTitle}</div>
          </>
        ) : (
          <p className="influence-empty">No qualifying comment in this window.</p>
        )}
      </div>
    </article>
  );
}

function Avatar({ player, tone }: { player: DashboardHighlightPlayer; tone: "positive" | "negative" }) {
  return (
    <div className={`player-avatar ${tone}`} aria-hidden="true">
      {player.headshotUrl ? <img src={player.headshotUrl} alt="" /> : <span>{initials(player.name)}</span>}
    </div>
  );
}

function initials(name: string) {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
}

function summaryFor(player: DashboardHighlightPlayer, tone: "positive" | "negative") {
  const magnitude = Math.max(Math.abs(player.impact) / Math.max(player.mentions, 1), Math.abs(player.communitySentiment));
  if (tone === "positive") {
    if (magnitude >= 0.75) {
      return "He's the GOAT 🐐";
    }
    if (magnitude >= 0.45) {
      return "League winner energy 🔥";
    }
    if (magnitude >= 0.25) {
      return "Reddit loves him 📈";
    }
    return "We're so back 🚀";
  }

  if (magnitude >= 0.75) {
    return "He's cooked 💀";
  }
  if (magnitude >= 0.45) {
    return "Panic mode activated 🚨";
  }
  if (magnitude >= 0.25) {
    return "Reddit is not happy 📉";
  }
  return "It's getting ugly 😬";
}

function formatSigned(value: number) {
  return `${value > 0 ? "+" : ""}${value.toFixed(2)}`;
}
