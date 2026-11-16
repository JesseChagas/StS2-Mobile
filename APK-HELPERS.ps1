function Set-LauncherAssemblyRefresh([string]$SmaliPath) {
    $text = [IO.File]::ReadAllText($SmaliPath)
    $pattern = '(?ms)^\.method[^\r\n]*\bisNewVersion\(\)Z[^\r\n]*\r?\n.*?^\.end method[ \t]*\r?$'
    if ([regex]::Matches($text, $pattern).Count -ne 1) {
        throw 'Nao foi encontrado exatamente um metodo isNewVersion()Z em GodotApp.smali.'
    }
    $replacement = @'
.method private isNewVersion()Z
    .locals 2

    const-string v0, "STS2Mobile"
    const-string v1, "[AssemblyRefresh-v4] refreshing DLLs from APK assets"
    invoke-static {v0, v1}, Landroid/util/Log;->i(Ljava/lang/String;Ljava/lang/String;)I
    const/4 v0, 0x1
    return v0
.end method
'@
    $text = [regex]::Replace($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement })
    [IO.File]::WriteAllText($SmaliPath, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Set-SteamNetworkingBridge([string]$SmaliPath) {
    $text = [IO.File]::ReadAllText($SmaliPath)
    $pattern = '(?ms)^\.method[^\r\n]*\bgetSteamNetworkingLibraryPath\(\)Ljava/lang/String;[^\r\n]*\r?\n.*?^\.end method[ \t]*\r?$'
    $method = @'
.method public getSteamNetworkingLibraryPath()Ljava/lang/String;
    .locals 3

    invoke-virtual {p0}, Lcom/game/sts2launcher/GodotApp;->getApplicationInfo()Landroid/content/pm/ApplicationInfo;
    move-result-object v0
    iget-object v0, v0, Landroid/content/pm/ApplicationInfo;->nativeLibraryDir:Ljava/lang/String;
    new-instance v1, Ljava/io/File;
    const-string v2, "libsts2_gns.so"
    invoke-direct {v1, v0, v2}, Ljava/io/File;-><init>(Ljava/lang/String;Ljava/lang/String;)V
    invoke-virtual {v1}, Ljava/io/File;->getAbsolutePath()Ljava/lang/String;
    move-result-object v0
    return-object v0
.end method
'@
    $count = [regex]::Matches($text, $pattern).Count
    if ($count -gt 1) { throw 'Metodo de biblioteca Steam duplicado no APK.' }
    if ($count -eq 1) {
        $text = [regex]::Replace($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $method })
    } else { $text += "`n`n" + $method + "`n" }
    [IO.File]::WriteAllText($SmaliPath, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Set-ApkVersionCode([string]$YamlPath) {
    $text = [IO.File]::ReadAllText($YamlPath)
    $pattern = '(?m)^([ \t]*versionCode:[ \t]*)([^\r\n]+)'
    $found = [regex]::Matches($text, $pattern)
    if ($found.Count -ne 1) { throw 'versionCode nao identificado em apktool.yml.' }
    $oldCode = [long]($found[0].Groups[2].Value.Trim().Trim("'").Trim('"'))
    $versionCode = [Math]::Max($oldCode + 1, [DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
    if ($versionCode -gt 2100000000) { throw 'versionCode excede o limite do Android.' }
    $text = [regex]::Replace($text, $pattern, ('${1}' + "'$versionCode'"))
    [IO.File]::WriteAllText($YamlPath, $text, (New-Object System.Text.UTF8Encoding($false)))
    return $versionCode
}

function Get-ApkDllHash([string]$ApkPath, [string]$AssetPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($ApkPath)
    try {
        $entry = $zip.GetEntry($AssetPath)
        if ($null -eq $entry) { throw 'DLL nao encontrada no APK assinado.' }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
    } finally { $zip.Dispose() }
}

function Read-SharedLog([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = $null
    try {
        $reader = New-Object IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } finally {
        if ($null -ne $reader) { $reader.Dispose() } else { $stream.Dispose() }
    }
}

function Set-MobileIdentity([string]$DecodedPath, [string]$ProjectPath) {
    $android = 'http://schemas.android.com/apk/res/android'
    $manifestPath = Join-Path $DecodedPath 'AndroidManifest.xml'
    $xml = New-Object Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($manifestPath)
    $application = $xml.SelectSingleNode('/manifest/application')
    if ($null -eq $application) { throw 'Application nao encontrada no manifesto.' }
    $label = 'STS 2 Mobile (unofficial) by Jessé chagas'
    $application.SetAttribute('label', $android, $label) | Out-Null
    $application.SetAttribute('icon', $android, '@drawable/sts2_mobile_icon') | Out-Null
    if ($application.HasAttribute('roundIcon', $android)) { $application.SetAttribute('roundIcon', $android, '@drawable/sts2_mobile_icon') | Out-Null }
    foreach ($activity in $application.SelectNodes('activity')) {
        if ($activity.GetAttribute('name', $android).EndsWith('GodotApp')) {
            $activity.SetAttribute('label', $android, $label) | Out-Null
            if ($activity.HasAttribute('icon', $android)) { $activity.SetAttribute('icon', $android, '@drawable/sts2_mobile_icon') | Out-Null }
        }
    }
    $writerSettings = New-Object Xml.XmlWriterSettings
    $writerSettings.Encoding = New-Object Text.UTF8Encoding($false)
    $writerSettings.Indent = $true
    $writer = [Xml.XmlWriter]::Create($manifestPath, $writerSettings)
    try { $xml.Save($writer) } finally { $writer.Dispose() }
    $drawable = Join-Path $DecodedPath 'res\drawable'
    New-Item -ItemType Directory -Path $drawable -Force | Out-Null
    foreach ($name in @('sts2_mobile_icon.xml','sts2_mobile_empty.xml')) {
        Copy-Item -LiteralPath (Join-Path $ProjectPath "android\res\drawable\$name") -Destination (Join-Path $drawable $name) -Force
    }
    Remove-Item -LiteralPath (Join-Path $drawable 'sts2_mobile_icon.png') -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $ProjectPath 'android\res\drawable\sts2_mobile_art.png') -Destination (Join-Path $drawable 'sts2_mobile_art.png') -Force
    foreach ($stylePath in (Get-ChildItem -LiteralPath (Join-Path $DecodedPath 'res') -Recurse -File -Filter '*.xml' | Where-Object { $_.Directory.Name -like 'values*' })) {
        $styleText = [IO.File]::ReadAllText($stylePath.FullName)
        if ($styleText -notmatch 'windowSplashScreen') { continue }
        $styleText = [regex]::Replace($styleText, '(<item\s+name="(?:android:)?windowSplashScreenAnimatedIcon"[^>]*>)[^<]*(</item>)', '${1}@drawable/sts2_mobile_empty${2}')
        $styleText = [regex]::Replace($styleText, '(<item\s+name="(?:android:)?windowSplashScreenBackground"[^>]*>)[^<]*(</item>)', '${1}#1b2838${2}')
        [IO.File]::WriteAllText($stylePath.FullName, $styleText, (New-Object Text.UTF8Encoding($false)))
    }
}
