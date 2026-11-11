"""Safety contracts for explicit User Manager account actions."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
CORE=ROOT/"src"/"NajmCard.Core"/"Services"
VIEW=ROOT/"src"/"NajmCard.Desktop"/"Views"/"UserManagerView.cs"

class UserManagerSafeOperationsUiContracts(unittest.TestCase):
    def test_actions_require_user_selection_and_confirmation(self):
        ui=VIEW.read_text(encoding="utf-8")
        self.assertIn("UserManagerAccountSelection? SelectedUser()",ui)
        self.assertIn("PageUi.Confirm(",ui)
        self.assertIn("SetUserManagerAccountDisabledAsync",ui)
        self.assertIn("AssignUserManagerV7ProfileAsync",ui)
        self.assertIn("RouterOsGeneration.V7",ui)
        self.assertIn("Reset();",ui)

    def test_exact_id_query_does_not_allow_unrestricted_read(self):
        transport=(CORE/"RouterOsClient.cs").read_text(encoding="utf-8")
        self.assertIn('"?.id=" + id',transport)
        self.assertIn('"/user-manager/user/print"',transport)
        self.assertIn('"/user-manager/user-profile/print"',transport)
        mutations=(CORE/"RouterGateway.UserManagerMutations.cs").read_text(encoding="utf-8")
        self.assertIn("QueryByIdAsync",mutations)
        self.assertIn("CommandWithReceiptAsync",mutations)
        self.assertNotIn("password",mutations.lower())
        self.assertNotIn("user/remove",mutations)
        self.assertNotIn("profile/remove",mutations)

if __name__=="__main__":
    unittest.main()
