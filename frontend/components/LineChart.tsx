import type { HistoryPoint } from "@/lib/api";

type LineChartProps = {
  title: string;
  points: HistoryPoint[];
  field: "mentions" | "sentiment" | "communitySentiment" | "buzz";
};

export function LineChart({ title, points, field }: LineChartProps) {
  const values = points.map((point) => Number(point[field]));
  const min = Math.min(...values, 0);
  const max = Math.max(...values, field === "buzz" ? 1 : 0.1);
  const range = max - min || 1;
  const width = 620;
  const height = 210;
  const path = values
    .map((value, index) => {
      const x = values.length === 1 ? 0 : (index / (values.length - 1)) * width;
      const y = height - ((value - min) / range) * height;
      return `${index === 0 ? "M" : "L"} ${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(" ");

  return (
    <section className="panel chart-panel">
      <div className="panel-heading">
        <h2>{title}</h2>
        <span>{points.length} points</span>
      </div>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={title}>
        <defs>
          <linearGradient id={`${field}-gradient`} x1="0" x2="1" y1="0" y2="0">
            <stop offset="0%" stopColor="#25d0ab" />
            <stop offset="55%" stopColor="#f5bf42" />
            <stop offset="100%" stopColor="#ff6b6b" />
          </linearGradient>
        </defs>
        <path d={path || "M 0 180"} fill="none" stroke={`url(#${field}-gradient)`} strokeWidth="4" />
        <line x1="0" x2={width} y1={height - ((0 - min) / range) * height} y2={height - ((0 - min) / range) * height} stroke="#273247" strokeDasharray="5 8" />
      </svg>
      <div className="chart-scale">
        <span>{min.toFixed(field === "mentions" ? 0 : 2)}</span>
        <span>{max.toFixed(field === "mentions" ? 0 : 2)}</span>
      </div>
    </section>
  );
}
