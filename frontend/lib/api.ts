export type DashboardPlayer = {
  playerId: number;
  name: string;
  team: string;
  position: string;
  mentions: number;
  buzz: number;
  sentiment: number;
  sentimentChange: number;
  addCount: number;
  dropCount: number;
  startCount: number;
  sitCount: number;
  buyCount: number;
  sellCount: number;
  hypeCount: number;
  panicCount: number;
};

export type PlayerListItem = {
  id: number;
  name: string;
  team: string;
  position: string;
  aliases: string[];
};

export type PlayerMetrics = {
  playerId: number;
  name: string;
  team: string;
  position: string;
  aliases: string[];
  mentions: number;
  buzz: number;
  sentiment: number;
  positivePercent: number;
  neutralPercent: number;
  negativePercent: number;
  addCount: number;
  dropCount: number;
  startCount: number;
  sitCount: number;
  buyCount: number;
  sellCount: number;
  holdCount: number;
  injuryCount: number;
  hypeCount: number;
  panicCount: number;
};

export type HistoryPoint = {
  timestamp: string;
  mentions: number;
  sentiment: number;
  buzz: number;
};

export type TeamSummary = {
  team: string;
  mentions: number;
  sentiment: number;
  buzz: number;
  mostDiscussedPlayer: string | null;
  biggestSentimentMover: string | null;
  biggestBuzzMover: string | null;
};

export type TeamDetail = {
  team: string;
  mentions: number;
  sentiment: number;
  buzz: number;
  players: DashboardPlayer[];
};

const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5044";

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`API request failed: ${response.status} ${response.statusText}`);
  }

  return response.json() as Promise<T>;
}

export function withWindow(path: string, windowValue: string): string {
  const separator = path.includes("?") ? "&" : "?";
  return `${path}${separator}window=${encodeURIComponent(windowValue)}`;
}
