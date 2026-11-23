"""Release UI contracts: actual resource values, not just a theme file name."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / 'src/NajmCard.Desktop'
NS = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}


class ReleaseAppearance(unittest.TestCase):
    def test_long_string_tooltips_wrap_in_the_visible_frame(self):
        theme = ET.parse(DESKTOP / 'Themes/BurgundyGold.xaml').getroot()
        tooltip = next(s for s in theme.findall('w:Style', NS) if s.get('TargetType') == 'ToolTip')
        wrapping = tooltip.findall('.//w:TextBlock[@TextWrapping="Wrap"]', NS)
        self.assertTrue(wrapping, 'Long Arabic tooltips must wrap instead of losing their tail')

    def test_creator_service_selection_uses_electric_blue_and_silver(self):
        text = (DESKTOP / 'Views/VoucherWorkspaceView.cs').read_text()
        self.assertFalse('#EAC77C' in text, 'The active service still uses the retired gold palette')
        self.assertFalse('#49142C' in text, 'The active service still uses the retired burgundy text')

    def test_calendar_and_print_dropdowns_use_the_shared_identity(self):
        for name, colors in {
            'UmUi.cs': ('#AF5472', '#C47490', '#7D2A4C', '#823452'),
            'PrintingView.BurgundyDropdowns.cs': ('#FFF4DF', '#75203E'),
        }.items():
            with self.subTest(view=name):
                text = (DESKTOP / 'Views' / name).read_text()
                for color in colors:
                    self.assertFalse(color in text, 'A popup retains the retired identity')

    def test_editor_and_progress_surfaces_use_the_same_identity(self):
        for name in ('GeneratorView.AdvancedEditor.cs','GeneratorView.DesignerPanel.cs',
                     'GeneratorView.CardDesign.cs','PrintingView.cs','VoucherOperationProgressWindow.cs'):
            with self.subTest(view=name):
                text = (DESKTOP / 'Views' / name).read_text()
                for color in ('#FFF4DF','#75203E','#341326','#F4D18C'):
                    self.assertFalse('ConvertFrom("'+color+'")' in text)

    def test_windows_manifest_declares_monitor_aware_dpi(self):
        project = ET.parse(DESKTOP / 'NajmCard.Desktop.csproj').getroot()
        manifest = project.find('.//ApplicationManifest')
        self.assertIsNotNone(manifest, 'Windows must receive an explicit DPI awareness declaration')
        self.assertIn('PerMonitorV2', (DESKTOP / manifest.text).read_text())

    def test_about_dialog_uses_the_current_version_and_identity(self):
        source = (DESKTOP / 'MainWindow.xaml.cs').read_text()
        about = source.split('private void About_Click', 1)[1].split('private static string TrialNotice', 1)[0]
        self.assertNotIn('0.1.0', about)
        self.assertNotIn('عنابية', about)
        self.assertIn('ProductVersionLabel.Text', about)

    def test_progress_dialog_can_grow_for_long_failure_messages(self):
        source = (DESKTOP / 'Views/VoucherOperationProgressWindow.cs').read_text()
        self.assertIn('SizeToContent=SizeToContent.Height', source)
        self.assertNotIn('MaxHeight=176', source)


if __name__ == '__main__':
    unittest.main()
