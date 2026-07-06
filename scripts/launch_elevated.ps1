$scriptPath = Resolve-Path -LiteralPath "$PSScriptRoot\run_deploy_elevated.ps1"
$proc = Start-Process -FilePath "powershell.exe" -ArgumentList "-ExecutionPolicy Bypass -NoProfile -File `"$($scriptPath.Path)`"" -Verb RunAs -WindowStyle Normal -Wait -PassThru
exit $proc.ExitCode
