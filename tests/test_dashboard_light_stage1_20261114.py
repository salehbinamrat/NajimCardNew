"""Stage-one visual and interaction regression: light Najm dashboard, original router controls."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src/NajmCard.Desktop"
VIEWS = DESKTOP / "Views"
WPF = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

class DashboardLightStageOne(unittest.TestCase):
    def test_dashboard_remains_valid_xaml_and_displays_four_live_counts(self):
        path = VIEWS / "DashboardView.xaml"
        src = path.read_text(encoding="utf-8")
        root = ET.fromstring(src)
        names = [item.attrib.get(X + "Name") for item in root.iter()]
        for name in ("AllUsers", "Online", "ActiveHeadline", "ExpiredHeadline",
                     "Cpu", "MemoryUsePercent", "ProfileBars", "ActivityRows"):
            self.assertIn(name, names)
            self.assertEqual(1, names.count(name))
        self.assertNotIn("روابط سريعة", src)
        self.assertNotIn("QuickLinks", src)
        for handler in ("DisableHotspot_Click", "RestoreHotspot_Click",
                        "RebootRouter_Click", "Refresh_Click", "Lifecycle_Click"):
            self.assertIn('Click="' + handler + '"', src)

    def test_critical_router_actions_keep_their_existing_logic(self):
        src = (VIEWS / "DashboardView.xaml.cs").read_text(encoding="utf-8") + "\n" + (VIEWS / "DashboardView.RouterActions.cs").read_text(encoding="utf-8")
        for handler in ("DisableHotspot_Click", "RestoreHotspot_Click", "RebootRouter_Click"):
            self.assertIn(" " + handler + "(", src)
        self.assertIn("ActiveHeadline.Text", src)
        self.assertIn("ExpiredHeadline.Text", src)
        self.assertIn('ActiveHeadline.Text=ExpiredHeadline.Text="—";', src)
        self.assertIn("if(!state.IsComplete)", src)
        self.assertIn("_lifecycleReading", src[src.index("private async Task RefreshLiveTelemetryAsync()"):
                                              src.index("private async Task RefreshAsync()")])

    def test_light_shell_and_cream_controls_are_shared_without_dark_backgrounds(self):
        theme = ET.fromstring((DESKTOP / "Themes/BurgundyGold.xaml").read_text(encoding="utf-8"))
        brushes = {item.attrib["{http://schemas.microsoft.com/winfx/2006/xaml}Key"]:
                   item.attrib["Color"] for item in theme.findall(WPF + "SolidColorBrush")}
        self.assertEqual("#EEF3FB", brushes["CanvasBrush"])
        self.assertEqual("#0761F8", brushes["SidebarBrush"])
        self.assertEqual("#E8EDF5", brushes["CreamPanelBrush"])
        self.assertEqual("#000000", (VIEWS / "VoucherWorkspaceView.cs").read_text(
            encoding="utf-8").split('var maroon=(Brush)new BrushConverter().ConvertFrom("')[1][:7])
        shell = ET.fromstring((DESKTOP / "MainWindow.xaml").read_text(encoding="utf-8"))
        self.assertEqual("1260", shell.attrib["Width"])
        self.assertEqual("730", shell.attrib["Height"])
        self.assertIn('Background="{StaticResource SidebarBrush}"', (DESKTOP / "MainWindow.xaml").read_text(encoding="utf-8"))
        self.assertNotIn("روابط سريعة", (DESKTOP / "MainWindow.xaml").read_text(encoding="utf-8"))

if __name__ == "__main__":
    unittest.main()
