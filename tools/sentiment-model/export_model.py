import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path


def configure_local_huggingface_cache() -> None:
    cache_dir = Path(__file__).resolve().parent / ".cache" / "huggingface"
    os.environ.setdefault("HF_HOME", str(cache_dir))
    os.environ.setdefault("HUGGINGFACE_HUB_CACHE", str(cache_dir / "hub"))
    os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")


configure_local_huggingface_cache()

from transformers import AutoTokenizer


def main() -> int:
    parser = argparse.ArgumentParser(description="Export CardiffNLP target-sentiment RoBERTa to ONNX.")
    parser.add_argument("--model-id", default="cardiffnlp/twitter-roberta-base-topic-sentiment-latest")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--opset", default="17")
    args = parser.parse_args()

    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    venv_cli = Path(sys.executable).with_name("optimum-cli.exe")
    optimum_cli = str(venv_cli) if venv_cli.exists() else shutil.which("optimum-cli")
    if optimum_cli is None:
        raise RuntimeError("optimum-cli was not found. Install requirements.txt first.")

    subprocess.run(
        [
            optimum_cli,
            "export",
            "onnx",
            "--model",
            args.model_id,
            "--task",
            "text-classification",
            "--opset",
            args.opset,
            str(output_dir),
        ],
        check=True,
    )

    tokenizer = AutoTokenizer.from_pretrained(args.model_id, use_fast=True)
    tokenizer.save_pretrained(output_dir)

    model_path = output_dir / "model.onnx"
    if not model_path.exists():
        raise RuntimeError(f"Expected ONNX model was not created: {model_path}")

    print(f"exported_model={model_path}")
    print(f"model_size_mb={model_path.stat().st_size / 1024 / 1024:.1f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
