export type DashboardPlayer = {
  playerId: number;
  name: string;
  team: string;
  position: string;
  mentions: number;
  buzz: number;
  sentiment: number;
  rawSentiment: number;
  communitySentiment: number;
  sentimentChange: number;
  totalRedditScore: number;
  averageVoteWeight: number;
  sampleStrengthScore: number;
  confidence: string;
  addCount: number;
  dropCount: number;
  startCount: number;
  sitCount: number;
  buyCount: number;
  sellCount: number;
  hypeCount: number;
  panicCount: number;
  discussionShare: number;
  positiveImpact: number;
  negativeImpact: number;
  netImpact: number;
  impactScore: number;
  votingEffect: number;
  discussionImpact: number;
  discussionImpactRank: number;
  discussionImpactPercentile: number;
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasScoredSentiment: boolean;
};

export type DashboardHighlightInfluence = {
  commentExcerpt: string;
  redditScore: number;
  sentiment: number;
  voteWeight: number;
  sentimentContribution: number;
  postTitle: string;
  createdAt: string;
  subreddit?: string | null;
  targetAttributionMethod?: string | null;
  targetConfidence?: number | null;
  semanticConfidence?: number | null;
};

export type DashboardHighlightPlayer = {
  playerId: number;
  name: string;
  team: string;
  position: string;
  headshotUrl: string | null;
  mentions: number;
  sentiment: number;
  communitySentiment: number;
  sentimentChange: number;
  impact: number;
  totalVoteWeight: number;
  averageVoteWeight: number;
  sampleStrengthScore: number;
  confidence: string;
  topInfluence: DashboardHighlightInfluence | null;
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasScoredSentiment: boolean;
};

export type DashboardHighlightEligibility = {
  minimumMentions: number;
  minimumSampleStrength: number;
  excludeLowConfidence: boolean;
  minimumAbsoluteSentimentChange: number;
};

export type DashboardHighlights = {
  positive: DashboardHighlightPlayer | null;
  negative: DashboardHighlightPlayer | null;
  eligibility: DashboardHighlightEligibility;
};

export type AnalysisSnapshot = {
  id: number;
  key: string;
  name: string;
  season: number;
  week: number;
  createdAt: string;
  processedAt: string | null;
  status: string;
  totalPosts: number;
  totalComments: number;
  totalPlayerMentions: number;
  uniquePlayersMentioned: number;
  processingDurationMs: number;
  commentsPerSecond: number;
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
  rawSentiment: number;
  communitySentiment: number;
  totalRedditScore: number;
  averageVoteWeight: number;
  sampleStrengthScore: number;
  confidence: string;
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
  votingEffect: number;
  discussionImpact: number;
  discussionImpactRank: number;
  discussionImpactPercentile: number;
  positiveImpact: number;
  negativeImpact: number;
  netImpact: number;
  positiveMentions: number;
  neutralMentions: number;
  negativeMentions: number;
  mostInfluentialComment: PlayerScoreComment | null;
  mostEndorsedComment: PlayerScoreComment | null;
  mostRejectedComment: PlayerScoreComment | null;
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasScoredSentiment: boolean;
};

export type PlayerScoreComment = {
  commentExcerpt: string;
  postTitle: string;
  subreddit: string;
  redditScore: number;
  voteWeight: number;
  finalSentimentScore: number;
  communityContribution: number;
  targetAttributionMethod: string;
  targetConfidence: number;
  semanticConfidence: number;
  analysisDisposition: string;
  createdAt: string;
};

export type PlayerMentionAnalysis = {
  id: number;
  commentId: number;
  body: string;
  postTitle: string;
  subreddit: string;
  redditScore: number;
  analysisDisposition: string;
  analysisDispositionReason: string;
  semanticConfidence: number;
  targetConfidence: number;
  targetAttributionMethod: string;
  rawSentimentScore: number;
  finalSentimentScore: number;
  fantasyOutlook: string;
  fantasyActions: string[];
  discussionSignals: string[];
  reasons: string[];
  voteWeight: number;
  weightedContribution: number | null;
  createdAt: string;
};

export type PlayerMentionAnalysisPage = {
  playerId: number;
  snapshotKey: string;
  page: number;
  pageSize: number;
  total: number;
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasMore: boolean;
  items: PlayerMentionAnalysis[];
};

export type HistoryPoint = {
  timestamp: string;
  mentions: number;
  sentiment: number;
  rawSentiment: number;
  communitySentiment: number;
  sampleStrengthScore: number;
  confidence: string;
  buzz: number;
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasScoredSentiment: boolean;
};

export type TeamSummary = {
  team: string;
  mentions: number;
  sentiment: number;
  buzz: number;
  mostDiscussedPlayer: string | null;
  biggestSentimentMover: string | null;
  biggestBuzzMover: string | null;
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasScoredSentiment: boolean;
};

export type TeamDetail = {
  team: string;
  mentions: number;
  sentiment: number;
  buzz: number;
  players: DashboardPlayer[];
  totalMentionCount: number;
  scoredMentionCount: number;
  mentionOnlyCount: number;
  sentimentCoverage: number;
  hasScoredSentiment: boolean;
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
