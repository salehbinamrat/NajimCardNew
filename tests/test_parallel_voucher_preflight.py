"""Use independent authenticated RouterOS read lanes to accelerate safe batch preflight."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
CORE=(ROOT/"src/NajmCard.Core/Services")

class ParallelVoucherPreflightContract(unittest.TestCase):
    def test_name_collision_scans_parallel_only_on_isolated_read_lanes(self):
        gateway=(CORE/"RouterGateway.cs").read_text(encoding="utf-8")
        session=(CORE/"ResilientRouterSession.cs").read_text(encoding="utf-8")
        self.assertIn("if(client is ResilientRouterSession)",gateway)
        self.assertIn("await Task.WhenAll(scans)",gateway)
        self.assertIn("else\n        {\n            foreach(var (path,field) in sources)",gateway)
        self.assertIn("private readonly Lane _inventory=new();",session)
        self.assertIn('command is "/tool/user-manager/user/print"',session)

    def test_failed_or_partial_inventory_never_issues_any_cards(self):
        gateway=(CORE/"RouterGateway.cs").read_text(encoding="utf-8")
        dispatcher=(ROOT/"src/NajmCard.Desktop/Services/VoucherBatchDispatcher.cs").read_text(encoding="utf-8")
        self.assertIn("rows.Any(row=>!row.ContainsKey(actualField))",gateway)
        self.assertIn("ReadSourceAsync(path,field)",gateway)
        self.assertLess(dispatcher.index("gateway.ReadExistingVoucherNamesAsync"),
                        dispatcher.index("sendSession.BeginSend(voucher)"))
        self.assertLess(dispatcher.index("await gateway.ReadExistingVoucherNamesAsync"),
                        dispatcher.index("using var sendSession = store.OpenSendSession()"))

if __name__=="__main__": unittest.main()
