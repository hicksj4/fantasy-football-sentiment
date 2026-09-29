import { ScorePill } from "@/components/ScorePill";

type SentimentEvidenceProps = {
  value: number;
  hasScoredSentiment: boolean;
  scoredMentionCount: number;
  totalMentionCount: number;
  kind?: "sentiment" | "change";
};

export function SentimentEvidence({
  value,
  hasScoredSentiment,
  scoredMentionCount,
  totalMentionCount,
  kind = "sentiment"
}: SentimentEvidenceProps) {
  if (!hasScoredSentiment) {
    return (
      <span className="insufficient-score" title={`${totalMentionCount} mentions, 0 scored for sentiment`}>
        Insufficient evidence
      </span>
    );
  }

  return (
    <span className="score-with-note">
      <ScorePill value={value} kind={kind} />
      <small>{scoredMentionCount}/{totalMentionCount} scored</small>
    </span>
  );
}
