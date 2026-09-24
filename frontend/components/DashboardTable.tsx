import Link from "next/link";
import type { DashboardPlayer } from "@/lib/api";
import { ScorePill } from "@/components/ScorePill";

type DashboardTableProps = {
  title: string;
  rows: DashboardPlayer[];
  mode?: "default" | "waiver" | "panic";
};

export function DashboardTable({ title, rows, mode = "default" }: DashboardTableProps) {
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
              <th>Buzz</th>
              <th>Sentiment</th>
              <th>Community</th>
              <th>Change</th>
              {mode === "waiver" ? <th>Add/Hype</th> : null}
              {mode === "panic" ? <th>Drop/Panic</th> : null}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.playerId}>
                <td>
                  <Link href={`/players/${row.playerId}`}>{row.name}</Link>
                </td>
                <td>
                  <Link href={`/teams/${row.team}`}>{row.team}</Link>
                </td>
                <td>{row.position}</td>
                <td>{row.mentions}</td>
                <td>
                  <ScorePill value={row.buzz} kind="buzz" />
                </td>
                <td>
                  <ScorePill value={row.sentiment} />
                </td>
                <td>
                  <ScorePill value={row.communitySentiment} />
                </td>
                <td>
                  <ScorePill value={row.sentimentChange} kind="change" />
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
