import { DashboardTable } from "@/components/DashboardTable";
import { MetricCard } from "@/components/MetricCard";
import { SentimentEvidence } from "@/components/SentimentEvidence";
import { getJson, type AnalysisSnapshot, type TeamDetail } from "@/lib/api";
import Link from "next/link";

type PageProps = {
  params: Promise<{ team: string }>;
  searchParams: Promise<{ snapshot?: string }>;
};

export default async function TeamPage({ params, searchParams }: PageProps) {
  const [{ team }, query] = await Promise.all([params, searchParams]);
  const snapshotKey = query.snapshot ?? "latest";
  const [snapshot, detail] = await Promise.all([
    getJson<AnalysisSnapshot>(`/api/snapshots/${snapshotKey}`),
    getJson<TeamDetail>(`/api/snapshots/${snapshotKey}/teams/${team}`)
  ]);
  const leader = [...detail.players].sort((a, b) => b.mentions - a.mentions)[0];
  const impact = [...detail.players].sort((a, b) => b.impactScore - a.impactScore)[0];

  return (
    <main>
      <header className="terminal-header">
        <div>
          <Link className="back-link" href={`/?snapshot=${snapshot.key}`}>Back to snapshot</Link>
          <p className="eyebrow">{snapshot.name} team sentiment</p>
          <h1>{detail.team}</h1>
        </div>
        <div className="hero-score">
          <span>Team sentiment</span>
          <SentimentEvidence
            value={detail.sentiment}
            hasScoredSentiment={detail.hasScoredSentiment}
            scoredMentionCount={detail.scoredMentionCount}
            totalMentionCount={detail.totalMentionCount || detail.mentions}
          />
        </div>
      </header>

      <section className="metrics-grid">
        <MetricCard label="Mentions" value={detail.mentions} detail="Within selected snapshot" />
        <MetricCard label="Discussion share" value={`${(detail.buzz * 100).toFixed(1)}%`} detail={snapshot.name} />
        <MetricCard label="Most discussed" value={leader?.mentions ?? 0} detail={leader?.name ?? "No data"} />
        <MetricCard
          label="Top discussion impact"
          value={impact ? `#${impact.discussionImpactRank}` : "—"}
          detail={impact ? `${impact.name} • ${impact.discussionImpactPercentile.toFixed(0)}th percentile` : "No data"}
        />
      </section>

      <DashboardTable title={`${detail.team} Snapshot Players`} rows={detail.players} snapshotKey={snapshot.key} />
    </main>
  );
}
