#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the standalone Icewolf setup.exe (engine: LibreWolf).

    1. Asks LibreWolf's official release feed for the newest version.
    2. Downloads LibreWolf's signed Windows portable build (HTTPS) and refuses to
       continue unless
         - its SHA256 matches the published checksum,
         - librewolf.exe, xul.dll and every .exe are signed by LibreWolf's publisher
           (OSSign / Scheibling Consulting AB), and
         - no file has a broken/tampered signature.
    3. Compiles the Icewolf setup with the C# compiler built into Windows
       (.NET Framework 4.x) - no extra software needed -> dist\IcewolfSetup-<version>.exe

.PARAMETER Version
    Optional: build a specific LibreWolf version, e.g. 161.0-1. Default: newest.
#>
[CmdletBinding()]
param([string]$Version = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$Root  = $PSScriptRoot
$Build = Join-Path $Root 'build'
$Dist  = Join-Path $Root 'dist'
$ExpectedSigner = 'Scheibling Consulting AB'
$ReleaseApi     = 'https://librewolf.dev/api/v1/repos/librewolf/bsys6/releases/latest'
$DownloadBase   = 'https://dl.librewolf.net/librewolf/'

function Step([string]$m) { Write-Host "[build] $m" -ForegroundColor Cyan }
function Fail([string]$m) {
    Write-Host "[build] ERROR: $m" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

try { Start-Transcript -LiteralPath (Join-Path $Root 'build-log.txt') -Force | Out-Null } catch { }
trap {
    Write-Host "[build] ERROR: $($_.Exception.Message)" -ForegroundColor Red
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}
Write-Host "[build] Windows $([Environment]::OSVersion.Version), PowerShell $($PSVersionTable.PSVersion)"

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# ------------------------------------------------------------ C# compiler (built into Windows)
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $fw 'csc.exe'
$wpf  = Join-Path $fw 'WPF'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $wpf 'PresentationFramework.dll'))) {
    Fail ".NET Framework 4.x compiler not found at $csc. It is part of Windows 10/11 - run Windows Update."
}
Step "Using built-in compiler: $csc"

# ------------------------------------------------------------ clean build folder
if (Test-Path -LiteralPath $Build) { Remove-Item -LiteralPath $Build -Recurse -Force }
New-Item -ItemType Directory -Path $Build | Out-Null
New-Item -ItemType Directory -Path $Dist -Force | Out-Null

# ------------------------------------------------------------ newest LibreWolf version
if (-not $Version) {
    Step 'Asking LibreWolf for the newest release...'
    $release = Invoke-RestMethod -Uri $ReleaseApi -UseBasicParsing -Headers @{ 'User-Agent' = 'Icewolf-Build' }
    $Version = [string]$release.tag_name
}
if ($Version -notmatch '^\d{1,4}(\.\d{1,4}){0,3}-\d{1,3}$') { Fail "Unexpected LibreWolf version: '$Version'" }
Step "LibreWolf version $Version"

# ------------------------------------------------------------ download + checksum
$file = "librewolf-$Version-windows-x86_64-portable.zip"
$zip  = Join-Path $Build 'engine.zip'
$sum  = Join-Path $Build 'engine.zip.sha256sum'
Step "Downloading $file ..."
Invoke-WebRequest -Uri ($DownloadBase + "$Version/$file") -OutFile $zip -UseBasicParsing
Invoke-WebRequest -Uri ($DownloadBase + "$Version/$file.sha256sum") -OutFile $sum -UseBasicParsing

$expected = ((Get-Content -LiteralPath $sum -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($expected -notmatch '^[0-9a-f]{64}$') { Fail 'The published checksum file is not valid.' }
$actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    Remove-Item -LiteralPath $zip -Force
    Fail "Checksum mismatch - download deleted.`n        expected $expected`n        got      $actual"
}
Step 'Checksum OK'

# ------------------------------------------------------------ digital signatures
Step 'Verifying digital signatures of every program file...'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$check = Join-Path $Build 'verify'
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $check)
$exe = Get-ChildItem -LiteralPath $check -Recurse -Filter 'librewolf.exe' | Select-Object -First 1
if (-not $exe) { Fail 'librewolf.exe not found in the LibreWolf package.' }
$pkg = $exe.Directory.FullName

