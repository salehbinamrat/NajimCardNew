param(
    [ValidateSet('x86','x64')][string]$Arch,
    [string]$Version = '0.1.0',
    [string]$Output = 'out'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
$rid = if ($Arch -eq 'x86') {'win-x86'} else {'win-x64'}
$outRoot = Join-Path $root $Output
$publishDir = Join-Path $outRoot "publish-$Arch"
# Delete only the architecture-specific, generated publish directory.
# Otherwise a previous release may silently inject obsolete files into the
# next portable ZIP and installer even after the source was removed.
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# Preserve a fully self-contained WPF runtime and reflection-based components.
# Restrict only .NET/NuGet *satellite translations* to Arabic and English:
# program UI is Arabic, and fallback English is embedded in base assemblies.
# Do not use PublishTrimmed, single-file extraction or invariant globalization.
# Symbols stay in CI build output, not in end-user installers/portable ZIPs.
dotnet publish src/NajmCard.Desktop/NajmCard.Desktop.csproj --configuration Release --runtime $rid --self-contained true --output $publishDir -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false '-p:SatelliteResourceLanguages=ar%3Ben'
if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }

# Never ship debug symbol files in public packages. This does not remove any
# executable/native/managed DLL or change the application's runtime logic.
Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -File -Recurse |
    Remove-Item -Force

$exe = Join-Path $publishDir 'NajmCard.exe'
if (!(Test-Path $exe)) { throw 'Published EXE missing' }
$sourceIcon=Join-Path $root 'src/NajmCard.Desktop/Assets/NajmCard.ico'
if (!(Test-Path $sourceIcon)) {throw 'NajmCard.ico missing before packaging'}
# Verify Win32 icon is embedded inside the executable, not only in the installer.
Add-Type -AssemblyName System.Drawing
$embedded=[System.Drawing.Icon]::ExtractAssociatedIcon($exe)
if ($null -eq $embedded) {throw "Embedded Windows icon missing in $exe"}
$embedded.Dispose()

# PE header check protects against accidentally labelling x64 packages as x86.
$bytes = [System.IO.File]::ReadAllBytes($exe)
if ($bytes.Length -lt 256 -or $bytes[0] -ne 77 -or $bytes[1] -ne 90) { throw 'Invalid Portable Executable' }
$offset = [BitConverter]::ToInt32($bytes, 0x3c)
if ($offset -lt 64 -or $offset -gt $bytes.Length-6 -or $bytes[$offset] -ne 80 -or $bytes[$offset+1] -ne 69) { throw 'Invalid PE header' }
$machine = [BitConverter]::ToUInt16($bytes, $offset+4)
$expected = if ($Arch -eq 'x86') { 0x14c } else { 0x8664 }
if ($machine -ne $expected) { throw "PE architecture mismatch: $machine != $expected" }

# Keep the complete self-contained WPF runtime and native SQLite assemblies
# side by side in App/. Do NOT use single-file publish or move dependencies
# independently: PDF, reflection, SQLite and WPF must retain their assembly
# probing and runtime config. The ZIP root exposes only a small native EXE.
$portableDir = Join-Path $outRoot "portable-$Arch"
if (Test-Path -LiteralPath $portableDir) {
    Remove-Item -LiteralPath $portableDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
Move-Item -LiteralPath $publishDir -Destination (Join-Path $portableDir 'App')

# Build an architecture-specific, /MT static Win32 launcher, with the real
# NajmCard.ico embedded into the launcher itself. No extra DLL is necessary
# beside it and no .cmd/.ps1 dependency is placed in the customer's ZIP.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) {
    throw "VS C++ build tools missing (vswhere): $vswhere"
}
$install = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($install)) { throw 'Visual C++ x86/x64 tools not installed' }
$vcvars = Join-Path $install.Trim() 'VC/Auxiliary/Build/vcvarsall.bat'
if (!(Test-Path -LiteralPath $vcvars)) { throw "vcvarsall.bat missing: $vcvars" }
$buildDir = Join-Path $outRoot "launcher-build-$Arch"
if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$rcFile = Join-Path $buildDir 'launcher.rc'
$resFile = Join-Path $buildDir 'launcher.res'
$cppFile = Join-Path $root 'scripts/portable-launcher.cpp'
$launcher = Join-Path $portableDir "NajmCard.exe"
$iconForResource = $sourceIcon.Replace('\', '/')
Set-Content -LiteralPath $rcFile -Encoding ASCII -Value "1 ICON `"$iconForResource`""
$machineName = if ($Arch -eq 'x86') {'X86'} else {'X64'}
$vcTarget = if ($Arch -eq 'x86') {'x64_x86'} else {'x64'}
$object = Join-Path $buildDir 'portable-launcher.obj'
$buildCmd = Join-Path $buildDir 'compile.cmd'
@"
@echo off
call "$vcvars" $vcTarget >nul
if errorlevel 1 exit /b 1
rc.exe /nologo /fo "$resFile" "$rcFile"
if errorlevel 1 exit /b 1
cl.exe /nologo /utf-8 /std:c++17 /O2 /EHsc /MT /DUNICODE /D_UNICODE /Fo"$object" "$cppFile" "$resFile" /link /SUBSYSTEM:WINDOWS /MACHINE:$machineName /OUT:"$launcher"
if errorlevel 1 exit /b 1
"@ | Set-Content -LiteralPath $buildCmd -Encoding ASCII
& cmd.exe /d /c "`"$buildCmd`""
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $launcher)) {
    throw "Native Portable launcher compilation failed for $Arch"
}

