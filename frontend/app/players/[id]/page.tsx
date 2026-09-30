import { MetricCard } from "@/components/MetricCard";
import { SentimentEvidence } from "@/components/SentimentEvidence";
import {
  getJson,
  type AnalysisSnapshot,
  type PlayerMentionAnalysis,
  type PlayerMentionAnalysisPage,
  type PlayerMetrics,
  type PlayerScoreComment
} from "@/lib/api";
import Link from "next/link";

type PageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ snapshot?: string; mentionFilter?: string; mentionSort?: string; mentionPage?: string }>;
};

export default async function PlayerPage({ params, searchParams }: PageProps) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const snapshotKey = query.snapshot ?? "latest";
  const mentionFilter = normalizeMentionFilter(query.mentionFilter);
  const mentionSort = normalizeMentionSort(query.mentionSort);
  const mentionPage = positiveInteger(query.mentionPage, 1);
  const mentionQuery = new URLSearchParams({
    page: mentionPage.toString(),
    pageSize: "20",
    sort: mentionSort
  });
  if (mentionFilter !== "all") {
    mentionQuery.set("disposition", mentionFilter);
  }

  const [snapshot, player, mentionAnalysis] = await Promise.all([
    getJson<AnalysisSnapshot>(`/api/snapshots/${snapshotKey}`),
    getJson<PlayerMetrics>(`/api/snapshots/${snapshotKey}/players/${id}`),
    getJson<PlayerMentionAnalysisPage>(`/api/snapshots/${snapshotKey}/players/${id}/mentions?${mentionQuery.toString()}`)
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
          <SentimentEvidence
            value={player.communitySentiment}
            hasScoredSentiment={player.hasScoredSentiment}
            scoredMentionCount={player.scoredMentionCount}
            totalMentionCount={player.totalMentionCount || player.mentions}
          />
        </div>
      </header>

      <section className="metrics-grid">
        <MetricCard label="Mentions" value={player.mentions} detail="Within selected snapshot" />
        <MetricCard label="Discussion share" value={`${(player.buzz * 100).toFixed(1)}%`} detail={`${snapshot.totalPlayerMentions} total mentions`} />
        <MetricCard
          label="Community sentiment"
          value={player.hasScoredSentiment ? player.communitySentiment.toFixed(2) : "Insufficient"}
          detail={player.hasScoredSentiment ? `Voting effect ${formatSigned(player.votingEffect)}` : "No scored sentiment evidence"}
        />
        <MetricCard
          label="Confidence"
          value={player.confidence}
          detail={`${player.scoredMentionCount}/${player.totalMentionCount || player.mentions} mentions scored • ${(player.sentimentCoverage * 100).toFixed(0)}% coverage`}
        />
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
              hasScoredSentiment={player.hasScoredSentiment}
              scoredMentionCount={player.scoredMentionCount}
              totalMentionCount={player.totalMentionCount || player.mentions}
              help="Average sentiment across scored mentions."
            />
            <ScoreStat
              label="Community Sentiment"
              value={player.communitySentiment}
              hasScoredSentiment={player.hasScoredSentiment}
              scoredMentionCount={player.scoredMentionCount}
              totalMentionCount={player.totalMentionCount || player.mentions}
              help="Sentiment weighted by Reddit voting, with diminishing returns."
            />
            <ScoreStat
              label="Voting Effect"
              value={player.votingEffect}
              hasScoredSentiment={player.hasScoredSentiment}
              scoredMentionCount={player.scoredMentionCount}
              totalMentionCount={player.totalMentionCount || player.mentions}
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
              <strong>{player.scoredMentionCount} scored</strong>
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
              <span><b>{player.mentionOnlyCount}</b> mention-only</span>
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
          [ "Injury", player.injuryCount ]
        ].map(([label, value]) => (
          <div className="signal" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </div>
        ))}
      </section>

      <CommentAnalysisSection
        snapshotKey={snapshot.key}
        page={mentionAnalysis}
        filter={mentionFilter}
        sort={mentionSort}
      />
    </main>
  );
}

