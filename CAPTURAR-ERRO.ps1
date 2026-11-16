param()
$ErrorActionPreference = 'Stop'
if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { throw 'ADB nao encontrado.' }
$devices = @(& adb devices)
if ($LASTEXITCODE -ne 0 -or @($devices | Where-Object { $_ -match '^\S+\s+device$' }).Count -ne 1) {
    throw 'Conecte exatamente um celular com depuracao USB autorizada.'
}
$log = Join-Path ([Environment]::GetFolderPath('UserProfile')) ('Downloads\STS2-crash-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
& adb shell am force-stop com.game.sts2launcher
& adb logcat -c
& adb shell am start -n com.game.sts2launcher/com.game.sts2launcher.GodotApp
Start-Sleep -Seconds 8
& adb logcat -d -v threadtime 'godot:V' 'DOTNET:V' 'STS2Mobile:V' 'AndroidRuntime:V' 'libc:V' 'DEBUG:V' 'mono:V' 'Mono:V' 'System.err:V' '*:S' | Out-File -LiteralPath $log -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Falha ao coletar o log.' }
Write-Host "Envie este arquivo: $log" -ForegroundColor Green
Start-Process explorer.exe -ArgumentList "/select,`"$log`""