function Test-PortableLayout([string]$folder) {
    $publicFiles = @(Get-ChildItem -LiteralPath $folder -File)
    $publicDirs = @(Get-ChildItem -LiteralPath $folder -Directory)
    if ($publicFiles.Count -ne 1 -or $publicFiles[0].Name -ne 'NajmCard.exe' -or
        $publicDirs.Count -ne 1 -or $publicDirs[0].Name -ne 'App') {
        throw 'Portable root must contain exactly NajmCard.exe and App folder'
    }
    $appExe = Join-Path (Join-Path $folder 'App') 'NajmCard.exe'
    if (!(Test-Path -LiteralPath $appExe)) {throw 'App/NajmCard.exe missing'}
    # Exact layout validation executes native launcher code without starting
    # the WPF login screen or accessing any real router/user data.
    & (Join-Path $folder "NajmCard.exe") --verify-layout
    if ($LASTEXITCODE -ne 0) {
        throw "Native launcher failed layout smoke for $Arch (exit $LASTEXITCODE)"
    }
    # Confirm both public and internal binaries are the requested PE machine.
    foreach ($path in @((Join-Path $folder "NajmCard.exe"),$appExe)) {
        $header = [System.IO.File]::ReadAllBytes($path)
        if ($header.Length -lt 256 -or $header[0] -ne 77 -or $header[1] -ne 90) {
            throw "Invalid PE binary: $path"
        }
        $pe = [BitConverter]::ToInt32($header,0x3c)
        if ($pe -lt 64 -or $pe -gt $header.Length-6 -or
            [BitConverter]::ToUInt16($header,$pe+4) -ne $expected) {
            throw "Wrong architecture in $path"
        }
    }
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $folder "NajmCard.exe"))
    if ($null -eq $icon) { throw 'Public launcher does not contain a Windows icon' }
    $icon.Dispose()
}
Test-PortableLayout $portableDir

$zip = Join-Path $outRoot "NajimCardNew-v$Version-$Arch-Portable.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zip -CompressionLevel Optimal
# Inspect the actual DISTRIBUTED ZIP, not just the staging folder. A missing
# root launcher or accidentally flattened DLLs must fail CI before Inno Setup.
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name -ne '' } |
        ForEach-Object { $_.FullName.Replace('\\','/') })
    if ($entries -notcontains 'NajmCard.exe' -or
        $entries -notcontains 'App/NajmCard.exe') {
        throw 'Portable ZIP must contain root launcher and App/NajmCard.exe'
    }
    $stray = @($entries | Where-Object { $_ -notmatch '^App/' -and $_ -ne 'NajmCard.exe' })
    if ($stray.Count -gt 0) {
        throw "Unexpected files beside public launcher: $($stray -join ', ')"
    }
}
finally { $archive.Dispose() }

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
if (!$iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (!(Test-Path $iscc)) { throw "Inno Setup compiler ISCC.exe missing: $iscc" }
& $iscc "/DSourceDir=$portableDir" "/DArch=$Arch" "/DAppVersion=$Version" "/O$outRoot" "/FNajimCardNew-v$Version-$Arch-Installer" /Q installer/NajmCard.iss
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $Arch" }
$installer = Join-Path $outRoot "NajimCardNew-v$Version-$Arch-Installer.exe"
if (!(Test-Path $installer)) { throw "Installer missing: $installer" }
foreach ($f in @($zip, $installer)) { if ((Get-Item $f).Length -lt 100000) { throw "Package suspiciously small: $f" } }
@($zip,$installer) | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 $_).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $_ -Leaf)"
} | Set-Content -Encoding ascii (Join-Path $outRoot "SHA256SUMS-$Arch.txt")
Write-Host "Successfully packaged $Arch Portable and Installer"
