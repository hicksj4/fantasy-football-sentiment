param(
    [string]$Python = "python",
    [string]$OutputDir = "..\..\backend\FantasySentiment.Api\Models\Sentiment",
    [string]$ModelId = "cardiffnlp/twitter-roberta-base-topic-sentiment-latest"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$resolvedOutput = Join-Path $scriptDir $OutputDir
$venvDir = Join-Path $scriptDir ".venv"
$venvPython = Join-Path $venvDir "Scripts\python.exe"

if (!(Test-Path $venvPython)) {
    & $Python -m venv $venvDir
}

& $venvPython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $venvPython -m pip install -r (Join-Path $scriptDir "requirements.txt")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $venvPython (Join-Path $scriptDir "export_model.py") --model-id $ModelId --output-dir $resolvedOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "RoBERTa sentiment assets exported to $resolvedOutput"
Write-Host "Set SENTIMENT_ROBERTA_ENABLED=true and SENTIMENT_MODE=Comparison or Roberta to use them."
