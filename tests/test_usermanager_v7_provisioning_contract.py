"""Create a RouterOS 7 UM account without breaking the existing voucher printer."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
CORE=ROOT/"src"/"NajmCard.Core"/"Services"
UI=ROOT/"src"/"NajmCard.Desktop"/"Views"/"UserManagerView.cs"

class V7ProvisioningContracts(unittest.TestCase):
    def test_server_side_exact_name_queries_for_three_account_services(self):
        client=(CORE/"RouterOsClient.cs").read_text(encoding="utf-8")
        interface=(CORE/"IRouterNameQueryClient.cs").read_text(encoding="utf-8")
        service=(CORE/"ResilientRouterSession.cs").read_text(encoding="utf-8")
        for source in (client,interface,service):
            self.assertIn("QueryByNameAsync",source)
        self.assertIn('"?name="+name',client)
        for path in ("/user-manager/user/print",
                     "/ip/hotspot/user/print","/ppp/secret/print"):
            self.assertIn(path,client)

    def test_two_step_v7_creator_not_v6_bulk_printer(self):
        code=(CORE/"RouterGateway.UserManagerV7Provision.cs").read_text(encoding="utf-8")
        for value in (
            "ReadRouterOsGenerationAsync",
            "QueryByNameAsync",
            "CommandWithReceiptAsync",
            "QueryByIdAsync",
            "AssignUserManagerV7ProfileAsync",
            '"password"]=password',
            "AccountCreated",
        ):
            self.assertIn(value,code)
        self.assertNotIn("password=username",code)
        self.assertNotIn("/tool/user-manager/user/add",code)
        self.assertNotIn("/ip/hotspot/user/add",code)
        self.assertNotIn("VoucherGenerator",code)

    def test_ui_requires_confirmation_and_warns_about_incomplete_assignment(self):
        ui=UI.read_text(encoding="utf-8")
        self.assertIn("PasswordBox",ui)
        self.assertIn("إنشاء مستخدم v7",ui)
        self.assertIn("PageUi.Confirm(",ui)
        self.assertIn("result.AccountCreated&&!result.ProfileAssigned",ui)
        self.assertIn("accountPassword.Clear();",ui)

if __name__=="__main__":
    unittest.main()
