import argparse
import json
import os
from pathlib import Path


def configure_local_huggingface_cache() -> None:
    cache_dir = Path(__file__).resolve().parent / ".cache" / "huggingface"
    os.environ.setdefault("HF_HOME", str(cache_dir))
    os.environ.setdefault("HUGGINGFACE_HUB_CACHE", str(cache_dir / "hub"))
    os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")


configure_local_huggingface_cache()

import torch
from transformers import AutoModelForSequenceClassification, AutoTokenizer


LABELS = [
    "strongly negative",
    "negative",
    "negative or neutral",
    "positive",
    "strongly positive",
]
WEIGHTS = torch.tensor([-1.0, -0.5, 0.0, 0.5, 1.0], dtype=torch.float32)


def model_input(context: str, target: str) -> str:
    return f"{context.strip()} </s> {target.strip()}".strip()


def main() -> int:
    parser = argparse.ArgumentParser(description="Run PyTorch reference inference for target sentiment.")
    parser.add_argument("--model-id", default="cardiffnlp/twitter-roberta-base-topic-sentiment-latest")
    parser.add_argument("--examples", default=str(Path(__file__).with_name("examples.json")))
    parser.add_argument("--max-sequence-length", type=int, default=512)
    parser.add_argument("--output")
    args = parser.parse_args()

    examples = json.loads(Path(args.examples).read_text(encoding="utf-8"))
    tokenizer = AutoTokenizer.from_pretrained(args.model_id, use_fast=True)
    model = AutoModelForSequenceClassification.from_pretrained(args.model_id)
    model.eval()

    outputs = []
    with torch.no_grad():
        for example in examples:
            text = model_input(example["context"], example["target"])
            encoded = tokenizer(
                text,
                return_tensors="pt",
                truncation=True,
                max_length=args.max_sequence_length,
            )
            logits = model(**encoded).logits[0]
            probabilities = torch.softmax(logits, dim=-1)
            score = float((probabilities * WEIGHTS).sum().clamp(-1, 1))
            winning_index = int(torch.argmax(probabilities))
            outputs.append(
                {
                    "id": example["id"],
                    "context": example["context"],
                    "target": example["target"],
                    "modelInput": text,
                    "inputIds": encoded["input_ids"][0].tolist(),
                    "attentionMask": encoded["attention_mask"][0].tolist(),
                    "sequenceLength": int(encoded["input_ids"].shape[1]),
                    "probabilities": {
                        label: round(float(probabilities[index]), 8)
                        for index, label in enumerate(LABELS)
                    },
                    "winningLabel": LABELS[winning_index],
                    "score": round(score, 6),
                }
            )

    payload = {
        "runtime": "python-transformers",
        "modelId": args.model_id,
        "maxSequenceLength": args.max_sequence_length,
        "examples": outputs,
    }

    text = json.dumps(payload, indent=2)
    if args.output:
        Path(args.output).write_text(text + "\n", encoding="utf-8")
    else:
        print(text)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
