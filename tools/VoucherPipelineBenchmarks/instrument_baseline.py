"""Add measurement probes ONLY to the pinned phase-3 benchmark checkout.

Never changes queries, waits, transaction boundaries, generation or delivery decisions.
Kept explicit so reviewers can audit the before/after comparison.
"""
from pathlib import Path
import shutil
import subprocess
import sys

BASE = "7f3b92b4656eb8fa14052b87cb493766b3fd1d6b"
current, baseline = map(lambda s: Path(s).resolve(), sys.argv[1:])
assert current != baseline
assert subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=baseline, text=True).strip() == BASE
for relative in ["tools/VoucherPipelineBenchmarks", "tools/VoucherPrintBenchmarks", "tests/Fixtures/VoucherPipelineRouter.cs",
                 "src/NajmCard.Core/Services/VoucherPipelineMetrics.cs"]:
    src, dst = current / relative, baseline / relative
    dst.parent.mkdir(parents=True, exist_ok=True)
    if src.is_dir():
        shutil.copytree(src, dst, dirs_exist_ok=True, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
    else:
        shutil.copy2(src, dst)

def change(relative, transform):
    path = baseline / relative
    text = path.read_text(encoding="utf-8-sig")
    marker = "// phase4-benchmark-probes"
    if marker in text:
        return
    path.write_text(marker + "\n" + transform(text), encoding="utf-8")

def probe(text, signature, stage):
    start = text.index(signature)
    brace = text.index("{", start)
    return text[:brace+1] + f"\n        using var stage4Probe=VoucherPipelineOperation.Measure(VoucherPipelineStage.{stage});\n" + text[brace+1:]

change("src/NajmCard.Core/Services/VoucherGenerator.cs", lambda t: probe(t, "public static IReadOnlyList<Voucher> Generate(", "Generation"))
def store(t):
    t = "using NajmCard.Core.Services;\n" + t
    for method in ["public void SaveBatch(", "private static Voucher BeginSendOnConnection(", "private static BatchProgress FinishSendOnConnection("]:
        t = probe(t, method, "SQLite")
    return t
change("src/NajmCard.Desktop/Services/VoucherStore.cs", store)
def gateway(t):
    t = probe(t, "public async Task<IReadOnlySet<string>> ReadExistingVoucherNamesAsync(", "NameCheck")
    t = probe(t, "public async Task AddHotspotUserAsync(", "Add")
    t = t.replace("            created = await receiptClient.CommandWithReceiptAsync(",
                  "            using var addProbe=VoucherPipelineOperation.Measure(VoucherPipelineStage.Add);\n            created = await receiptClient.CommandWithReceiptAsync(", 1)
    t = t.replace('            await client.CommandAsync(\n                "/tool/user-manager/user/create-and-activate-profile",',
                  '            using(VoucherPipelineOperation.Measure(VoucherPipelineStage.Activation))\n            await client.CommandAsync(\n                "/tool/user-manager/user/create-and-activate-profile",', 1)
    t = t.replace("                if (client is not IRouterQueryClient lookup)",
                  "                using var verifyProbe=VoucherPipelineOperation.Measure(VoucherPipelineStage.Verification);\n                if (client is not IRouterQueryClient lookup)", 1)
    return t
change("src/NajmCard.Core/Services/RouterGateway.cs", gateway)
change("src/NajmCard.Core/Services/RouterGateway.PppoeIssuing.cs", lambda t: t.replace(
    "            reply=await receiptClient.CommandWithReceiptAsync(",
    "            using var addProbe=VoucherPipelineOperation.Measure(VoucherPipelineStage.Add);\n            reply=await receiptClient.CommandWithReceiptAsync(", 1))
change("src/NajmCard.Core/Services/RouterGateway.BatchRecovery.cs", lambda t: t.replace(
    "    public Task<string> ReadRouterIdentityAsync(CancellationToken ct=default)=>\n        client is ResilientRouterSession session?session.BindRouterIdentityAsync(ct):\n            RouterDeviceIdentity.ReadAsync(client,ct);",
    """    public async Task<string> ReadRouterIdentityAsync(CancellationToken ct=default)
    {
        using var probe=VoucherPipelineOperation.Measure(VoucherPipelineStage.Identity);
        return await (client is ResilientRouterSession session?session.BindRouterIdentityAsync(ct):RouterDeviceIdentity.ReadAsync(client,ct));
    }"""))
change("src/NajmCard.Core/Services/RouterGateway.UserManagerV6HotspotReadiness.cs", lambda t: probe(t,
    "public async Task VerifyUserManagerV6HotspotReadinessAsync(", "Readiness"))

# These two files differ from phase 3 only by measurement probes. The actual
# page generation, 144-dpi rendering, PDF writes and atomic rename are unchanged.
for relative in ["src/NajmCard.Desktop/Services/VoucherPdfExporter.cs",
                 "src/NajmCard.Desktop/Views/PrintingView.DraftDelivery.cs"]:
    shutil.copy2(current / relative, baseline / relative)
