param([switch]$SomenteBuild)
$ErrorActionPreference = 'Stop'
$externos = Join-Path $PSScriptRoot 'externos'
$downloads = Join-Path $env:USERPROFILE 'Downloads'
$guardado = Join-Path $env:USERPROFILE 'STS2-Guardado'
$necessarios = @('original.apk', '*apktool*.jar', '*uber-apk-signer*.jar')
New-Item -ItemType Directory -Path $externos -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($padrao in $necessarios) {
    if (@(Get-ChildItem -LiteralPath $externos -Filter $padrao -File -ErrorAction SilentlyContinue).Count -gt 0) { continue }
    $solto = @(Get-ChildItem -LiteralPath $downloads -Filter $padrao -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    if ($solto.Count -gt 0) { Copy-Item -LiteralPath $solto[0].FullName -Destination $externos; continue }
    $backups = @(foreach ($pasta in @($guardado, $downloads)) {
        Get-ChildItem -LiteralPath $pasta -Filter 'STS2*.zip' -File -ErrorAction SilentlyContinue
    }) | Sort-Object LastWriteTime -Descending
    foreach ($backup in $backups) {
        $arquivo = $null
        try {
            $arquivo = [IO.Compression.ZipFile]::OpenRead($backup.FullName)
            $entrada = $arquivo.Entries | Where-Object {
                $_.Name -like $padrao -and $_.FullName -match '(^|/)externos/'
            } | Select-Object -First 1
            if ($entrada) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entrada, (Join-Path $externos $entrada.Name), $true)
                Write-Host "Recuperado do backup: $($entrada.Name)"
                break
            }
        } finally { if ($arquivo) { $arquivo.Dispose() } }
    }
    if (@(Get-ChildItem -LiteralPath $externos -Filter $padrao -File -ErrorAction SilentlyContinue).Count -eq 0) {
        throw "Arquivo necessario nao encontrado: $padrao. Copie do seu backup para $externos e execute novamente."
    }
}
if ($SomenteBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'REBUILD-APK.ps1') -UsePrebuilt
} else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'REBUILD-APK.ps1') -UsePrebuilt -Install
}
if ($LASTEXITCODE -ne 0) { throw 'Build/instalacao falhou. Confira o erro acima.' }
