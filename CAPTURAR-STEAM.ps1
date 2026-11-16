$ErrorActionPreference = 'Stop'
if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { throw 'adb nao encontrado no PATH.' }
$log = Join-Path ([Environment]::GetFolderPath('Desktop')) ("STS2-r911-steam-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
& adb wait-for-device
& adb logcat -c
Write-Host "Capturando em $log"
Write-Host 'Abra o app. Teste o Steam Guard alternando de app e depois hospede uma sala Steam.'
Write-Host 'Peca ao amigo para atualizar a lista. Ao terminar, pressione Ctrl+C e envie o TXT.'
& adb logcat -v threadtime 'godot:I' 'DOTNET:V' 'AndroidRuntime:E' '*:S' 2>&1 | Tee-Object -FilePath $log
