import Link from "next/link";
import type { DashboardPlayer } from "@/lib/api";
import { ScorePill } from "@/components/ScorePill";

type DashboardTableProps = {
  title: string;
  rows: DashboardPlayer[];
  mode?: "default" | "waiver" | "panic";
  snapshotKey?: string;
};

export function DashboardTable({ title, rows, mode = "default", snapshotKey }: DashboardTableProps) {
  return (
    <section className="panel">
      <div className="panel-heading">
        <h2>{title}</h2>
        <span>{rows.length} tracked</span>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Player</th>
              <th>Team</th>
              <th>Pos</th>
              <th>Mentions</th>
              <th>Share</th>
              <th>Sentiment</th>
              <th>Community</th>
              <th>Discussion Impact</th>
              {mode === "waiver" ? <th>Add/Hype</th> : null}
              {mode === "panic" ? <th>Drop/Panic</th> : null}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.playerId}>
                <td>
                  <Link href={`/players/${row.playerId}${snapshotKey ? `?snapshot=${snapshotKey}` : ""}`}>{row.name}</Link>
                </td>
                <td>
                  <Link href={`/teams/${row.team}${snapshotKey ? `?snapshot=${snapshotKey}` : ""}`}>{row.team}</Link>
                </td>
                <td>{row.position}</td>
                <td>{row.mentions}</td>
                <td>{(row.discussionShare * 100).toFixed(1)}%</td>
                <td>
                  <ScorePill value={row.sentiment} />
                </td>
                <td>
                  <ScorePill value={row.communitySentiment} />
                </td>
                <td>
                  <Link
                    className="impact-rank"
                    href={`/players/${row.playerId}${snapshotKey ? `?snapshot=${snapshotKey}` : ""}`}
                    title="How much weighted sentiment-bearing discussion this player generated relative to other players."
                  >
                    <strong>#{row.discussionImpactRank || "—"}</strong>
                    <span>{row.discussionImpactPercentile.toFixed(0)}th pct</span>
                  </Link>
                </td>
                {mode === "waiver" ? <td>{row.addCount + row.hypeCount}</td> : null}
                {mode === "panic" ? <td>{row.dropCount + row.sitCount + row.sellCount + row.panicCount}</td> : null}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
