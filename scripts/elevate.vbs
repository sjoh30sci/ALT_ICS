Set UAC = CreateObject("Shell.Application")
UAC.ShellExecute "powershell.exe", "-ExecutionPolicy Bypass -NoProfile -File """ & "C:\DEV\ALT_ICS\scripts\run_deploy_elevated.ps1" & """", "", "runas", 1
