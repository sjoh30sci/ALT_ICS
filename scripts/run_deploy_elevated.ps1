$logFile = "$env:TEMP\deploy_output.log"
$null = New-Item -ItemType File -Path $logFile -Force
$deployScript = "$PSScriptRoot\deploy.tmp.ps1"
& $deployScript *>&1 | ForEach-Object { Add-Content -LiteralPath $logFile -Value $_ -Encoding UTF8 }
exit $LASTEXITCODE
