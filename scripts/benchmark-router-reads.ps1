param([ValidatePattern('^[a-z0-9-]+$')][string]$Label = 'local')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = Join-Path $root '.benchmark-baseline'
$baselineTool = Join-Path $baseline 'tools/RouterReadBenchmarks'
if (!(Test-Path (Join-Path $baseline 'src/NajmCard.Core/NajmCard.Core.csproj'))) {
    throw 'Check out baseline bc26ced into .benchmark-baseline before benchmarking.'
}
$baselineHead = git -C $baseline rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $baselineHead -ne 'bc26cedc2e60fbe2cbc778631c8b3cc6a2ad27f5') {
    throw 'Benchmark baseline must be the approved phase 2 commit bc26ced.'
}
New-Item -ItemType Directory -Force $baselineTool | Out-Null
Copy-Item (Join-Path $root 'tools/RouterReadBenchmarks/Program.cs') $baselineTool -Force
Copy-Item (Join-Path $root 'tools/RouterReadBenchmarks/RouterReadBenchmarks.csproj') $baselineTool -Force
New-Item -ItemType Directory -Force (Join-Path $root 'out') | Out-Null
$before = Join-Path $root "out/router-reads-before-$Label.jsonl"
$after = Join-Path $root "out/router-reads-after-$Label.jsonl"
dotnet build (Join-Path $baselineTool 'RouterReadBenchmarks.csproj') -c Release -p:Stage3=false
if ($LASTEXITCODE -ne 0) { throw 'Baseline harness build failed.' }
dotnet run --project $baselineTool -c Release --no-build -p:Stage3=false | Set-Content $before -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Baseline simulation failed.' }
dotnet build (Join-Path $root 'tools/RouterReadBenchmarks/RouterReadBenchmarks.csproj') -c Release -p:Stage3=true
if ($LASTEXITCODE -ne 0) { throw 'Current harness build failed.' }
dotnet run --project (Join-Path $root 'tools/RouterReadBenchmarks') -c Release --no-build -p:Stage3=true | Set-Content $after -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Current simulation failed.' }
python (Join-Path $root 'tools/RouterReadBenchmarks/compare.py') $before $after
if ($LASTEXITCODE -ne 0) { throw 'Router read performance or correctness budget failed.' }
