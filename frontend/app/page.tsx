import { DashboardTable } from "@/components/DashboardTable";
import { HighlightCards } from "@/components/HighlightCards";
import { MetricCard } from "@/components/MetricCard";
import { PlayerSearch } from "@/components/PlayerSearch";
import { ScorePill } from "@/components/ScorePill";
import { getJson, type AnalysisSnapshot, type DashboardHighlights, type DashboardPlayer, type TeamSummary } from "@/lib/api";
import Link from "next/link";

type PageProps = {
  searchParams: Promise<{ snapshot?: string }>;
};

export default async function Home({ searchParams }: PageProps) {
  const params = await searchParams;
  const snapshotKey = params.snapshot ?? "latest";

  const [snapshot, highlights, impactPlayers, waivers, panic, mostDiscussed, teams] = await Promise.all([
    getJson<AnalysisSnapshot>(`/api/snapshots/${snapshotKey}`),
    getJson<DashboardHighlights>(`/api/snapshots/${snapshotKey}/highlights`),
    getJson<DashboardPlayer[]>(`/api/snapshots/${snapshotKey}/players?sort=impact&limit=10`),
    getJson<DashboardPlayer[]>(`/api/snapshots/${snapshotKey}/players?sort=waivers&limit=8`),
    getJson<DashboardPlayer[]>(`/api/snapshots/${snapshotKey}/players?sort=panic&limit=8`),
    getJson<DashboardPlayer[]>(`/api/snapshots/${snapshotKey}/players?sort=most-discussed&limit=8`),
    getJson<TeamSummary[]>(`/api/snapshots/${snapshotKey}/teams`)
  ]);

  const topImpact = impactPlayers[0];
  const processedAt = snapshot.processedAt ? new Date(snapshot.processedAt).toLocaleDateString() : "Pending";

  return (
    <main>
      <header className="terminal-header snapshot-terminal-header">
        <div>
          <p className="eyebrow">FANTASY FOOTBALL SENTIMENT TERMINAL</p>
          <h1 className="terminal-title">Game week Reddit snapshot</h1>
          <p className="snapshot-subtitle">One completed week of r/fantasyfootball game-thread discussion.</p>
        </div>
      </header>

      <div className="dashboard-controls snapshot-controls">
        <section className="snapshot-banner" aria-label="Selected snapshot">
          <div>
            <span>{snapshot.season} • WEEK {snapshot.week}</span>
            <strong>{snapshot.name}</strong>
          </div>
          <div>
            <span>GAME WEEK SNAPSHOT</span>
            <strong>{snapshot.status}</strong>
          </div>
        </section>
        <PlayerSearch />
      </div>

      <HighlightCards highlights={highlights} />

      <section className="metrics-grid">
        <MetricCard label="Comments analyzed" value={snapshot.totalComments} detail={`${snapshot.totalPosts} game threads`} />
        <MetricCard label="Player mentions" value={snapshot.totalPlayerMentions} detail={`${snapshot.uniquePlayersMentioned} players`} />
        <MetricCard
          label="Top discussion impact"
          value={topImpact ? `#${topImpact.discussionImpactRank}` : "—"}
          detail={topImpact ? `${topImpact.name} • ${topImpact.discussionImpactPercentile.toFixed(0)}th percentile` : "No data"}
        />
        <MetricCard label="Processed" value={processedAt} detail={`${snapshot.commentsPerSecond.toFixed(1)} comments/sec`} />
      </section>

      <section className="dashboard-grid">
        <DashboardTable title="Discussion Impact" rows={impactPlayers} snapshotKey={snapshot.key} />
        <DashboardTable title="Waiver / Add Buzz" rows={waivers} mode="waiver" snapshotKey={snapshot.key} />
        <DashboardTable title="Drop / Sit / Sell Buzz" rows={panic} mode="panic" snapshotKey={snapshot.key} />
        <DashboardTable title="Most Discussed" rows={mostDiscussed} snapshotKey={snapshot.key} />
      </section>

      <section className="panel">
        <div className="panel-heading">
          <h2>Team Sentiment</h2>
          <span>{teams.length} teams</span>
        </div>
        <div className="team-grid">
          {teams.map((team) => (
            <Link className="team-row" href={`/teams/${team.team}?snapshot=${snapshot.key}`} key={team.team}>
              <strong>{team.team}</strong>
              <span>{team.mentions} mentions</span>
              <ScorePill value={team.sentiment} />
              <span>{(team.buzz * 100).toFixed(1)}% share</span>
            </Link>
          ))}
        </div>
      </section>
    </main>
  );
}
