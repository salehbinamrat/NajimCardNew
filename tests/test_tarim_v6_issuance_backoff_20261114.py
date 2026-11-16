"""Bound v6 activation read-back wait without reducing correctness safeguards."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/"src/NajmCard.Core/Services/RouterGateway.cs"

class IssuingResponsivenessTests(unittest.TestCase):
    def test_v6_confirmation_is_bounded_and_never_replays_writes(self):
        source=SRC.read_text(encoding="utf-8")
        method=source[source.index("public async Task<UserManagerV6DeliveryResult> CreateAndActivateUserManagerV6Async("):
                      source.index("public async Task<BatchDeliveryResult> UploadUserManagerV6BatchAsync(")]
        poller=(SRC.parent/"VoucherReadbackPoller.cs").read_text(encoding="utf-8")
        self.assertIn('VerifyIssuedVoucherAsync(voucher,id,"userman",customer,ct)',method)
        self.assertIn("MaximumReads=5",poller)
        self.assertIn("deadline.CancelAfter(TimeSpan.FromSeconds(20))",poller)
        self.assertIn("last.Confirmed||!last.MayBecomeReady",poller)
        self.assertIn("-(clock.Elapsed-started)",poller)
        self.assertNotIn("CommandAsync",poller)
        self.assertNotIn("Task.Run(()=>client.CommandAsync",method)

if __name__=="__main__":unittest.main()
