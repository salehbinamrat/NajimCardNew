"""User requested: three active-session views, explicit burgundy batch list,
and a fixed 1260x730 window while preserving existing footer."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/"src/NajmCard.Desktop"

def read(relative):
    return (SRC/relative).read_text(encoding="utf-8")

class ConnectedSourcesBatchAndSize(unittest.TestCase):
    def test_online_tabs_and_independent_loads(self):
        v=read("Views/SessionsView.cs")
        core=(ROOT/"src/NajmCard.Core/Models/ConnectedUsersProjection.cs").read_text(encoding="utf-8")
        for label in ("الكل","متصلون اليوزرمنجر","متصلون الهوتسبوت"):
            self.assertIn('PageUi.Button("'+label+'"',v)
        self.assertIn("ReadSessionsAsync(token.Token)",v)
        self.assertIn("ReadUmSessionsAsync(token.Token)",v)
        self.assertIn("ConnectedUsersProjection.ForView(selected,",v)
        self.assertIn("s.IsActive==true",core)
        self.assertIn('view=="userman"',core)
        self.assertIn('view=="hotspot"',core)
        self.assertIn('Source="الهوتسبوت + اليوزر مانجر"',core)
        self.assertIn("if(um.Address!=\"غير متاح\")",core)

    def test_batch_only_light_burgundy_white_and_rebuild_preserves_selection(self):
        v=read("Views/PrintingView.PrintSelectors.cs")
        self.assertIn('ConvertFrom("#B75D7D")',v)
        self.assertIn('ConvertFrom("#C87692")',v)
        self.assertIn("new Style(typeof(ComboBoxItem))",v)
        self.assertIn("ContentSourceProperty,\"Content\"",v)
        self.assertIn("rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White))",v)
        self.assertIn("ComboBoxItem BatchChoice(",v)
        self.assertIn("batchSelector.Items.Add(BatchChoice(",v)
        self.assertIn("Tag=saved,Style=rowStyle",v)
        self.assertIn("previous.BatchId",v)
        self.assertIn("previous.Service",v)
        self.assertNotIn("selectedPrinter.Style=batchStyle",v)

    def test_window_is_1260x730_fixed_and_footer_preserved(self):
        window=ET.parse(SRC/"MainWindow.xaml").getroot()
        for name in ("Width","MinWidth","MaxWidth"):
            self.assertEqual("1260",window.attrib[name])
        for name in ("Height","MinHeight","MaxHeight"):
            self.assertEqual("730",window.attrib[name])
        self.assertEqual("CanMinimize",window.attrib["ResizeMode"])
        self.assertIn("DockPanel.SetDock(footer,Dock.Bottom)",
                      read("Views/VoucherWorkspaceView.cs"))

if __name__=="__main__":unittest.main()
