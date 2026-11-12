"""Performance contracts without weakening RouterOS or SQLite safety."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
CORE=ROOT/"src/NajmCard.Core/Services"
DESKTOP=ROOT/"src/NajmCard.Desktop"

class VoucherIssuancePerformanceContracts(unittest.TestCase):
    def test_batch_session_reuses_progress_and_keeps_per_card_commits(self):
        store=(DESKTOP/"Services/VoucherStore.cs").read_text(encoding="utf-8")
        self.assertIn("private sealed record BatchProgress",store)
        self.assertIn("PRAGMA data_version",store)
        self.assertIn("cached.WithOutcome(state)",store)
        self.assertIn("ReadBatchProgress(_db,null,voucher)",store)
        self.assertIn('if(previous=="FAILED")_progress=null;',store)
        self.assertIn("tx.Commit();\n        return progress;",store)
        self.assertIn("using var tx = db.BeginTransaction();",store)
        dispatcher=(DESKTOP/"Services/VoucherBatchDispatcher.cs").read_text(encoding="utf-8")
        self.assertIn("using var sendSession = store.OpenSendSession();",dispatcher)
        self.assertIn("sendSession.BeginSend(voucher);",dispatcher)
        self.assertIn("sendSession.FinishSend(voucher, state, error);",dispatcher)

    def test_um_v6_learns_projection_only_without_removing_readback(self):
        gateway=(CORE/"RouterGateway.cs").read_text(encoding="utf-8")
        self.assertIn("_umV6ReadProjectionMode",gateway)
        self.assertIn("Volatile.Read(ref _umV6ReadProjectionMode)",gateway)
        self.assertIn("QueryByIdAsync(verifyPath,id,projection,ct)",gateway)
        self.assertIn("QueryByIdAsync(verifyPath,id,",gateway)
        self.assertIn("lastVerificationError",gateway)
        self.assertIn("for (int attempt=0;attempt<3;attempt++)",gateway)

    def test_creator_reentry_cache_is_bounded_and_manual_refresh_forced(self):
        catalog=(DESKTOP/"Views/GeneratorView.ProfileLoading.cs").read_text(encoding="utf-8")
        creator=(DESKTOP/"Views/GeneratorView.cs").read_text(encoding="utf-8")
        self.assertIn("DateTimeOffset.UtcNow-_lastCatalogLoadedAt<TimeSpan.FromSeconds(12)",catalog)
        self.assertIn("LoadProfilesAsync(bool force=false)",catalog)
        self.assertIn("LoadProfilesAsync(force:true)",creator)
        self.assertIn("_profileReadGate.WaitAsync()",catalog)

    def test_router_api_has_low_latency_full_sentence_writes_and_buffered_reads(self):
        client=(CORE/"RouterOsClient.cs").read_text(encoding="utf-8")
        protocol=(CORE/"RouterOsProtocol.cs").read_text(encoding="utf-8")
        self.assertIn("tcp.NoDelay = true",client)
        self.assertIn("new BufferedStream(stream, 64 * 1024)",client)
        self.assertIn("frame.WriteByte(0)",protocol)
        self.assertIn("stream.WriteAsync(frame.GetBuffer().AsMemory(",protocol)
        self.assertIn("await stream.FlushAsync(token)",protocol)
        self.assertEqual(protocol.count("await stream.WriteAsync("),1)

    def test_pdf_staging_buffer_is_reused_but_quality_and_text_unchanged(self):
        pdf=(DESKTOP/"Services/VoucherPdfExporter.cs").read_text(encoding="utf-8")
        self.assertIn("using var imageBuffer=new MemoryStream(1024*1024)",pdf)
        self.assertIn("imageBuffer.SetLength(0)",pdf)
        self.assertIn("using var image=XImage.FromStream(imageBuffer)",pdf)
        self.assertIn("const int dpi=144",pdf)
        self.assertIn("foreach(var text in credentials)DrawCredential",pdf)
        self.assertIn("File.Replace(staged,target,null)",pdf)

if __name__=="__main__":
    unittest.main()
