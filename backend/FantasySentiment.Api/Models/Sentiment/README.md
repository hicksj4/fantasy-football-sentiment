# Sentiment Model Assets

Generated RoBERTa assets live here for local development:

- `model.onnx`
- `tokenizer.json`
- `config.json`
- `tokenizer_config.json`

These generated files are intentionally ignored by Git because the ONNX model is large. Recreate them with:

```powershell
.\tools\sentiment-model\export-model.ps1
```
