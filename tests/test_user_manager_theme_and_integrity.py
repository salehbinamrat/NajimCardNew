"""User Manager pages must use NajimCard's shipped burgundy/gold theme."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
THEME=ROOT/"src/NajmCard.Desktop/Themes/BurgundyGold.xaml"
X="{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"

class UserManagerPaletteAndIntegrityTests(unittest.TestCase):
    def test_real_application_theme_has_required_shared_tokens(self):
        root=ET.parse(THEME).getroot()
        names={node.get("{http://schemas.microsoft.com/winfx/2006/xaml}Key")
               for node in root}
        for token in ("SidebarBrush","SurfaceBrush","BurgundyBrush",
                      "CreamPanelBrush","CreamTextBrush","GoldBrush",
                      "GoldBrightBrush","MaroonLineBrush","LineBrush"):
            self.assertIn(token,names)

    def test_six_card_home_uses_burgundy_and_gold_shared_resources(self):
        xaml=(VIEWS/"UserManagerHomeView.xaml").read_text(encoding="utf-8")
        ET.fromstring(xaml)
        for token in ("CreamPanelBrush","CreamTextBrush","BurgundyBrush",
                      "GoldBrush","MaroonLineBrush"):
            self.assertIn("{StaticResource "+token+"}",xaml)
        for old in ("#D9EDFD","#F8FCFF","#C7E8FA","#1974A3",
                    "#053A66","#11BFC5","#03BEC5","#08A4AF"):
            self.assertNotIn(old,xaml)

    def test_all_um_controls_use_program_tokens_not_blue_hardcodes(self):
        content=(VIEWS/"UmUi.cs").read_text(encoding="utf-8")
        for token in ("SidebarBrush","BurgundyBrush","CreamPanelBrush",
                      "CreamTextBrush","GoldBrush","GoldBrightBrush"):
            self.assertIn('FromTheme("'+token+'"',content)
        for old in ("#072447","#0B4F82","#007E86",
                    "#F1F7FC","#BFCFDA","#E8F0F8","#AFC2D2"):
            self.assertNotIn(old,content)

    def test_reports_and_sidebar_use_shared_accent_palette(self):
        reports=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        nav=(VIEWS/"UserManagerView.cs").read_text(encoding="utf-8")
        self.assertIn("UmUi.Gold",reports)
        self.assertIn("UmUi.Gold",nav)
        self.assertNotIn("#0B8FB5",reports)
        self.assertNotIn("#254865",nav)

    def test_limitation_editor_never_rounds_existing_router_quota(self):
        plans=(VIEWS/"UserManagerPlansScreen.cs").read_text(encoding="utf-8")
        self.assertIn("UserManagerQuotaMath.ToEditorGiB(bytes)",plans)
        self.assertIn("UserManagerQuotaMath.ParseGiBToByteString(quota.Text)",plans)
        self.assertNotIn('ToString("0.###"',plans)

    def test_unknown_profile_prices_must_not_be_silently_overwritten(self):
        plans=(VIEWS/"UserManagerPlansScreen.cs").read_text(encoding="utf-8")
        self.assertIn('price.Text=row.Price=="غير متاح"?"":row.Price;',plans)
        self.assertIn('duration.Text=row.Validity=="غير متاح"?"":row.Validity;',plans)

    def test_unknown_payment_currencies_cannot_be_aggregated(self):
        reports=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        self.assertIn("UserManagerRevenueMath.TryTotalSingleCurrency(",reports)
        self.assertIn("else if(!validRevenue)",reports)

if __name__=="__main__":
    unittest.main()