$bad = @(); $unsigned = @()
Get-ChildItem -LiteralPath $pkg -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' } | ForEach-Object {
    $s = Get-AuthenticodeSignature -LiteralPath $_.FullName
    $isMain = ($_.Extension -eq '.exe') -or ($_.Name -eq 'xul.dll')
    if ($s.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        if ($isMain -and $s.SignerCertificate.Subject -notlike "*$ExpectedSigner*") {
            $bad += "$($_.Name): signed by '$($s.SignerCertificate.Subject)'"
        }
    } elseif ($s.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned -and -not $isMain) {
        $unsigned += $_.Name
    } else {
        $bad += "$($_.Name): $($s.Status)"
    }
}
if ($bad.Count -gt 0) { Fail ("Signature check failed:`n        " + ($bad -join "`n        ")) }
if ($unsigned.Count -gt 0) {
    Write-Host "[build] Note: unsigned helper libraries (protected by the SHA256 checksum): $($unsigned -join ', ')" -ForegroundColor Yellow
}
$subject = (Get-AuthenticodeSignature -LiteralPath (Join-Path $pkg 'librewolf.exe')).SignerCertificate.Subject
Step "Main program files signed by: $subject"

# ------------------------------------------------------------ assets + version info
$cfg = Join-Path $Root 'icewolf\config'
foreach ($f in 'policies.json', 'user.js', 'userChrome.css', 'userContent.css', 'librewolf.overrides.cfg') {
    Copy-Item -LiteralPath (Join-Path $cfg $f) -Destination (Join-Path $Build $f)
}
Copy-Item -LiteralPath (Join-Path $Root 'setup\assets\icewolf.ico') -Destination (Join-Path $Build 'icewolf.ico')
Copy-Item -LiteralPath (Join-Path $Root 'setup\assets\side.png')    -Destination (Join-Path $Build 'side.png')
$null = Get-Content -LiteralPath (Join-Path $Build 'policies.json') -Raw | ConvertFrom-Json   # must be valid JSON

