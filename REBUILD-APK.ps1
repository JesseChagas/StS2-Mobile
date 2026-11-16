param([switch]$Install, [switch]$UsePrebuilt)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'APK-HELPERS.ps1')
$projeto = $PSScriptRoot
$downloads = Join-Path $env:USERPROFILE 'Downloads'
$original = Join-Path $downloads 'original.apk'
$marker = '[build=mobile-steam-p2p-v11]'
$commands = @('java')
if (-not $UsePrebuilt) { $commands += 'dotnet' }
if ($Install) { $commands += 'adb' }
foreach ($command in $commands) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Comando nao encontrado: $command" }
}
if (-not (Test-Path -LiteralPath $original)) { throw "APK nao encontrado: $original" }
if (-not ([IO.File]::ReadAllText((Join-Path $projeto 'src\STS2Mobile\ModEntry.cs')).Contains($marker))) {
    throw 'Os fontes nao sao da integracao Steam P2P v11. Extraia novamente o ZIP completo.'
}
function Localizar-Jar([string]$Filter) {
    $files = @(Get-ChildItem -LiteralPath $downloads -Filter $Filter -Recurse -File -ErrorAction SilentlyContinue)
    if ($files.Count -ne 1) {
        $files.FullName | Out-Host
        throw "Esperava um arquivo $Filter em Downloads; encontrei $($files.Count)."
    }
    return $files[0].FullName
}
$apktool = Localizar-Jar '*apktool*.jar'
$signer = Localizar-Jar '*uber-apk-signer*.jar'
Write-Host 'Compilando STS 2 Mobile com Steam P2P...' -ForegroundColor Cyan
if ($UsePrebuilt) {
    $dll = Join-Path $projeto 'prebuilt\STS2Mobile.dll'
    $prebuiltManifest = Get-Content -LiteralPath (Join-Path $projeto 'prebuilt\manifest.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $prebuiltManifest.sha256) {
        throw 'A DLL precompilada difere do manifesto. Extraia o ZIP completo.'
    }
    Write-Host 'Usando DLL precompilada e conferida; nao precisa das referencias locais do jogo.' -ForegroundColor Cyan
} else {
    & dotnet publish (Join-Path $projeto 'src\STS2Mobile\STS2Mobile.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao.' }
    $dll = Join-Path $projeto 'src\STS2Mobile\bin\Release\net9.0\publish\STS2Mobile.dll'
}
$dllHex = [BitConverter]::ToString([IO.File]::ReadAllBytes($dll))
$markerHex = [BitConverter]::ToString([Text.Encoding]::Unicode.GetBytes($marker))
if (-not $dllHex.Contains($markerHex)) { throw 'A DLL compilada nao contem o marcador da interface.' }
$pasta = Join-Path $downloads ('STS2-Mobile-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $pasta | Out-Null
$decoded = Join-Path $pasta 'extraido'
$apk = Join-Path $pasta 'STS2-Mobile-JesseChagas.apk'
$out = Join-Path $pasta 'assinado'
# Full decode is required: the Android cache decision is in compiled DEX/smali.
& java -jar $apktool d $original -o $decoded
if ($LASTEXITCODE -ne 0) { throw 'Falha ao decodificar APK com smali.' }
$smaliFiles = @(Get-ChildItem -LiteralPath $decoded -Filter 'GodotApp.smali' -Recurse -File |
    Where-Object { $_.FullName -match '[\\/]com[\\/]game[\\/]sts2launcher[\\/]GodotApp\.smali$' })
if ($smaliFiles.Count -ne 1) { throw 'GodotApp.smali nao identificado de forma unica.' }
Set-LauncherAssemblyRefresh $smaliFiles[0].FullName
Set-SteamNetworkingBridge $smaliFiles[0].FullName
Set-MobileIdentity $decoded $projeto
$bootstrap = Join-Path $projeto 'android\assets\bootstrap.pck'
if (Test-Path -LiteralPath $bootstrap) {
    Copy-Item -LiteralPath $bootstrap -Destination (Join-Path $decoded 'assets\bootstrap.pck') -Force
}
$versionCode = Set-ApkVersionCode (Join-Path $decoded 'apktool.yml')
Write-Host "Cache corrigido no DEX; versionCode=$versionCode" -ForegroundColor Cyan
$destinations = @(Get-ChildItem -LiteralPath (Join-Path $decoded 'assets') -Filter 'STS2Mobile.dll' -Recurse -File)
if ($destinations.Count -ne 1) { throw 'Esperava uma unica STS2Mobile.dll nos assets.' }
Copy-Item -LiteralPath $dll -Destination $destinations[0].FullName -Force
$dependencyCopies = @()
foreach ($dependencyName in @('SteamKit2.dll', 'protobuf-net.dll', 'protobuf-net.Core.dll', 'System.IO.Hashing.dll', 'ZstdSharp.dll')) {
    $dependency = Join-Path (Split-Path -Parent $dll) $dependencyName
    if (-not (Test-Path -LiteralPath $dependency)) { throw "Dependencia ausente: $dependencyName" }
    if ($UsePrebuilt) {
        $expected = @($prebuiltManifest.dependencies | Where-Object { $_.name -eq $dependencyName })
        if ($expected.Count -ne 1 -or (Get-FileHash -LiteralPath $dependency -Algorithm SHA256).Hash -ne $expected[0].sha256) {
            throw "Dependencia diferente do manifesto: $dependencyName"
        }
    }
    $dependencyDestination = Join-Path $destinations[0].DirectoryName $dependencyName
    Copy-Item -LiteralPath $dependency -Destination $dependencyDestination -Force
    $dependencyCopies += @{ Source = $dependency; Asset = $dependencyDestination.Substring($decoded.Length + 1).Replace('\', '/') }
}
$native = Join-Path $projeto 'native\steam-p2p\arm64-v8a\libsts2_gns.so'
if (-not (Test-Path -LiteralPath $native)) { throw 'Biblioteca Steam P2P ARM64 ausente. Extraia o ZIP completo.' }
if ((Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ne 'C8E0D143B967F2510F8EFD56C2D5E9F1FA6032C06DC25799B1EF6EBC3D7B8F5F') {
    throw 'Biblioteca Steam P2P ARM64 diferente da revisao validada.'
}
$nativeDir = Join-Path $decoded 'lib\arm64-v8a'
New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null
Copy-Item -LiteralPath $native -Destination (Join-Path $nativeDir 'libsts2_gns.so') -Force
# Extract native libraries at install time, including on 16 KB page devices.
# The ELF is 16 KB aligned; extraction avoids depending on APK entry offsets.
$manifestPath = Join-Path $decoded 'AndroidManifest.xml'
$androidManifest = New-Object System.Xml.XmlDocument
$androidManifest.Load($manifestPath)
$application = $androidManifest.SelectSingleNode('/manifest/application')
if (-not $application) { throw 'Manifesto Android sem application.' }
$extractAttribute = $androidManifest.CreateAttribute('android', 'extractNativeLibs', 'http://schemas.android.com/apk/res/android')
$extractAttribute.Value = 'true'
$application.SetAttributeNode($extractAttribute) | Out-Null
$androidManifest.Save($manifestPath)
$assetPath = $destinations[0].FullName.Substring($decoded.Length + 1).Replace('\', '/')
& java -jar $apktool b $decoded -o $apk
if ($LASTEXITCODE -ne 0) { throw 'Falha ao reconstruir APK.' }
& java -jar $signer -a $apk --allowResign --out $out
if ($LASTEXITCODE -ne 0) { throw 'Falha ao assinar/verificar APK.' }
$signed = @(Get-ChildItem -LiteralPath $out -Filter '*.apk' -File)
if ($signed.Count -ne 1) { throw 'Esperava um APK assinado.' }
if ((Get-ApkDllHash $signed[0].FullName $assetPath) -ne (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash) {
    throw 'A DLL dentro do APK assinado difere da compilada.'
}
if ((Get-ApkDllHash $signed[0].FullName 'lib/arm64-v8a/libsts2_gns.so') -ne (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash) {
    throw 'A biblioteca P2P no APK assinado difere da revisao validada.'
}
foreach ($dependency in $dependencyCopies) {
    if ((Get-ApkDllHash $signed[0].FullName $dependency.Asset) -ne (Get-FileHash -LiteralPath $dependency.Source -Algorithm SHA256).Hash) {
        throw "Dependencia incorreta no APK assinado: $($dependency.Asset)"
    }
}
Write-Host "APK validado: $($signed[0].FullName)" -ForegroundColor Green
if ($Install) {
    $deviceLines = @(& adb devices)
    if ($LASTEXITCODE -ne 0 -or @($deviceLines | Where-Object { $_ -match '^\S+\s+device$' }).Count -ne 1) {
        throw 'Conecte exatamente um celular com depuracao USB autorizada.'
    }
    & adb shell am force-stop com.game.sts2launcher
    & adb install -r $signed[0].FullName
    if ($LASTEXITCODE -ne 0) { throw 'A instalacao falhou. Nao desinstale o app.' }
    $installed = (& adb shell dumpsys package com.game.sts2launcher) -join "`n"
    if ($installed -notmatch ("versionCode=" + $versionCode + '(?:\s|$)')) {
        throw 'A versao instalada nao corresponde ao APK criado.'
    }
    & adb logcat -c
    $logPath = Join-Path $pasta 'startup-log.txt'
    $errPath = Join-Path $pasta 'logcat-errors.txt'
    $adbPath = (Get-Command adb).Source
    $logger = Start-Process -FilePath $adbPath -ArgumentList @('logcat', '-v', 'threadtime', 'godot:V', 'DOTNET:V', 'STS2Mobile:V', 'AndroidRuntime:V', 'libc:V', 'DEBUG:V', 'mono:V', 'Mono:V', 'System.err:V', '*:S') -RedirectStandardOutput $logPath -RedirectStandardError $errPath -NoNewWindow -PassThru
    try {
        & adb shell am start -n com.game.sts2launcher/com.game.sts2launcher.GodotApp
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao abrir o launcher.' }
        $loaded = $false
        for ($i = 0; $i -lt 45; $i++) {
            Start-Sleep -Seconds 1
            if (Test-Path -LiteralPath $logPath) {
                $text = Read-SharedLog $logPath
                if ($text.Contains($marker) -and $text.Contains('[AssemblyRefresh-v4]') -and $text.Contains('[UI] Interface ready [mobile-steam-ui-v8]')) {
                    $loaded = $true
                    break
                }
            }
        }
        if (-not $loaded) { throw "A abertura do launcher Steam P2P v11 nao foi confirmada em execucao. Log: $logPath" }
        Write-Host 'CONFIRMADO: STS 2 Mobile instalado, interface nova confirmada.' -ForegroundColor Green
        Write-Host 'Agora inicie o jogo pelo launcher. Pressione Enter aqui depois do resultado.'
        Read-Host | Out-Null
    } finally {
        if (-not $logger.HasExited) { Stop-Process -Id $logger.Id -ErrorAction SilentlyContinue }
        Write-Host "Log salvo: $logPath"
    }
}
Start-Process explorer.exe -ArgumentList "`"$pasta`""
