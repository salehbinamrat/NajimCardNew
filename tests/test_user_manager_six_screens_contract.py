"""Reference layout, real data and safe operations for all six User Manager pages."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
CORE=ROOT/"src/NajmCard.Core/Services"

class UserManagerSixScreensContract(unittest.TestCase):
    def test_all_six_cards_have_real_destination_views(self):
        source=(VIEWS/"UserManagerView.cs").read_text(encoding="utf-8")
        for view in ("UserManagerAccountsScreen","UserManagerPlansScreen",
                     "UserManagerSessionsScreen","UserManagerSearchScreen",
                     "UserManagerReportsView","UserManagerMaintenanceView"):
            with self.subTest(view=view):
                self.assertIn("new "+view+"(",source)
                self.assertTrue(any((VIEWS/(view+suffix)).exists()
                    for suffix in (".cs",".xaml")))
        self.assertIn("new UserManagerHomeView(",source)
        self.assertIn("العودة إلى إدارة User Manager",source)

    def test_data_readers_have_version_specific_read_only_paths(self):
        core=(CORE/"RouterGateway.UserManagerLiveRows.cs").read_text(encoding="utf-8")
        for path in ("/user-manager/user-profile/print",
                     "/user-manager/session/print",
                     "/tool/user-manager/session/print",
                     "/user-manager/profile-limitation/print",
                     "/tool/user-manager/profile/profile-limitation/print"):
            self.assertIn(path,core)
        self.assertIn('".proplist"',core)
        self.assertNotIn('"/user-manager/user/add"',core)
        self.assertNotIn('"password"',core)

    def test_real_users_packages_sessions_and_advanced_search(self):
        for file,read,refresh in (
            ("UserManagerAccountsScreen.cs","ReadUmAccountsAsync","Draw()"),
            ("UserManagerPlansScreen.cs","ReadCachedUmPlansAsync","Draw()"),
            ("UserManagerSessionsScreen.cs","ReadUmSessionsAsync","Update()"),
            ("UserManagerSearchScreen.cs","ReadUmSessionsAsync","Draw()")):
            with self.subTest(file=file):
                ui=(VIEWS/file).read_text(encoding="utf-8")
                self.assertIn(read,ui)
                self.assertIn("ItemsSource=",ui)
                self.assertIn("CancellationTokenSource",ui)
                self.assertIn(refresh,ui)
                self.assertIn("Skip(page*",ui)
                self.assertIn("Take(",ui)
        plans=(VIEWS/"UserManagerPlansScreen.cs").read_text(encoding="utf-8")
        cache=(CORE/"RouterGateway.CachedUmPlans.cs").read_text(encoding="utf-8")
        self.assertIn("await ReadUmPlansAsync(ct)",cache)
        self.assertIn("CurrentPlansCache()",cache)
        self.assertIn("ReadCachedUmPlansAsync(force,request.Token)",plans)
        for action in ("ChangeUmPlanAsync","LinkUmProfileLimitationAsync",
                       "Profiles (الباقات)","Limitations (القيود)"):
            self.assertIn(action,plans)

    def test_reports_use_only_verified_routeros_payments(self):
        reports=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        self.assertIn("ReadUmApprovedPaymentsAsync",reports)
        self.assertIn("ReadUmSessionsAsync",reports)
        self.assertIn("ReadUmAccountsAsync",reports)
        self.assertIn("DrawBars",reports)
        self.assertIn("UmCsv.Export",reports)
        core=(CORE/"RouterGateway.UserManagerReportData.cs").read_text(encoding="utf-8")
        self.assertIn('"approved"',core)
        self.assertIn('"/user-manager/payment/print"',core)

    def test_maintenance_really_lists_backups_without_unsafe_restore(self):
        ui=(VIEWS/"UserManagerMaintenanceView.cs").read_text(encoding="utf-8")
        self.assertIn("ReadUmRouterBackupsAsync",ui)
        self.assertIn("CreateUmRouterBackupAsync",ui)
        self.assertIn("store.Backup()",ui)
        self.assertIn("لا يمكن",ui)
        policy=(CORE/"RouterOsCommandPolicy.cs").read_text(encoding="utf-8")
        self.assertIn("overwrite",policy)
        self.assertNotIn('"/user-manager/database/load",',policy)

    def test_scoped_controls_keep_legible_foreground_and_padding(self):
        ui=(VIEWS/"UmUi.cs").read_text(encoding="utf-8")
        self.assertIn("Border.PaddingProperty,new Binding(nameof(Button.Padding))",ui)
        self.assertIn("TextBlock.ForegroundProperty,new Binding(nameof(Button.Foreground))",ui)
        self.assertIn("TextBlock.ForegroundProperty,new Binding(nameof(ComboBox.Foreground))",ui)
        self.assertIn("TextBlock.ForegroundProperty,new Binding(nameof(ComboBoxItem.Foreground))",ui)
        self.assertIn("popupBorder.SetValue(TextElement.ForegroundProperty,Ink)",ui)
        self.assertIn("ScrollViewer.CanContentScrollProperty,true",ui)
        self.assertIn("options.Distinct(StringComparer.Ordinal)",ui)

    def test_maintenance_does_not_duplicate_unavailable_actions(self):
        ui=(VIEWS/"UserManagerMaintenanceView.cs").read_text(encoding="utf-8")
        self.assertNotIn('UmUi.Action("تنزيل عبر Winbox")',ui)
        self.assertNotIn('UmUi.Action("استعادة النسخة")',ui)
        self.assertIn("لتحميل ملف .umb إلى الكمبيوتر استخدم قائمة Files",ui)
        self.assertIn("زر الاستعادة محجوز",ui)

    def test_no_fake_reference_values(self):
        examples=["6,842","1,245,000","4,836","1.24","5000511","2026/11/11"]
        for name in ("UserManagerAccountsScreen.cs","UserManagerPlansScreen.cs",
                     "UserManagerSessionsScreen.cs","UserManagerReportsView.cs"):
            source=(VIEWS/name).read_text(encoding="utf-8")
            for example in examples:
                self.assertNotIn(example,source)

if __name__=="__main__":
    unittest.main()
