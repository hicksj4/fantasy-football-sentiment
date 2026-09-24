import { DashboardTable } from "@/components/DashboardTable";
import { LineChart } from "@/components/LineChart";
import { MetricCard } from "@/components/MetricCard";
import { ScorePill } from "@/components/ScorePill";
import { getJson, withWindow, type DashboardPlayer, type HistoryPoint, type PlayerMetrics } from "@/lib/api";
import Link from "next/link";

type PageProps = {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ window?: string }>;
};

export default async function PlayerPage({ params, searchParams }: PageProps) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const windowValue = query.window ?? "24h";
  const [player, history, related] = await Promise.all([
    getJson<PlayerMetrics>(`/api/players/${id}`),
    getJson<HistoryPoint[]>(withWindow(`/api/players/${id}/history`, windowValue)),
    getJson<DashboardPlayer[]>(withWindow("/api/most-discussed?limit=8", windowValue))
  ]);

  return (
    <main>
      <header className="terminal-header">
        <div>
          <Link className="back-link" href={`/?window=${windowValue}`}>Back to terminal</Link>
          <p className="eyebrow">{player.team} {player.position}</p>
          <h1>{player.name}</h1>
          <div className="alias-list">{player.aliases.map((alias) => <span key={alias}>{alias}</span>)}</div>
        </div>
        <div className="hero-score">
          <span>Current sentiment</span>
          <ScorePill value={player.sentiment} />
        </div>
      </header>

      <section className="metrics-grid">
        <MetricCard label="Mentions" value={player.mentions} detail="Recent aggregation" />
        <MetricCard label="Buzz" value={`${player.buzz.toFixed(1)}x`} detail="Versus baseline" />
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

      <section className="chart-grid">
        <LineChart title="Sentiment over time" points={history} field="sentiment" />
        <LineChart title="Community sentiment over time" points={history} field="communitySentiment" />
        <LineChart title="Mentions over time" points={history} field="mentions" />
        <LineChart title="Buzz over time" points={history} field="buzz" />
      </section>

      <DashboardTable title="Most Discussed Nearby" rows={related.filter((row) => row.playerId !== player.playerId).slice(0, 6)} />
    </main>
  );
}
