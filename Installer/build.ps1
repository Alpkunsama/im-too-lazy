param(
    [Parameter(Mandatory)][string] $Compiler,
    [Parameter(Mandatory)][string] $WebViewInstaller,
    [string] $UpdateFeedUrl
)

$ErrorActionPreference = 'Stop'
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
$WebViewInstaller = (Resolve-Path -LiteralPath $WebViewInstaller).Path
$runtimeSignature = Get-AuthenticodeSignature -LiteralPath $WebViewInstaller
if ($runtimeSignature.Status -ne 'Valid' -or $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'WebView2 kurucusunun Microsoft imzası doğrulanamadı.'
}
if ($UpdateFeedUrl) {
    $feed = $UpdateFeedUrl -as [Uri]
    if (!$feed -or !$feed.IsAbsoluteUri -or $feed.Scheme -ne 'https' -or !$feed.Host -or $feed.UserInfo -or $UpdateFeedUrl -match '[;,]') {
        throw 'Güncelleme adresi, kimlik bilgisi içermeyen bir HTTPS URL olmalıdır.'
    }
}

$root = Split-Path $PSScriptRoot -Parent
$version = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'App/App.xaml.cs') -Raw), 'Current = "(\d+)\.(\d{4})"')
if (!$version.Success) { throw 'Sürüm numarası bulunamadı.' }
$appVersion = $version.Groups[1].Value + '.' + $version.Groups[2].Value
$fileVersion = $version.Groups[1].Value + '.0.0.' + [int]$version.Groups[2].Value
$versionProperties = @(
    '-p:Product=im too lazy'
    "-p:FileVersion=$fileVersion"
    "-p:InformationalVersion=$appVersion"
    '-p:IncludeSourceRevisionInInformationalVersion=false'
)

function Assert-ProductMetadata([string] $Path) {
    $info = (Get-Item -LiteralPath $Path).VersionInfo
    if ($info.ProductName.Trim() -cne 'im too lazy' -or $info.ProductVersion.Trim() -cne $appVersion -or
        $info.FileVersion.Trim() -cne $fileVersion) {
        throw "İmzalama için ürün adı veya sürüm bilgisi eşleşmiyor: $Path"
    }
}

# Only fresh build output goes into the installer, never the user's app data.
$stage = (New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('payload-build-' + [guid]::NewGuid().ToString('N')))).FullName
$payload = Join-Path $stage 'app'
$uninstaller = Join-Path $stage 'uninstaller'
Push-Location $root
try {
    Push-Location 'App/WebUI'
    try {
        node build.mjs
        if ($LASTEXITCODE) { throw 'Arayüz derlemesi başarısız.' }
    } finally { Pop-Location }

    dotnet publish App/VmixSubtitleController.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false @versionProperties "-p:UpdateFeedUrl=$UpdateFeedUrl" -o $payload --ignore-failed-sources
    if ($LASTEXITCODE) { throw 'Uygulama derlemesi başarısız.' }
    dotnet publish Installer/Uninstaller/Uninstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false @versionProperties -o $uninstaller --ignore-failed-sources
    if ($LASTEXITCODE) { throw 'Kaldırıcı derlemesi başarısız.' }
    foreach ($name in 'uninstall.exe','uninstall.dll','uninstall.deps.json','uninstall.runtimeconfig.json') {
        Copy-Item -LiteralPath (Join-Path $uninstaller $name) -Destination $payload
    }
    Assert-ProductMetadata (Join-Path $payload 'im too lazy.exe')
    Assert-ProductMetadata (Join-Path $payload 'uninstall.exe')
    $privateNames = 'account.bin','registration.bin','host-id.txt','welcome-shown','settings.json','profiles.txt','active-profile.txt'
    $privateFolders = 'chatgpt','chatgpt-login','ui-browser','translation-cache','translations','profiles'
    $privateFiles = Get-ChildItem -LiteralPath $payload -Force -Recurse | Where-Object {
        ($_.PSIsContainer -and $_.Name -in $privateFolders) -or
        (!$_.PSIsContainer -and ($_.Name -in $privateNames -or $_.Extension -in '.srt','.log','.pfx','.p12'))
    }
    if ($privateFiles) { throw 'Kurulum içeriğinde kullanıcı verisi olabilecek dosyalar bulundu.' }
    & $Compiler "/DMyPayloadDir=$payload" "/DMyAppVersion=$appVersion" "/DMyFileVersion=$fileVersion" "/DWebViewInstaller=$WebViewInstaller" (Join-Path $PSScriptRoot 'setup.iss')
    if ($LASTEXITCODE) { throw 'Setup derlemesi başarısız.' }
    $setup = Join-Path $PSScriptRoot "output/im-too-lazy-$appVersion-setup.exe"
    Assert-ProductMetadata $setup
    Get-FileHash -LiteralPath $setup -Algorithm SHA256
} finally {
    Pop-Location
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $stagePrefix = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\') + '\payload-build-'
    if ($resolvedStage.StartsWith($stagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
