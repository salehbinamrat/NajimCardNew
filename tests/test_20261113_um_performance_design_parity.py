"""Contracts for v6 creator catalog caching, verified sessions and card appearance."""
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
D=ROOT/"src/NajmCard.Desktop"
C=ROOT/"src/NajmCard.Core"
def src(root,name):return (root/name).read_text(encoding="utf-8")

class UserManagerPerformanceAndAppearance(unittest.TestCase):
    def test_profile_catalog_is_reused_on_reentry_and_refresh_bypasses_it(self):
        cache=src(C,"Services/RouterGateway.CachedCreatorCatalog.cs")
        loading=src(D,"Views/GeneratorView.ProfileLoading.cs")
        self.assertIn("CreatorCatalogTtl=TimeSpan.FromMinutes(2)",cache)
        self.assertIn("ReadCachedCreatorProfilesAsync(force)",loading)
        self.assertIn("ReadCachedCreatorCustomersAsync(force)",loading)
        self.assertIn("ReadUserManagerV6ProfilesAsync(ct)",cache)
        self.assertIn("ReadUserManagerV6CustomersAsync(ct)",cache)
        self.assertIn("DateTimeOffset.UtcNow-_lastCatalogLoadedAt<TimeSpan.FromMinutes(2)",loading)

    def test_dashboard_does_not_download_historical_sessions_to_display_zero(self):
        view=src(D,"Views/DashboardView.xaml.cs")
        classifier=src(C,"Services/UserManagerV6Usage.cs")
        self.assertIn("Interval=TimeSpan.FromSeconds(3)",view)
        self.assertIn("ReadCachedUserManagerV6InventoryAsync(force,cts.Token)",view)
        self.assertIn("ActiveSessionsVerified",view)
        self.assertIn("row.ContainsKey(\"active-sessions\")",classifier)
        self.assertIn('Text=$"الجلسات: {p.Sessions}"',view)
        self.assertNotIn("ReadUmSessionsAsync",view)

    def test_color_squares_are_always_visible_and_share_inline_palette(self):
        designer=src(D,"Views/GeneratorView.DesignerPanel.cs")
        self.assertIn("CardPaletteColors",designer)
        self.assertIn("foreach(var hex in CardPaletteColors)",designer)
        self.assertIn("palette.Children.Add(square)",designer)
        self.assertIn("entry.Text=key",designer)
        self.assertIn("currentSwatch.Background",designer)
        self.assertIn("Foreground=Brushes.White",designer)
        self.assertIn('ShapeCell("حجم خط العناصر",elementSizes,4)',designer)
        self.assertIn('ShapeCell("لون الحدود"',designer)

    def test_password_preview_and_pdf_share_username_size_and_color(self):
        design=src(D,"Views/GeneratorView.CardDesign.cs")
        printer=src(D,"Views/PrintingView.cs")
        model=src(C,"Models/VoucherCardAppearance.cs")
        self.assertIn("_previewPassword.FontSize=appearance.UsernameFontSize",design)
        self.assertIn("_previewPassword.Foreground=numberInk",design)
        self.assertIn("appearance.UsernameFontSize,appearance.ShowPassword,credential:true",printer)
        self.assertIn("Foreground=credential?numberInk:ink",printer)
        self.assertIn("ElementScale = 1",model)
        self.assertIn("appearance.ElementScale",printer)

    def test_page_heading_is_right_and_status_is_left(self):
        markup=src(D,"MainWindow.xaml")
        dash=src(D,"Views/DashboardView.xaml")
        ET.fromstring(markup);ET.fromstring(dash)
        self.assertIn('Grid.Column="1" HorizontalAlignment="Right"',markup)
        self.assertIn('Grid.Column="0" Orientation="Horizontal"',markup)
        self.assertIn('Grid.Column="1" Text="الرئيسية" HorizontalAlignment="Right"',dash)

    def test_um_editor_fields_stretch_in_two_horizontal_rows(self):
        s=src(D,"Views/UserManagerPlansScreen.cs")
        self.assertIn("Columns=4,Rows=2",s)
        self.assertIn("control.HorizontalAlignment=HorizontalAlignment.Stretch",s)
        self.assertIn("header,inventoryPanel,",s)

if __name__=="__main__":
    unittest.main()
