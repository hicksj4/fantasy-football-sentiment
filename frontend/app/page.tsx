import { DashboardTable } from "@/components/DashboardTable";
import { MetricCard } from "@/components/MetricCard";
import { PlayerSearch } from "@/components/PlayerSearch";
import { ScorePill } from "@/components/ScorePill";
import { getJson, withWindow, type DashboardPlayer, type TeamSummary } from "@/lib/api";
import Link from "next/link";

type PageProps = {
  searchParams: Promise<{ window?: string }>;
};

const windows = [ "1h", "6h", "24h", "7d" ];

export default async function Home({ searchParams }: PageProps) {
  const params = await searchParams;
  const windowValue = windows.includes(params.window ?? "") ? params.window! : "24h";

  const [trending, risers, fallers, waivers, panic, mostDiscussed, teams] = await Promise.all([
    getJson<DashboardPlayer[]>(withWindow("/api/trending?limit=10", windowValue)),
    getJson<DashboardPlayer[]>(withWindow("/api/sentiment/risers?limit=8", windowValue)),
    getJson<DashboardPlayer[]>(withWindow("/api/sentiment/fallers?limit=8", windowValue)),
    getJson<DashboardPlayer[]>(withWindow("/api/waivers?limit=8", windowValue)),
    getJson<DashboardPlayer[]>(withWindow("/api/panic?limit=8", windowValue)),
    getJson<DashboardPlayer[]>(withWindow("/api/most-discussed?limit=8", windowValue)),
    getJson<TeamSummary[]>(withWindow("/api/teams", windowValue))
  ]);

  const totalMentions = mostDiscussed.reduce((sum, player) => sum + player.mentions, 0);
  const hottest = trending[0];
  const bestRiser = risers[0];
  const panicLeader = panic[0];

  return (
    <main>
      <header className="terminal-header">
        <div>
          <p className="eyebrow">Fantasy Football Sentiment Terminal</p>
          <h1>Reddit player buzz, velocity, and intent</h1>
        </div>
        <PlayerSearch />
      </header>

      <nav className="time-filters" aria-label="Time filter">
        {windows.map((item) => (
          <Link className={item === windowValue ? "active" : ""} href={`/?window=${item}`} key={item}>
            {item}
          </Link>
        ))}
      </nav>

      <section className="metrics-grid">
        <MetricCard label="Tracked mentions" value={totalMentions} detail={`${windowValue} window`} />
        <MetricCard label="Top buzz" value={hottest ? `${hottest.buzz.toFixed(1)}x` : "0x"} detail={hottest?.name ?? "No data"} />
        <MetricCard label="Best sentiment move" value={bestRiser ? `+${bestRiser.sentimentChange.toFixed(2)}` : "0.00"} detail={bestRiser?.name ?? "No data"} />
        <MetricCard label="Highest panic signal" value={panicLeader ? panicLeader.dropCount + panicLeader.sitCount + panicLeader.sellCount + panicLeader.panicCount : 0} detail={panicLeader?.name ?? "No data"} />
      </section>

      <section className="dashboard-grid">
        <DashboardTable title="Trending Players" rows={trending} />
        <DashboardTable title="Waiver Buzz" rows={waivers} mode="waiver" />
        <DashboardTable title="Sentiment Risers" rows={risers} />
        <DashboardTable title="Sentiment Fallers" rows={fallers} />
        <DashboardTable title="Panic / Drop Buzz" rows={panic} mode="panic" />
        <DashboardTable title="Most Discussed" rows={mostDiscussed} />
      </section>

      <section className="panel">
        <div className="panel-heading">
          <h2>Team Sentiment</h2>
          <span>{teams.length} teams</span>
        </div>
        <div className="team-grid">
          {teams.map((team) => (
            <Link className="team-row" href={`/teams/${team.team}?window=${windowValue}`} key={team.team}>
              <strong>{team.team}</strong>
              <span>{team.mentions} mentions</span>
              <ScorePill value={team.sentiment} />
              <ScorePill value={team.buzz} kind="buzz" />
            </Link>
          ))}
        </div>
      </section>
    </main>
  );
}
