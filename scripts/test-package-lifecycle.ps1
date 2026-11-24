param(
    [Parameter(Mandatory)][ValidateSet('x86','x64')][string]$Arch,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$BaselineInstaller,
    [Parameter(Mandatory)][string]$Probe,
    [string]$Output = 'out'
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows -or $env:GITHUB_ACTIONS -ne 'true') {
    throw 'This integration test requires a disposable Windows GitHub Actions runner.'
}
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root $Output
$reportDir = Join-Path $out "acceptance-$Arch"
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$Probe = (Resolve-Path -LiteralPath $Probe).Path
$BaselineInstaller = (Resolve-Path -LiteralPath $BaselineInstaller).Path
$fixtureProfile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NajmCardNew'
if (Test-Path -LiteralPath $fixtureProfile) { throw 'Refusing to alter an existing application profile.' }
New-Item -ItemType Directory -Path $fixtureProfile | Out-Null
$sentinel = Join-Path $fixtureProfile '.stage6-disposable-profile'
[IO.File]::WriteAllText($sentinel, [Guid]::NewGuid().ToString())
$work = Join-Path $env:RUNNER_TEMP ("Tarim QA ; مسار عربي " + [Guid]::NewGuid().ToString('N'))
$portable = Join-Path $work 'Portable files'
$install = Join-Path $work 'Installed app'
New-Item -ItemType Directory -Path $work | Out-Null
$rules = [Collections.Generic.List[string]]::new()
$results = [Collections.Generic.List[object]]::new()

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TarimReleaseNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("kernel32.dll",SetLastError=true)] public static extern bool IsWow64Process2(IntPtr process,out ushort machine,out ushort nativeMachine);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
}
'@

function Check-Data([string]$phase, [switch]$Seed) {
    $file = Join-Path $reportDir "$phase-data.json"
    $mode = if ($Seed) {'--lifecycle-seed'} else {'--lifecycle-verify'}
    & $Probe $mode $sentinel $file
    if ($LASTEXITCODE -ne 0) { throw "Fixture data check failed: $phase" }
    $data = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($data.architecture.ToLowerInvariant() -ne $Arch) {throw 'The fixture probe used the wrong architecture'}
    return $data
}

function Block-Network([string]$executable) {
    $name = 'Tarim-QA-' + [Guid]::NewGuid().ToString('N')
    New-NetFirewallRule -Name $name -DisplayName $name -Direction Outbound -Action Block -Profile Any -Program $executable | Out-Null
    $rules.Add($name)
}

function Boot-App([string]$launcher,[string]$phase,[string]$expectedVersion) {
    $application = Join-Path (Split-Path $launcher -Parent) 'App/NajmCard.exe'
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($application)
    if (!$versionInfo.ProductVersion.StartsWith($expectedVersion,[StringComparison]::Ordinal)) {
        throw "Package label and embedded product version differ: $phase"
    }
    Block-Network $application
    $process = Start-Process -FilePath $launcher -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        $child = $null
        while ([DateTime]::UtcNow -lt $deadline) {
            $child = Get-Process -Name NajmCard -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -eq $application -and $_.MainWindowHandle -ne 0 } |
                Select-Object -First 1
            if ($child) {break}
            if ($process.HasExited) {throw "Application exited before displaying its login: $phase"}
            Start-Sleep -Milliseconds 100
        }
        if (!$child) {throw "No real WPF login window: $phase"}
        [ushort]$machine = 0
        [ushort]$native = 0
        if (![TarimReleaseNative]::IsWow64Process2($child.Handle,[ref]$machine,[ref]$native)) {
            throw "Cannot prove the running process architecture: $phase"
        }
        $actual = if ($machine -ne 0) {$machine} else {$native}
        $expected = if ($Arch -eq 'x86') {0x14c} else {0x8664}
        if ($actual -ne $expected) {throw "Running process is not $Arch"}
        if (!$child.WaitForInputIdle(15000)) {throw "WPF did not become responsive: $phase"}
        $perMonitorV2 = [TarimReleaseNative]::AreDpiAwarenessContextsEqual(
            [TarimReleaseNative]::GetWindowDpiAwarenessContext($child.MainWindowHandle),[IntPtr]::new(-4))
        if ($expectedVersion -eq $Version -and !$perMonitorV2) {throw "The shipped window is not PerMonitorV2: $phase"}
        $bounds = [TarimReleaseNative+Rect]::new()
        $monitor = [TarimReleaseNative+MonitorInfo]::new()
        $monitor.Size = [Runtime.InteropServices.Marshal]::SizeOf($monitor)
        if (![TarimReleaseNative]::GetMonitorInfo([TarimReleaseNative]::MonitorFromWindow($child.MainWindowHandle,2),[ref]$monitor)) {
            throw 'No monitor work area'
        }
        $layoutDeadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            if (![TarimReleaseNative]::GetWindowRect($child.MainWindowHandle,[ref]$bounds)) {throw 'No window bounds'}
            $fits = $bounds.Left -ge $monitor.Work.Left-1 -and $bounds.Top -ge $monitor.Work.Top-1 -and
                $bounds.Right -le $monitor.Work.Right+1 -and $bounds.Bottom -le $monitor.Work.Bottom+1
            if ($fits -or $expectedVersion -ne $Version) {break}
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $layoutDeadline)
        if ($expectedVersion -eq $Version -and !$fits) {throw "The actual window leaves the work area: $phase"}
        $bitmap = [Drawing.Bitmap]::new($bounds.Right-$bounds.Left,$bounds.Bottom-$bounds.Top)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        try {
            if (![TarimReleaseNative]::PrintWindow($child.MainWindowHandle,$dc,2)) {throw 'Window capture failed'}
        } finally {$graphics.ReleaseHdc($dc)}
        try {$bitmap.Save((Join-Path $reportDir "$phase-login.png"),[Drawing.Imaging.ImageFormat]::Png)}
        finally {$graphics.Dispose();$bitmap.Dispose()}
        if (!$child.CloseMainWindow() -or !$process.WaitForExit(15000)) {throw "Application did not close normally: $phase"}
        if ($process.ExitCode -ne 0) {throw "Application exit was not successful: $phase"}
        $results.Add([ordered]@{phase=$phase;architecture=$Arch;productVersion=$versionInfo.ProductVersion;
            wpfLoginDisplayed=$true;responsive=$true;normalExit=$true;outboundNetworkBlocked=$true;
            perMonitorV2=$perMonitorV2;fitsCurrentWorkArea=$fits})
    } finally {
        if (!$process.HasExited) {$process.Kill($true);$process.WaitForExit(5000) | Out-Null}
        $process.Dispose()
    }
}