# ------------------------------------------------------------ Getem (only in the "with Getem" project)
# Project Icewolf (main repository) contains no Getem files and builds the two plain versions.
# The Getem edition (separate repository) contains them and builds all four versions.
$WithGetem = $true
foreach ($need in 'icewolf\getem\app.py', 'icewolf\getem-tab\server.py', 'setup\GetemHost.cs') {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $need))) { $WithGetem = $false }
}
if ($WithGetem) {
Step 'Packaging Getem...'
# Getem with tab support (local server + one-click button) - the same for installer and portable
$getemSrc = Join-Path $Build 'getem-src'
if (Test-Path -LiteralPath $getemSrc) { Remove-Item -LiteralPath $getemSrc -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $Root 'icewolf\getem') -Destination $getemSrc -Recurse
Copy-Item -Path (Join-Path $Root 'icewolf\getem-tab\*') -Destination $getemSrc -Force
$getemZip = Join-Path $Build 'getem.zip'
if (Test-Path -LiteralPath $getemZip) { Remove-Item -LiteralPath $getemZip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($getemSrc, $getemZip, [IO.Compression.CompressionLevel]::Optimal, $false)
} else {
    Step 'No Getem files in this project - building Icewolf without Getem.'
}


$main4 = @(($Version -split '-')[0].Split('.')) + @('0', '0', '0')
$ver4  = "$($main4[0]).$($main4[1]).$($main4[2]).$(($Version -split '-')[1])"
@"
using System.Reflection;
[assembly: AssemblyTitle("Icewolf Setup")]
[assembly: AssemblyDescription("Icewolf Setup")]
[assembly: AssemblyProduct("Icewolf")]
[assembly: AssemblyCompany("Icewolf Project")]
[assembly: AssemblyCopyright("Engine: LibreWolf (MPL 2.0)")]
[assembly: AssemblyVersion("$ver4")]
[assembly: AssemblyFileVersion("$ver4")]
[assembly: AssemblyInformationalVersion("$Version")]
namespace Icewolf.Setup { internal static class BuildInfo { public const string EngineVersion = "$Version"; } }
"@ | Set-Content -LiteralPath (Join-Path $Build 'Version.cs') -Encoding UTF8

# ------------------------------------------------------------ compile
Push-Location $Root
try {
    $base = @(
        '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', '/utf8output',
        '/win32icon:build\icewolf.ico',
        "/lib:$wpf",
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Xaml.dll',
        '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll', '/r:System.Web.Extensions.dll',
        '/r:WindowsBase.dll', '/r:PresentationCore.dll', '/r:PresentationFramework.dll',
        '/resource:build\icewolf.ico,icewolf.ico',
        '/resource:build\side.png,side.png',
        '/resource:setup\assets\logo512.png,logo512.png',
        '/resource:setup\assets\logo.svg,logo.svg',
        '/resource:setup\assets\wordmark.svg,wordmark.svg',
        '/resource:setup\icewolf-core.js,icewolf-core.js'
    )
    if ($WithGetem) {
        $base += @('/resource:setup\getem-button.js,getem-button.js', '/resource:setup\getem-tab.js,getem-tab.js')
    }
    $common   = $base + @('/win32manifest:setup\app.manifest')
    $portable = $base + @('/win32manifest:setup\portable.manifest', '/define:PORTABLE')
    $sources = @('setup\IcewolfSetup.cs', 'build\Version.cs')

    Step 'Compiling uninstaller / updater...'
    & $csc @common '/out:build\Icewolf-Uninstall.exe' @sources | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { Fail "Compiling the uninstaller failed (exit code $LASTEXITCODE). See messages above." }

    if ($WithGetem) {
    Step 'Compiling Getem helper...'
    & $csc '/nologo' '/target:winexe' '/platform:x64' '/optimize+' '/utf8output' '/win32icon:setup\assets\getem.ico' `
        '/out:build\GetemHost.exe' 'setup\GetemHost.cs' | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { Fail "Compiling the Getem helper failed (exit code $LASTEXITCODE). See messages above." }
    }

    $engineRes = @(
        '/resource:build\engine.zip,engine.zip',
        '/resource:build\policies.json,policies.json',
        '/resource:build\user.js,user.js',
        '/resource:build\userChrome.css,userChrome.css',
        '/resource:build\userContent.css,userContent.css',
        '/resource:build\librewolf.overrides.cfg,librewolf.overrides.cfg',
        '/resource:build\Icewolf-Uninstall.exe,Uninstaller.exe'
    )
    $getemRes = @('/resource:build\getem.zip,getem.zip', '/resource:build\GetemHost.exe,GetemHost.exe')

    Step 'Compiling setup.exe (Icewolf)...'
    & $csc @common @engineRes "/out:dist\IcewolfSetup-$Version.exe" @sources | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { Fail "Compiling the setup failed (exit code $LASTEXITCODE). See messages above." }

    if ($WithGetem) {
    Step 'Compiling setup.exe (Icewolf with Getem)...'
    & $csc @common @engineRes @getemRes "/out:dist\IcewolfSetup-Getem-$Version.exe" @sources | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { Fail "Compiling the setup with Getem failed (exit code $LASTEXITCODE). See messages above." }
    }

    Step 'Compiling portable launcher...'
    & $csc @portable '/out:build\Icewolf.exe' @sources | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { Fail "Compiling the portable launcher failed (exit code $LASTEXITCODE). See messages above." }
}
finally { Pop-Location }

# ------------------------------------------------------------ portable version
Step 'Assembling Icewolf Portable...'
$pRoot = Join-Path $Build 'portable'
$pd    = Join-Path $pRoot 'Icewolf'
$prof  = Join-Path $pd 'profile'
$icons = Join-Path $prof 'chrome\icons\default'
New-Item -ItemType Directory -Path $icons -Force | Out-Null
Copy-Item -LiteralPath $pkg -Destination (Join-Path $pd 'engine') -Recurse
Copy-Item -LiteralPath (Join-Path $Build 'Icewolf.exe')            -Destination $pd
Copy-Item -LiteralPath (Join-Path $Build 'policies.json')          -Destination (Join-Path $pd 'icewolf-policies.json')
Copy-Item -LiteralPath (Join-Path $Build 'user.js')                -Destination $prof
Copy-Item -LiteralPath (Join-Path $Build 'librewolf.overrides.cfg') -Destination $prof
Copy-Item -LiteralPath (Join-Path $Build 'userChrome.css')         -Destination (Join-Path $prof 'chrome')
Copy-Item -LiteralPath (Join-Path $Build 'userContent.css')        -Destination (Join-Path $prof 'chrome')
Copy-Item -LiteralPath (Join-Path $Build 'icewolf.ico')            -Destination (Join-Path $icons 'main-window.ico')
Copy-Item -LiteralPath (Join-Path $Build 'icewolf.ico')            -Destination (Join-Path $icons 'default.ico')
Set-Content -LiteralPath (Join-Path $pd 'engine-version.txt') -Value $Version -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $Root 'setup\README-Portable.txt') -Destination $pd

Step 'Branding the portable engine (program icon, program name, interface texts)...'
$brand = Start-Process -FilePath (Join-Path $pd 'Icewolf.exe') -ArgumentList '/brand-engine', ('"' + (Join-Path $pd 'engine') + '"') -Wait -PassThru
if ($brand.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $pd 'engine\icewolf.exe'))) { Fail 'Branding the portable engine failed.' }
$brandLog = Join-Path $pd 'branding-log.txt'
if (Test-Path -LiteralPath $brandLog) {
    Get-Content -LiteralPath $brandLog | ForEach-Object { Write-Host "[branding] $_" }
    Remove-Item -LiteralPath $brandLog -Force      # contains paths of this PC - not shipped
}

function New-PortableZip([string]$name) {
    $zipPath = Join-Path $Dist $name
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($pRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $h = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$h  $name" -Encoding ASCII
    return $zipPath
}

Step 'Packing Icewolf Portable...'
$pZip = New-PortableZip "IcewolfPortable-$Version.zip"

if ($WithGetem) {
Step 'Packing Icewolf Portable with Getem (Getem inside a tab + one-click button)...'
Copy-Item -LiteralPath $getemSrc -Destination (Join-Path $pd 'getem') -Recurse
New-Item -ItemType Directory -Path (Join-Path $pd 'getem-host') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $Build 'GetemHost.exe') -Destination (Join-Path $pd 'getem-host')
$gt = Start-Process -FilePath (Join-Path $pd 'Icewolf.exe') -ArgumentList '/install-getem-tab', ('"' + (Join-Path $pd 'engine') + '"') -Wait -PassThru
if ($gt.ExitCode -ne 0) { Fail 'Adding the Getem features to the portable engine failed.' }
if (Test-Path -LiteralPath $brandLog) {
    Get-Content -LiteralPath $brandLog | ForEach-Object { Write-Host "[branding] $_" }
    Remove-Item -LiteralPath $brandLog -Force
}
$pZipG = New-PortableZip "IcewolfPortable-Getem-$Version.zip"
}
# left over from older builds (the test fork is now the regular portable Getem version)
Get-ChildItem -LiteralPath $Dist -Filter 'IcewolfPortable-GetemTab-*' -ErrorAction SilentlyContinue | Remove-Item -Force

Remove-Item -LiteralPath $check -Recurse -Force
Remove-Item -LiteralPath $pRoot -Recurse -Force

$setups = @("IcewolfSetup-$Version.exe")
if ($WithGetem) { $setups += "IcewolfSetup-Getem-$Version.exe" }
foreach ($f in $setups) {
    $o = Join-Path $Dist $f
    if (-not (Test-Path -LiteralPath $o -PathType Leaf)) { Fail "Expected output not found: $o" }
    $h = (Get-FileHash -LiteralPath $o -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$o.sha256" -Value "$h  $f" -Encoding ASCII
}

if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "version=$Version" }

Write-Host ''
Write-Host '  Done - in the "dist" folder:' -ForegroundColor Green
Write-Host "    IcewolfSetup-$Version.exe            installer"
Write-Host "    IcewolfPortable-$Version.zip         portable"
if ($WithGetem) {
Write-Host "    IcewolfSetup-Getem-$Version.exe      installer with Getem"
Write-Host "    IcewolfPortable-Getem-$Version.zip   portable with Getem (in a tab + one-click button)"
}
Write-Host ''
try { Stop-Transcript | Out-Null } catch { }
exit 0
