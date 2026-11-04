[CmdletBinding()]
param(
    [string] $IdentityName = 'Yacplayer.OpenSource',
    [string] $Publisher = 'CN=Yacplayer Development, OID.2.25.311729368913984317654407730594956997722=1',
    [string] $PublisherDisplayName = 'Yacplayer contributors',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '1.26.5.11',
    [string] $OutputPath,
    [switch] $UseLocalStoreIdentity
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactsRoot = Join-Path $repoRoot 'artifacts\msix'
$publishRoot = Join-Path $artifactsRoot 'publish'
$layoutRoot = Join-Path $artifactsRoot 'layout'
$identityFile = Join-Path $PSScriptRoot 'StoreIdentity.local.psd1'

if ($UseLocalStoreIdentity) {
    if (-not (Test-Path -LiteralPath $identityFile)) {
        throw "Missing $identityFile. Copy StoreIdentity.example.psd1 and use the exact Partner Center values."
    }

    $identity = Import-PowerShellDataFile -LiteralPath $identityFile
    $IdentityName = $identity.IdentityName
    $Publisher = $identity.Publisher
    $PublisherDisplayName = $identity.PublisherDisplayName
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $artifactsRoot "Yacplayer-$Version-win-x64.msix"
}
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter MakeAppx.exe |
    Where-Object FullName -Match '\\x64\\MakeAppx\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $makeAppx) {
    throw 'MakeAppx.exe was not found. Install the Windows 10/11 SDK.'
}

New-Item -ItemType Directory -Force -Path $artifactsRoot, $publishRoot, $layoutRoot | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $layoutRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

& dotnet publish (Join-Path $repoRoot 'src\Yacplayer\Yacplayer.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item -Path (Join-Path $publishRoot '*') -Destination $layoutRoot -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $layoutRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $layoutRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'PRIVACY.md') -Destination $layoutRoot

[xml] $manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml')
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identityNode = $manifest.SelectSingleNode('/f:Package/f:Identity', $ns)
$identityNode.SetAttribute('Name', $IdentityName)
$identityNode.SetAttribute('Publisher', $Publisher)
$identityNode.SetAttribute('Version', $Version)
$manifest.SelectSingleNode('/f:Package/f:Properties/f:PublisherDisplayName', $ns).InnerText = $PublisherDisplayName
$manifest.Save((Join-Path $layoutRoot 'AppxManifest.xml'))

Add-Type -AssemblyName System.Drawing
$sourceLogo = Join-Path $repoRoot 'assets\Yacplayer_icon.png'
$assets = Join-Path $layoutRoot 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
function Write-Logo([string] $name, [int] $width, [int] $height) {
    $source = [System.Drawing.Image]::FromFile($sourceLogo)
    try {
        $bitmap = [System.Drawing.Bitmap]::new($width, $height)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $side = [Math]::Min($width, $height)
                $left = [int](($width - $side) / 2)
                $graphics.DrawImage($source, $left, 0, $side, $side)
                $bitmap.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose() }
        } finally { $bitmap.Dispose() }
    } finally { $source.Dispose() }
}
Write-Logo 'StoreLogo.png' 50 50
Write-Logo 'Square44x44Logo.png' 44 44
Write-Logo 'Square150x150Logo.png' 150 150
Write-Logo 'Wide310x150Logo.png' 310 150

if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
& $makeAppx pack /d $layoutRoot /p $OutputPath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }

Write-Host "Created unsigned MSIX: $OutputPath"
Write-Host 'Microsoft Store signs the accepted package. Do not commit certificates or Partner Center secrets.'
