# RoBERTa Sentiment Model Tools

These tools create and validate local runtime assets for:

```text
cardiffnlp/twitter-roberta-base-topic-sentiment-latest
```

The Python tools are setup/export/validation only. The ASP.NET app runs inference in .NET with ONNX Runtime.

## Export

From the repository root:

```powershell
.\tools\sentiment-model\export-model.ps1 -Python "C:\Path\To\python.exe"
```

The script installs `requirements.txt` into `tools/sentiment-model/.venv`, uses Hugging Face Optimum to export text-classification ONNX, and writes generated assets to:

```text
backend/FantasySentiment.Api/Models/Sentiment/
```

Expected files:

- `model.onnx`
- `tokenizer.json`
- `config.json`
- `tokenizer_config.json`

The generated model assets are ignored by Git.
Hugging Face downloads are cached under `tools/sentiment-model/.cache`, which is also ignored by Git.

## Python Reference

```powershell
.\tools\sentiment-model\.venv\Scripts\python.exe .\tools\sentiment-model\reference_inference.py --output .\tools\sentiment-model\python-reference.json
```

This runs the original PyTorch model and writes token IDs, attention masks, probabilities, winning label, and normalized expected-value score.

## .NET ONNX Parity

```powershell
dotnet run --project .\tools\sentiment-model\dotnet-parity\FantasySentiment.RobertaParity.csproj -- --repeat 100
```

This uses the same `RobertaOnnxSentimentModel` class used by the API.

## Cross-Runtime Check

```powershell
.\tools\sentiment-model\.venv\Scripts\python.exe .\tools\sentiment-model\compare_runtime_parity.py
```

The comparison checks:

- `input_ids`
- `attention_mask`
- probability closeness for all five labels

Tokenizer parity must match exactly. Probability parity defaults to a `0.01` absolute tolerance.

If NuGet restore needs a local config, pass it through:

```powershell
.\tools\sentiment-model\.venv\Scripts\python.exe .\tools\sentiment-model\compare_runtime_parity.py --dotnet-configfile C:\Path\To\NuGet.Local.Config --dotnet-output-path C:\Path\To\BuildOutput\
```
