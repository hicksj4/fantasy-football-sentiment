type ScorePillProps = {
  value: number;
  kind?: "sentiment" | "buzz" | "change";
  suffix?: string;
};

export function ScorePill({ value, kind = "sentiment", suffix = "" }: ScorePillProps) {
  const tone =
    kind === "buzz"
      ? value >= 3
        ? "hot"
        : value >= 1.5
          ? "warm"
          : "neutral"
      : value > 0.12
        ? "positive"
        : value < -0.12
          ? "negative"
          : "neutral";

  const formatted =
    kind === "buzz"
      ? `${value.toFixed(1)}x`
      : `${value > 0 && kind === "change" ? "+" : ""}${value.toFixed(2)}${suffix}`;

  return <span className={`score-pill ${tone}`}>{formatted}</span>;
}
