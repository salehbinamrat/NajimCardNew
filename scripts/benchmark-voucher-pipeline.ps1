param([string]$Label = 'core', [string]$Architecture = '',
      [ValidateSet('All', 'Print', 'Pipeline')][string]$Mode = 'All')
$ErrorActionPreference = 'Stop'
if ($Label -notmatch '^[a-z0-9-]+$') { throw 'Invalid benchmark label' }
if ($Architecture -notin @('', 'x86', 'x64')) { throw 'Invalid architecture' }
$root = Split-Path $PSScriptRoot -Parent
$baseline = Join-Path $root '.pipeline-baseline'
$out = Join-Path $root 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null
python (Join-Path $root 'tools/VoucherPipelineBenchmarks/instrument_baseline.py') $root $baseline
if ($LASTEXITCODE -ne 0) { throw 'Baseline instrumentation failed' }
$archArgs = @()
if ($Architecture) { $archArgs = @('--runtime', "win-$Architecture", '--self-contained', 'true') }
$runtime = if ($Architecture) { "win-$Architecture/" } else { '' }
# Exercise the real WPF host before the longer durable SQLite measurements.
if ($Mode -ne 'Pipeline') {
foreach ($variant in @('before', 'after')) {
    $location = if ($variant -eq 'before') { $baseline } else { $root }
    $print = Join-Path $location 'tools/VoucherPrintBenchmarks/VoucherPrintBenchmarks.csproj'
    dotnet build $print --configuration Release @archArgs
    if ($LASTEXITCODE -ne 0) { throw "WPF $variant build failed" }
    $printExe = Join-Path $location "tools/VoucherPrintBenchmarks/bin/Release/net10.0-windows/$($runtime)VoucherPrintBenchmarks.exe"
    & $printExe (Join-Path $out "voucher-print-$variant-$Label.jsonl")
    if ($LASTEXITCODE -ne 0) { throw "WPF $variant measurement failed" }
}
python (Join-Path $root 'tools/VoucherPipelineBenchmarks/compare_print.py') (Join-Path $out "voucher-print-before-$Label.jsonl") (Join-Path $out "voucher-print-after-$Label.jsonl")
if ($LASTEXITCODE -ne 0) { throw 'WPF comparison failed' }
}
if ($Mode -ne 'Print') {
foreach ($variant in @('before', 'after')) {
    $location = if ($variant -eq 'before') { $baseline } else { $root }
    $stage4 = if ($variant -eq 'before') { 'false' } else { 'true' }
    $project = Join-Path $location 'tools/VoucherPipelineBenchmarks/VoucherPipelineBenchmarks.csproj'
    dotnet build $project --configuration Release "-p:Stage4=$stage4" @archArgs
    if ($LASTEXITCODE -ne 0) { throw "Pipeline $variant build failed" }
    $exe = Join-Path $location "tools/VoucherPipelineBenchmarks/bin/Release/net10.0/$($runtime)VoucherPipelineBenchmarks.exe"
    & $exe (Join-Path $out "voucher-pipeline-$variant-$Label.jsonl")
    if ($LASTEXITCODE -ne 0) { throw "Pipeline $variant measurement failed" }
}
python (Join-Path $root 'tools/VoucherPipelineBenchmarks/compare.py') (Join-Path $out "voucher-pipeline-before-$Label.jsonl") (Join-Path $out "voucher-pipeline-after-$Label.jsonl")
if ($LASTEXITCODE -ne 0) { throw 'Pipeline comparison failed' }
}
