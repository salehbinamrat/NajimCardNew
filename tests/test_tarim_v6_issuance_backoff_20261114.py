"""Bound v6 activation read-back wait without reducing correctness safeguards."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/"src/NajmCard.Core/Services/RouterGateway.cs"

class IssuingResponsivenessTests(unittest.TestCase):
    def test_v6_confirmation_keeps_three_exact_id_reads_and_same_total_wait(self):
        source=SRC.read_text(encoding="utf-8")
        method=source[source.index("public async Task<UserManagerV6DeliveryResult> CreateAndActivateUserManagerV6Async("):
                      source.index("public async Task<BatchDeliveryResult> UploadUserManagerV6BatchAsync(")]
        self.assertIn("for (int attempt=0;attempt<3;attempt++)",method)
        self.assertIn("attempt==1?60:390",method)
        self.assertIn("lookup.QueryByIdAsync(verifyPath,id",method)
        self.assertIn("return new UserManagerV6DeliveryResult(false,true,id,",method)
        self.assertIn("Never replay either write",method)
        self.assertNotIn("Task.Run(()=>client.CommandAsync",method)

if __name__=="__main__":unittest.main()
