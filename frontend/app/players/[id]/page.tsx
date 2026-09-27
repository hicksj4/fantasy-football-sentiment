import { DashboardTable } from "@/components/DashboardTable";
import { MetricCard } from "@/components/MetricCard";
import { ScorePill } from "@/components/ScorePill";
import { getJson, type AnalysisSnapshot, type DashboardPlayer, type PlayerMetrics, type PlayerScoreComment } from "@/lib/api";
import Link from "next/link";

type PageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ snapshot?: string }>;
};

export default async function PlayerPage({ params, searchParams }: PageProps) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const snapshotKey = query.snapshot ?? "latest";
  const [snapshot, player, related] = await Promise.all([
    getJson<AnalysisSnapshot>(`/api/snapshots/${snapshotKey}`),
    getJson<PlayerMetrics>(`/api/snapshots/${snapshotKey}/players/${id}`),
    getJson<DashboardPlayer[]>(`/api/snapshots/${snapshotKey}/players?sort=most-discussed&limit=8`)
  ]);

  return (
    <main>
      <header className="terminal-header">
        <div>
          <Link className="back-link" href={`/?snapshot=${snapshot.key}`}>Back to snapshot</Link>
          <p className="eyebrow">{snapshot.name} • {player.team} {player.position}</p>
          <h1>{player.name}</h1>
          <div className="alias-list">{player.aliases.map((alias) => <span key={alias}>{alias}</span>)}</div>
        </div>
        <div className="hero-score">
          <span>Snapshot sentiment</span>
          <ScorePill value={player.communitySentiment} />
        </div>
      </header>

      <section className="metrics-grid">
        <MetricCard label="Mentions" value={player.mentions} detail="Within selected snapshot" />
        <MetricCard label="Discussion share" value={`${(player.buzz * 100).toFixed(1)}%`} detail={`${snapshot.totalPlayerMentions} total mentions`} />
        <MetricCard label="Community sentiment" value={player.communitySentiment.toFixed(2)} detail={`Voting effect ${formatSigned(player.votingEffect)}`} />
        <MetricCard label="Confidence" value={player.confidence} detail={`${player.totalRedditScore} Reddit score`} />
      </section>

      <section className="panel score-explanation">
        <div className="panel-heading">
          <h2>Why This Score?</h2>
          <span>Snapshot explanation</span>
        </div>
        <div className="score-explanation-grid">
          <div className="score-stack">
            <ScoreStat
              label="Sentiment"
              value={player.sentiment}
              help="Average sentiment across all analyzed mentions."
            />
            <ScoreStat
              label="Community Sentiment"
              value={player.communitySentiment}
              help="Sentiment weighted by Reddit voting, with diminishing returns."
            />
            <ScoreStat
              label="Voting Effect"
              value={player.votingEffect}
              help="How much Reddit voting moved community sentiment away from the unweighted average."
            />
          </div>

          <div className="impact-summary">
            <span>Discussion Impact</span>
            <strong>{player.discussionImpactPercentile.toFixed(0)}th percentile</strong>
            <p>#{player.discussionImpactRank} of {snapshot.uniquePlayersMentioned} players</p>
            <small>How much weighted positive and negative discussion this player generated relative to other players in this snapshot.</small>
          </div>

          <div className="distribution-card">
            <div className="distribution-heading">
              <span>Sentiment Distribution</span>
              <strong>{player.mentions} mentions</strong>
            </div>
            <div className="distribution-bar" aria-label="Sentiment distribution">
              <span className="positive" style={{ width: `${player.positivePercent}%` }} />
              <span className="neutral" style={{ width: `${player.neutralPercent}%` }} />
              <span className="negative" style={{ width: `${player.negativePercent}%` }} />
            </div>
            <div className="distribution-counts">
              <span><b>{player.positiveMentions}</b> positive</span>
              <span><b>{player.neutralMentions}</b> neutral</span>
              <span><b>{player.negativeMentions}</b> negative</span>
            </div>
          </div>

          <div className="impact-breakdown">
            <div>
              <span>Positive weighted impact</span>
              <strong>{player.positiveImpact.toFixed(2)}</strong>
            </div>
            <div>
              <span>Negative weighted impact</span>
              <strong>{player.negativeImpact.toFixed(2)}</strong>
            </div>
          </div>
        </div>
      </section>

      <section className="explanation-comments">
        <CommentCard
          title="Most Influential Comment"
          comment={player.mostInfluentialComment}
          emptyText="No influential comment available for this player."
        />
        <CommentCard
          title="Most Endorsed Take"
          comment={player.mostEndorsedComment}
          emptyText="No endorsed take available for this player."
        />
      </section>

      <section className="signal-grid">
        {[
          [ "Add", player.addCount ],
          [ "Drop", player.dropCount ],
          [ "Start", player.startCount ],
          [ "Sit", player.sitCount ],
          [ "Buy", player.buyCount ],
          [ "Sell", player.sellCount ],
          [ "Hold", player.holdCount ],
          [ "Injury", player.injuryCount ],
          [ "Hype", player.hypeCount ],
          [ "Panic", player.panicCount ]
        ].map(([label, value]) => (
          <div className="signal" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </div>
        ))}
      </section>

      <DashboardTable
        title="Most Discussed In Snapshot"
        rows={related.filter((row) => row.playerId !== player.playerId).slice(0, 6)}
        snapshotKey={snapshot.key}
      />
    </main>
  );
}

function ScoreStat({ label, value, help }: { label: string; value: number; help: string }) {
  return (
    <div className="score-stat">
      <div>
        <span>{label}</span>
        <small>{help}</small>
      </div>
      <ScorePill value={value} kind={label === "Voting Effect" ? "change" : "sentiment"} />
    </div>
  );
}

function CommentCard({
  title,
  comment,
  emptyText
}: {
  title: string;
  comment: PlayerScoreComment | null;
  emptyText: string;
}) {
  return (
    <article className="comment-card">
      <div className="comment-card-heading">
        <span>{title}</span>
        {comment ? <strong>Score {comment.redditScore}</strong> : null}
      </div>
      {comment ? (
        <>
          <blockquote>{comment.commentExcerpt}</blockquote>
          <div className="comment-metrics">
            <span>Target sentiment {formatSigned(comment.finalSentimentScore)}</span>
            <span>Vote weight {comment.voteWeight.toFixed(2)}</span>
            <span>Contribution {formatSigned(comment.communityContribution)}</span>
            <span>{comment.targetAttributionMethod} • {(comment.targetConfidence * 100).toFixed(0)}%</span>
          </div>
          <div className="thread-title">r/{comment.subreddit} • {comment.postTitle}</div>
        </>
      ) : (
        <p className="influence-empty">{emptyText}</p>
      )}
    </article>
  );
}

function formatSigned(value: number) {
  return `${value > 0 ? "+" : ""}${value.toFixed(2)}`;
}
