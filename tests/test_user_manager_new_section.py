"""Regression checks for the independent read-only User Manager v6/v7 explorer."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
DESKTOP=ROOT/"src"/"NajmCard.Desktop"

class UserManagerSectionContract(unittest.TestCase):
    def test_dedicated_sidebar_entry_and_real_view(self):
        main=(DESKTOP/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        view=(DESKTOP/"Views"/"UserManagerView.cs").read_text(encoding="utf-8")
        self.assertEqual(main.count('Entry("usermanager"'),1)
        self.assertIn('"usermanager" => new UserManagerView(',main)
        self.assertIn("ReadUserManagerCatalogAsync",view)
        self.assertIn("ReadRouterOsGenerationAsync",view)
        self.assertIn("السابق",view)
        self.assertIn("التالي",view)
        self.assertIn("بحث",view)
        self.assertIn("قراءة البيانات",view)

    def test_catalog_does_not_mutate_or_print_cards(self):
        gateway=(ROOT/"src"/"NajmCard.Core"/"Services"/"RouterGateway.UserManagerCatalog.cs").read_text(encoding="utf-8")
        self.assertIn('"/tool/user-manager"',gateway)
        self.assertIn('"/user-manager"',gateway)
        for bad in ('/user/add', '/user/remove', '/user/set', 'create-and-activate-profile'):
            self.assertNotIn(bad,gateway)

    def test_massive_um_reads_use_separate_connection_lane(self):
        session=(ROOT/"src"/"NajmCard.Core"/"Services"/"ResilientRouterSession.cs").read_text(encoding="utf-8")
        for path in ("/tool/user-manager/user/print","/user-manager/user/print",
                     "/tool/user-manager/session/print","/user-manager/session/print"):
            self.assertIn(path,session)

if __name__=="__main__":
    unittest.main()
