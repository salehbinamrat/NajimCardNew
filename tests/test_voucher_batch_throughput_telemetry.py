"""The voucher progress indicator reports actual throughput, without weakening delivery safety."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
DISPATCH=(ROOT/"src/NajmCard.Desktop/Services/VoucherBatchDispatcher.cs")

class BatchThroughputTelemetryContract(unittest.TestCase):
    def test_measures_preflight_and_card_router_latency_separately(self):
        source=DISPATCH.read_text(encoding="utf-8")
        self.assertIn("using System.Diagnostics;",source)
        self.assertIn("Stopwatch.StartNew()",source)
        self.assertIn("preflightTimer.Elapsed",source)
        self.assertIn("routerTimer.Elapsed",source)
        self.assertIn("databaseTimer.Elapsed",source)
        self.assertIn("routerElapsed +=",source)
        self.assertIn("databaseElapsed +=",source)
        self.assertIn("كرت/ث",source)
        self.assertIn("زمن الراوتر",source)
        self.assertIn("زمن الحفظ",source)
    def test_does_not_remove_exact_verification_or_durable_receipts(self):
        source=DISPATCH.read_text(encoding="utf-8")
        self.assertIn("gateway.ReadExistingVoucherNamesAsync",source)
        self.assertIn("VerifyUserManagerV6HotspotReadinessAsync",source)
        self.assertIn("sendSession.BeginSend(voucher)",source)
        self.assertIn("sendSession.FinishSend(voucher, state, error)",source)
        self.assertIn('state = "UNKNOWN"',source)
        self.assertIn("using var sendSession = store.OpenSendSession()",source)

if __name__=="__main__": unittest.main()
