import Link from "next/link";
import type { DashboardPlayer } from "@/lib/api";
import { SentimentEvidence } from "@/components/SentimentEvidence";

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
              {mode === "waiver" ? <th>Add/Buy</th> : null}
              {mode === "panic" ? <th>Drop/Sit/Sell</th> : null}
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
                  <SentimentEvidence
                    value={row.sentiment}
                    hasScoredSentiment={row.hasScoredSentiment}
                    scoredMentionCount={row.scoredMentionCount}
                    totalMentionCount={row.totalMentionCount || row.mentions}
                  />
                </td>
                <td>
                  <SentimentEvidence
                    value={row.communitySentiment}
                    hasScoredSentiment={row.hasScoredSentiment}
                    scoredMentionCount={row.scoredMentionCount}
                    totalMentionCount={row.totalMentionCount || row.mentions}
                  />
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
                {mode === "waiver" ? <td>{row.addCount + row.buyCount}</td> : null}
                {mode === "panic" ? <td>{row.dropCount + row.sitCount + row.sellCount}</td> : null}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
