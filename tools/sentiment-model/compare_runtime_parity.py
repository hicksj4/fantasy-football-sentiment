import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path


LABELS = [
    "strongly negative",
    "negative",
    "negative or neutral",
    "positive",
    "strongly positive",
]


def main() -> int:
    script_dir = Path(__file__).resolve().parent
    repo_root = script_dir.parent.parent
    assets = repo_root / "backend" / "FantasySentiment.Api" / "Models" / "Sentiment"

    parser = argparse.ArgumentParser(description="Compare Python Transformers and .NET ONNX RoBERTa outputs.")
    parser.add_argument("--python", default=sys.executable)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--dotnet-configfile")
    parser.add_argument("--dotnet-output-path")
    parser.add_argument("--skip-dotnet-restore", action="store_true")
    parser.add_argument("--examples", default=str(script_dir / "examples.json"))
    parser.add_argument("--model-id", default="cardiffnlp/twitter-roberta-base-topic-sentiment-latest")
    parser.add_argument("--model-path", default=str(assets / "model.onnx"))
    parser.add_argument("--tokenizer-path", default=str(assets / "tokenizer.json"))
    parser.add_argument("--config-path", default=str(assets / "config.json"))
    parser.add_argument("--tokenizer-config-path", default=str(assets / "tokenizer_config.json"))
    parser.add_argument("--max-sequence-length", default="512")
    parser.add_argument("--probability-tolerance", type=float, default=0.01)
    args = parser.parse_args()

    with tempfile.TemporaryDirectory() as temp_dir:
        reference_json = Path(temp_dir) / "python-reference.json"
        run(
            [
                args.python,
                str(script_dir / "reference_inference.py"),
                "--model-id",
                args.model_id,
                "--examples",
                args.examples,
                "--max-sequence-length",
                args.max_sequence_length,
                "--output",
                str(reference_json),
            ]
        )

        parity_project = script_dir / "dotnet-parity" / "FantasySentiment.RobertaParity.csproj"
        if not args.skip_dotnet_restore:
            restore_command = [args.dotnet, "restore", str(parity_project)]
            if args.dotnet_configfile:
                restore_command.extend(["--configfile", args.dotnet_configfile])
            run(restore_command)

        dotnet_command = [
            args.dotnet,
            "run",
            "--no-restore",
            "--project",
            str(parity_project),
        ]
        if args.dotnet_output_path:
            dotnet_command.append(f"-p:OutputPath={args.dotnet_output_path}")
        dotnet_command.extend(
            [
                "--",
                "--examples",
                args.examples,
                "--model-path",
                args.model_path,
                "--tokenizer-path",
                args.tokenizer_path,
                "--config-path",
                args.config_path,
                "--tokenizer-config-path",
                args.tokenizer_config_path,
                "--max-sequence-length",
                args.max_sequence_length,
            ]
        )
        dotnet_output = subprocess.check_output(
            dotnet_command,
            text=True,
        )
        python_payload = json.loads(reference_json.read_text(encoding="utf-8"))
        dotnet_payload = json.loads(dotnet_output)
    failures = []

    dotnet_by_id = {example["id"]: example for example in dotnet_payload["examples"]}
    for python_example in python_payload["examples"]:
        dotnet_example = dotnet_by_id[python_example["id"]]
        if python_example["inputIds"] != dotnet_example["inputIds"]:
            failures.append(f"{python_example['id']}: input_ids differ")
        if python_example["attentionMask"] != dotnet_example["attentionMask"]:
            failures.append(f"{python_example['id']}: attention_mask differs")
        for label in LABELS:
            difference = abs(
                python_example["probabilities"][label]
                - dotnet_example["probabilities"][label]
            )
            if difference > args.probability_tolerance:
                failures.append(
                    f"{python_example['id']}: probability {label} differs by {difference:.6f}"
                )

    report = {
        "tokenizationMatched": not any("input_ids" in failure or "attention_mask" in failure for failure in failures),
        "probabilitiesWithinTolerance": not failures,
        "probabilityTolerance": args.probability_tolerance,
        "dotnetThroughput": {
            "elapsedMs": dotnet_payload["elapsedMs"],
            "analysesPerSecond": dotnet_payload["analysesPerSecond"],
        },
        "failures": failures,
        "python": python_payload,
        "dotnet": dotnet_payload,
    }
    print(json.dumps(report, indent=2))

    return 1 if failures else 0


def run(command: list[str]) -> None:
    subprocess.run(command, check=True, stdout=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main())