function Install-App([string]$installer,[string]$phase) {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',
        ('/DIR="' + $install + '"'),('/LOG="' + (Join-Path $reportDir "$phase-installer.log") + '"'))
    $p = Start-Process -FilePath $installer -ArgumentList $arguments -PassThru -Wait
    if ($p.ExitCode -ne 0) {throw "Installer failed: $phase ($($p.ExitCode))"}
    if (!(Test-Path -LiteralPath (Join-Path $install 'App/NajmCard.exe'))) {throw 'Installer omitted the WPF application'}
}

function Uninstall-App([string]$phase) {
    $uninstaller = Join-Path $install 'unins000.exe'
    if (!(Test-Path -LiteralPath $uninstaller)) {throw 'Uninstaller missing'}
    $p = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
        ('/LOG="' + (Join-Path $reportDir "$phase-uninstaller.log") + '"')) -Wait -PassThru
    if ($p.ExitCode -ne 0) {throw "Uninstaller failed: $phase"}
    if (Test-Path -LiteralPath (Join-Path $install 'App/NajmCard.exe')) {throw 'Uninstaller left application binaries installed'}
    if ([IO.File]::ReadAllText((Join-Path $install 'user-note.txt')) -ne 'User-owned file: preserve') {
        throw 'Uninstall removed a user-owned file'
    }
    $null = Check-Data $phase
    $results.Add([ordered]@{phase=$phase;uninstalled=$true;userDataPreserved=$true;unownedInstallFilePreserved=$true})
}

try {
    $null = Check-Data 'seed' -Seed
    Expand-Archive -LiteralPath (Join-Path $out "NajimCardNew-v$Version-$Arch-Portable.zip") -DestinationPath $portable
    Boot-App (Join-Path $portable 'NajmCard.exe') 'portable' $Version
    $null = Check-Data 'portable'
    Install-App $BaselineInstaller 'baseline'
    [IO.File]::WriteAllText((Join-Path $install 'user-note.txt'),'User-owned file: preserve')
    Boot-App (Join-Path $install 'NajmCard.exe') 'baseline' '0.1.0'
    $null = Check-Data 'baseline'
    $current = Join-Path $out "NajimCardNew-v$Version-$Arch-Installer.exe"
    Install-App $current 'upgrade'
    Boot-App (Join-Path $install 'NajmCard.exe') 'upgrade' $Version
    $null = Check-Data 'upgrade'
    Uninstall-App 'after-upgrade'
    Install-App $current 'clean-install'
    Boot-App (Join-Path $install 'NajmCard.exe') 'clean-install' $Version
    Uninstall-App 'after-clean-install'
    [ordered]@{architecture=$Arch;version=$Version;commit=$env:GITHUB_SHA;checks=$results;
        realRouterTested=$false;physicalPrinterTested=$false;fieldApproval=$false} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reportDir 'package-acceptance.json') -Encoding utf8
    Write-Host "Actual $Arch Portable, Installer, upgrade, uninstall and data preservation checks passed."
} finally {
    foreach ($rule in $rules) {Remove-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue}
}
