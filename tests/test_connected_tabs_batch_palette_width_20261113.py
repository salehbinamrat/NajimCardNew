"""WPF connected tabs, batch dropdown and owner-requested width contracts."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
D=ROOT/"src/NajmCard.Desktop"
V=D/"Views"
C=ROOT/"src/NajmCard.Core"

def source(root,relative):
    return (root/relative).read_text(encoding="utf-8")

class ConnectedTabsAndBatchStyle(unittest.TestCase):
    def test_connected_views_are_live_and_separate(self):
        ui=source(V,"SessionsView.cs")
        core=source(C,"Services/RouterGateway.LiveHotspotConnections.cs")
        for title in ('PageUi.Button("الكل"', 'PageUi.Button("متصلون اليوزرمنجر"',
                      'PageUi.Button("متصلون الهوتسبوت"'):
            self.assertIn(title,ui)
        self.assertIn('row.Origin==HotspotConnectionOrigin.UserManagerRadius',ui)
        self.assertIn('row.Origin==HotspotConnectionOrigin.LocalHotspot',ui)
        self.assertIn('ReadLiveHotspotConnectionsAsync(request.Token)',ui)
        self.assertIn('grid.ItemsSource=results',ui)
        self.assertIn('/ip/hotspot/active/print',core)
        self.assertNotIn('/tool/user-manager/session/print',core)
        self.assertIn('SourceOf(row)',core)
        self.assertIn('HotspotConnectionOrigin.Unknown',core)

    def test_batch_print_dropdown_has_white_text_on_light_burgundy(self):
        ui=source(V,"PrintingView.BurgundyDropdowns.cs")
        selectors=source(V,"PrintingView.PrintSelectors.cs")
        printing=source(V,"PrintingView.cs")
        self.assertIn('ConvertFrom("#AF5472")',ui)
        self.assertIn('ConvertFrom("#C47490")',ui)
        self.assertIn('Control.ForegroundProperty,Brushes.White',ui)
        self.assertIn('caption.SetValue(TextBlock.ForegroundProperty,Brushes.White)',ui)
        self.assertIn('selector.Style=style',ui)
        self.assertIn('selector.ItemContainerStyle=BurgundyDropdownItemStyle()',ui)
        self.assertGreaterEqual(selectors.count('Style=BurgundyDropdownItemStyle()'),3)
        self.assertIn('ApplyBurgundyDropdown(choice)',printing)
        self.assertIn('new[]{paper,orientation,order,batchSelector,selectedPrinter}',printing)
        self.assertIn('GetPrintQueues',selectors)

    def test_main_window_fixed_width_is_1260_with_height_unchanged(self):
        x=ET.parse(D/"MainWindow.xaml").getroot()
        for key in ('Width','MinWidth','MaxWidth'):
            self.assertEqual(x.attrib[key],"1260")
        for key in ('Height','MinHeight','MaxHeight'):
            self.assertEqual(x.attrib[key],"730")
        self.assertEqual(x.attrib["ResizeMode"],"CanMinimize")

if __name__=="__main__":
    unittest.main()
