"""User Manager must open the six-card landing screen before any subpage."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
XAML="{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"

class UserManagerHomeContract(unittest.TestCase):
    def test_six_clickable_cards_match_reference_rtl_order(self):
        path=VIEWS/"UserManagerHomeView.xaml"
        root=ET.parse(path).getroot()
        tags=[b.get("Tag") for b in root.iter(XAML+"Button") if b.get("Tag")]
        self.assertEqual(tags,[
            "users","packages","sessions",
            "search","reports","maintenance"
        ])
        text=path.read_text(encoding="utf-8")
        for title in (
            "إدارة User Manager","إدارة المستخدمين","إدارة الباقات",
            "الجلسات","البحث المتقدم","التقارير","النسخ والصيانة",
            "Profiles و Limitations","الكروت والحسابات",
            "الاتصالات والاستهلاك","فلاتر دقيقة وسريعة",
            "إحصائيات الكروت","النسخ والاستعادة"
        ):
            self.assertIn(title,text)
        self.assertIn('FlowDirection="RightToLeft"',text)
        self.assertIn('Grid.Row="1"',text)
        self.assertIn('Grid.Row="2"',text)

    def test_sidebar_enters_hub_first_and_back_restores_it(self):
        main=(ROOT/"src/NajmCard.Desktop/MainWindow.xaml.cs").read_text(encoding="utf-8")
        explorer=(VIEWS/"UserManagerView.cs").read_text(encoding="utf-8")
        home=(VIEWS/"UserManagerHomeView.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('"usermanager" => new UserManagerView(',main)
        self.assertIn('new UserManagerHomeView(',explorer)
        self.assertIn('Content=home',explorer)
        self.assertIn('void ReturnToHome()',explorer)
        self.assertIn('Back',explorer)
        self.assertIn('Card_Click',home)
        self.assertIn('Navigate',home)
        self.assertIn('ReadUserManagerCatalogAsync',explorer)
        self.assertIn('CreateUserManagerV7AccountAsync',explorer)

    def test_all_six_cards_reach_real_sections(self):
        host=(VIEWS/"UserManagerView.cs").read_text(encoding="utf-8")
        for key in ('"users"','"packages"','"sessions"','"search"','"reports"','"maintenance"'):
            self.assertIn(key,host)
        self.assertIn('UserManagerReportsView',host)
        self.assertIn('UserManagerMaintenanceView',host)
        self.assertIn('UserManagerCatalogSection.Users',host)
        self.assertIn('UserManagerCatalogSection.Profiles',host)
        self.assertIn('UserManagerCatalogSection.Sessions',host)
        self.assertIn('search.Focus()',host)

    def test_report_and_maintenance_are_honest_and_safe(self):
        reports=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        maintenance=(VIEWS/"UserManagerMaintenanceView.cs").read_text(encoding="utf-8")
        self.assertIn('ReadUmAccountsAsync',reports)
        self.assertIn('ReadUmSessionsAsync',reports)
        self.assertIn('SaveFileDialog',reports)
        self.assertIn('ReadUserManagerV6DatabaseStatusAsync',maintenance)
        self.assertIn('DiagnoseUserManagerV7RadiusAsync',maintenance)
        self.assertIn('store.Backup()',maintenance)
        self.assertIn('لا يمكن',maintenance)
        self.assertIn('CreateUmRouterBackupAsync',maintenance)
        self.assertIn('ReadUmRouterBackupsAsync',maintenance)
        self.assertNotIn('database/load',maintenance)

    def test_visual_smoke_captures_actual_home_not_mockup(self):
        smoke=(ROOT/"tests/NajmCard.VisualSmoke/Program.cs").read_text(encoding="utf-8")
        ci=(ROOT/".github/workflows/windows-ci.yml").read_text(encoding="utf-8")
        self.assertIn('user-manager-home-1260x730.png',smoke)
        self.assertIn('VerifyUserManagerHome',smoke)
        self.assertIn('NajmCard-usermanager-home-1260x730',ci)

if __name__=="__main__":
    unittest.main()
