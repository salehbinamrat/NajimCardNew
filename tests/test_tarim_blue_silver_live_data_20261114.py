"""Contract for Tarim Card branding, blue/silver UI and bounded dashboard load."""
import re
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
D=ROOT/"src/NajmCard.Desktop"
def content(p):return p.read_text(encoding="utf-8")

class TarimCardVisualAndPerformanceContract(unittest.TestCase):
    def test_brand_is_tarim_but_original_data_paths_and_binary_survive(self):
        shell=content(D/"MainWindow.xaml")
        code=content(D/"MainWindow.xaml.cs")
        setup=content(ROOT/"installer/NajmCard.iss")
        shortcuts=content(D/"Services/DesktopShortcutService.cs")
        project=content(D/"NajmCard.Desktop.csproj")
        self.assertIn('Title="تريم كارد | إدارة الشبكات وطباعة الكروت"',shell)
        self.assertIn('Text="تريم كارد"',shell)
        self.assertIn('"حول تريم كارد"',code)
        self.assertIn("AppName=تريم كارد",setup)
        self.assertIn('LinkName="تريم كارد.lnk"',shortcuts)
        self.assertIn("<Product>تريم كارد</Product>",project)
        # Do not break licensing state, existing data or Windows deployment.
        self.assertIn("<AssemblyName>NajmCard</AssemblyName>",project)
        self.assertIn("DefaultDirName={localappdata}\\Programs\\NajmCardNew",setup)
        self.assertIn("AppId={{CF6AAB62-DA00-49AF-B3D2-1DD83FF90143}",setup)

    def test_theme_uses_electric_blue_with_silver_panels_not_burgundy_cream(self):
        theme=ET.parse(D/"Themes/BurgundyGold.xaml").getroot()
        x="{http://schemas.microsoft.com/winfx/2006/xaml}Key"
        solids={n.attrib[x]:n.attrib["Color"] for n in theme
                if n.tag.endswith("SolidColorBrush") and x in n.attrib}
        self.assertEqual("#075DFF",solids["BurgundyBrush"])
        self.assertEqual("#075DFF",solids["MaroonLineBrush"])
        self.assertEqual("#E8EDF5",solids["CreamPanelBrush"])
        self.assertEqual("#0B2C60",solids["CreamTextBrush"])
        self.assertEqual("#0761F8",solids["SidebarBrush"])
        self.assertEqual("#DDE5F1",solids["SurfaceRaisedBrush"])
        self.assertIn('Color="#A6BCD7"',content(D/"Themes/BurgundyGold.xaml"))
        self.assertIn('Background="{StaticResource CreamPanelBrush}"',
                      content(D/"Themes/BurgundyGold.xaml"))

    def test_sidebar_is_blue_silver_and_existing_colored_icons_remain(self):
        shell=content(D/"MainWindow.xaml")
        ui=content(D/"MainWindow.xaml.cs")
        self.assertIn('Background="{StaticResource SidebarBrush}"',shell)
        self.assertIn('BorderBrush="{StaticResource MaroonLineBrush}"',shell)
        self.assertIn('IconBackground',ui)
        self.assertIn('IconDashboard',ui)
        self.assertIn('IconPeople',ui)

    def test_router_polling_is_bounded_and_heavy_reads_do_not_repeat_on_reentry(self):
        dashboard=content(D/"Views/DashboardView.xaml.cs")
        self.assertIn('Interval=TimeSpan.FromSeconds(10)',dashboard)
        self.assertIn('while(_refreshing',dashboard)
        self.assertIn('_initialLifecycleAttempted=true;',dashboard)
        self.assertIn('ReadCachedUserManagerV6InventoryAsync(force,cts.Token)',dashboard)
        self.assertIn('if(_lifecycleReading || !IsLoaded)return;',dashboard)

if __name__=="__main__":unittest.main()