function ScoreStat({
  label,
  value,
  help,
  hasScoredSentiment,
  scoredMentionCount,
  totalMentionCount
}: {
  label: string;
  value: number;
  help: string;
  hasScoredSentiment: boolean;
  scoredMentionCount: number;
  totalMentionCount: number;
}) {
  return (
    <div className="score-stat">
      <div>
        <span>{label}</span>
        <small>{help}</small>
      </div>
      <SentimentEvidence
        value={value}
        kind={label === "Voting Effect" ? "change" : "sentiment"}
        hasScoredSentiment={hasScoredSentiment}
        scoredMentionCount={scoredMentionCount}
        totalMentionCount={totalMentionCount}
      />
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

function CommentAnalysisSection({
  snapshotKey,
  page,
  filter,
  sort
}: {
  snapshotKey: string;
  page: PlayerMentionAnalysisPage;
  filter: string;
  sort: string;
}) {
  const firstShown = page.total === 0 ? 0 : (page.page - 1) * page.pageSize + 1;
  const lastShown = Math.min(page.total, page.page * page.pageSize);

  return (
    <section className="panel comment-analysis">
      <div className="panel-heading">
        <h2>Comment Analysis</h2>
        <span>{page.scoredMentionCount}/{page.totalMentionCount} scored • {(page.sentimentCoverage * 100).toFixed(0)}% coverage</span>
      </div>

      <div className="analysis-toolbar">
        <p>Mention-only comments were linked to this player but were not included in sentiment aggregates because the scorer did not have enough confidence.</p>
        <div className="analysis-controls">
          <ControlGroup
            label="Disposition"
            links={[
              ["All", buildMentionHref(snapshotKey, "all", sort, 1), filter === "all"],
              ["Scored", buildMentionHref(snapshotKey, "scored", sort, 1), filter === "scored"],
              ["Mention only", buildMentionHref(snapshotKey, "mention-only", sort, 1), filter === "mention-only"]
            ]}
          />
          <ControlGroup
            label="Sort"
            links={[
              ["Influence", buildMentionHref(snapshotKey, filter, "influence", 1), sort === "influence"],
              ["Reddit score", buildMentionHref(snapshotKey, filter, "reddit-score", 1), sort === "reddit-score"],
              ["Confidence", buildMentionHref(snapshotKey, filter, "confidence", 1), sort === "confidence"]
            ]}
          />
        </div>
      </div>

      <div className="analysis-list">
        {page.items.length === 0 ? (
          <p className="analysis-empty">No comments match this filter.</p>
        ) : (
          page.items.map((item) => <MentionAnalysisCard key={item.id} item={item} />)
        )}
      </div>

      <div className="analysis-pagination">
        <span>Showing {firstShown}-{lastShown} of {page.total}</span>
        <div>
          {page.page > 1 ? (
            <Link href={buildMentionHref(snapshotKey, filter, sort, page.page - 1)}>Previous</Link>
          ) : null}
          {page.hasMore ? (
            <Link href={buildMentionHref(snapshotKey, filter, sort, page.page + 1)}>Next</Link>
          ) : null}
        </div>
      </div>
    </section>
  );
}

function ControlGroup({
  label,
  links
}: {
  label: string;
  links: [string, string, boolean][];
}) {
  return (
    <div className="analysis-control-group">
      <span>{label}</span>
      <div>
        {links.map(([text, href, active]) => (
          <Link className={active ? "active" : ""} href={href} key={text}>{text}</Link>
        ))}
      </div>
    </div>
  );
}

function MentionAnalysisCard({ item }: { item: PlayerMentionAnalysis }) {
  const isScored = item.analysisDisposition.toLowerCase() === "scored";
  const hasInheritedParent =
    item.parentBody &&
    item.targetAttributionMethod.toLowerCase().includes("inherited");

  return (
    <article className={`mention-analysis-card ${isScored ? "scored" : "mention-only"}`}>
      <div className="mention-analysis-heading">
        <span className={`disposition-badge ${isScored ? "scored" : "mention-only"}`}>
          {isScored ? "Scored" : "Mention only"}
        </span>
        <strong>Score {item.redditScore}</strong>
      </div>

      {hasInheritedParent ? (
        <div className="parent-context">
          <span>Parent context</span>
          <p>{item.parentBody}</p>
        </div>
      ) : null}

      <p className="comment-body">
        {hasInheritedParent ? <span className="comment-context-label">Reply</span> : null}
        {item.body}
      </p>

      <div className="mention-metrics">
        <span>Sentiment {formatSigned(item.finalSentimentScore)}</span>
        <span>Raw {formatSigned(item.rawSentimentScore)}</span>
        <span>Semantic {(item.semanticConfidence * 100).toFixed(0)}%</span>
        <span>Target {(item.targetConfidence * 100).toFixed(0)}%</span>
        <span>{item.targetAttributionMethod}</span>
        <span>Vote weight {item.voteWeight.toFixed(2)}</span>
        <span>{isScored && item.weightedContribution !== null ? `Weighted contribution ${formatSigned(item.weightedContribution)}` : "Weighted contribution Not scored"}</span>
        <span>{item.fantasyOutlook}</span>
      </div>

      <div className="mention-labels">
        <LabelList title="Actions" values={item.fantasyActions} />
        <LabelList title="Signals" values={item.discussionSignals} />
        <LabelList title="Reasons" values={item.reasons.length > 0 ? item.reasons : [item.analysisDispositionReason]} />
      </div>

      <div className="thread-title">r/{item.subreddit} • {item.postTitle}</div>
    </article>
  );
}

function LabelList({ title, values }: { title: string; values: string[] }) {
  return (
    <div>
      <span>{title}</span>
      <div>
        {values.length === 0 ? <em>none</em> : values.map((value) => <b key={value}>{value}</b>)}
      </div>
    </div>
  );
}

function buildMentionHref(snapshotKey: string, filter: string, sort: string, page: number) {
  const params = new URLSearchParams({ snapshot: snapshotKey, mentionSort: sort, mentionPage: page.toString() });
  if (filter !== "all") {
    params.set("mentionFilter", filter);
  }

  return `?${params.toString()}`;
}

function normalizeMentionFilter(value?: string) {
  return value === "scored" || value === "mention-only" ? value : "all";
}

function normalizeMentionSort(value?: string) {
  return value === "reddit-score" || value === "confidence" ? value : "influence";
}

function positiveInteger(value: string | undefined, fallback: number) {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}
