import { DashboardTable } from "@/components/DashboardTable";
import { MetricCard } from "@/components/MetricCard";
import { ScorePill } from "@/components/ScorePill";
import { getJson, type AnalysisSnapshot, type DashboardPlayer, type PlayerMetrics } from "@/lib/api";
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
        <MetricCard label="Community sentiment" value={player.communitySentiment.toFixed(2)} detail={`Raw ${player.rawSentiment.toFixed(2)}`} />
        <MetricCard label="Confidence" value={player.confidence} detail={`${player.totalRedditScore} Reddit score`} />
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

      <DashboardTable title="Most Discussed In Snapshot" rows={related.filter((row) => row.playerId !== player.playerId).slice(0, 6)} />
    </main>
  );
}
