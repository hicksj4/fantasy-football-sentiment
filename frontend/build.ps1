if (-not (Test-Path ".\node_modules\next\dist\bin\next")) {
  Write-Error "Frontend dependencies are missing. Install them first with pnpm, or ask Codex to run pnpm install."
  exit 1
}

node .\node_modules\next\dist\bin\next build
