"""Global WPF tooltip appearance must match the light NajmCard palette.

Checks run on both Windows release architectures before packaging. The visual
smoke test also instantiates a real WPF ToolTip and verifies its template.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

THEME=Path(__file__).resolve().parents[1]/"src/NajmCard.Desktop/Themes/BurgundyGold.xaml"
NS={"p":"http://schemas.microsoft.com/winfx/2006/xaml/presentation"}
def ss(node,prop):
    return next((x.attrib.get("Value","") for x in node.findall("p:Setter",NS)
                 if x.attrib.get("Property")==prop),None)

class TooltipPaletteRegression(unittest.TestCase):
    def test_tooltip_has_global_cream_style_not_windows_default_white(self):
        root=ET.parse(THEME).getroot()
        styles=[s for s in root.findall("p:Style",NS)
                if s.attrib.get("TargetType")=="ToolTip" and "Key" not in
                " ".join(s.attrib.keys())]
        self.assertEqual(1,len(styles),"Use one implicit ToolTip style for every screen.")
        tip=styles[0]
        self.assertEqual("{StaticResource CreamPanelBrush}",ss(tip,"Background"))
        self.assertEqual("{StaticResource CreamTextBrush}",ss(tip,"Foreground"))
        self.assertEqual("{StaticResource MaroonLineBrush}",ss(tip,"BorderBrush"))
        self.assertEqual("1",ss(tip,"BorderThickness"))
        self.assertEqual("Arial",ss(tip,"FontFamily"))
        template=tip.find(".//p:ControlTemplate",NS)
        self.assertIsNotNone(template)
        frame=template.find("p:Border",NS)
        self.assertIsNotNone(frame)
        self.assertEqual("TooltipFrame",frame.attrib.get(
            "{http://schemas.microsoft.com/winfx/2006/xaml}Name"))
        self.assertEqual("{TemplateBinding Background}",frame.attrib.get("Background"))
        self.assertEqual("{TemplateBinding BorderBrush}",frame.attrib.get("BorderBrush"))
        self.assertEqual("{TemplateBinding Foreground}",frame.attrib.get("TextElement.Foreground"))
        self.assertIsNotNone(frame.find("p:ContentPresenter",NS))

    def test_tooltip_keeps_content_without_erasing_or_hiding_all_tips(self):
        source=THEME.read_text(encoding="utf-8")
        self.assertIn('ControlTemplate TargetType="ToolTip"',source)
        self.assertIn('ContentSource="Content"',source)
        self.assertNotIn('ToolTipService.IsEnabled" Value="False"',source)

if __name__=="__main__":
    unittest.main()
