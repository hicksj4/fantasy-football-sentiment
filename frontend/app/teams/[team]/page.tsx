import { DashboardTable } from "@/components/DashboardTable";
import { MetricCard } from "@/components/MetricCard";
import { ScorePill } from "@/components/ScorePill";
import { getJson, withWindow, type TeamDetail } from "@/lib/api";
import Link from "next/link";

type PageProps = {
  params: Promise<{ team: string }>;
  searchParams: Promise<{ window?: string }>;
};

export default async function TeamPage({ params, searchParams }: PageProps) {
  const [{ team }, query] = await Promise.all([params, searchParams]);
  const windowValue = query.window ?? "24h";
  const detail = await getJson<TeamDetail>(withWindow(`/api/teams/${team}`, windowValue));
  const leader = [...detail.players].sort((a, b) => b.mentions - a.mentions)[0];
  const mover = [...detail.players].sort((a, b) => b.sentimentChange - a.sentimentChange)[0];
  const buzz = [...detail.players].sort((a, b) => b.buzz - a.buzz)[0];

  return (
    <main>
      <header className="terminal-header">
        <div>
          <Link className="back-link" href={`/?window=${windowValue}`}>Back to terminal</Link>
          <p className="eyebrow">Team sentiment</p>
          <h1>{detail.team}</h1>
        </div>
        <div className="hero-score">
          <span>Team sentiment</span>
          <ScorePill value={detail.sentiment} />
        </div>
      </header>

      <section className="metrics-grid">
        <MetricCard label="Mentions" value={detail.mentions} detail={`${windowValue} window`} />
        <MetricCard label="Peak buzz" value={`${detail.buzz.toFixed(1)}x`} detail={buzz?.name ?? "No data"} />
        <MetricCard label="Most discussed" value={leader?.mentions ?? 0} detail={leader?.name ?? "No data"} />
        <MetricCard label="Best mover" value={mover ? `+${mover.sentimentChange.toFixed(2)}` : "0.00"} detail={mover?.name ?? "No data"} />
      </section>

      <DashboardTable title={`${detail.team} Players`} rows={detail.players} />
    </main>
  );
}
