"""Visual regression contracts for light-burgundy dropdowns and gold licence indicator.
Do not alter trial duration, server licensing state or card template behavior.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
DESKTOP=ROOT/"src/NajmCard.Desktop"
VIEWS=DESKTOP/"Views"
NS={"w":"http://schemas.microsoft.com/winfx/2006/xaml/presentation",
    "x":"http://schemas.microsoft.com/winfx/2006/xaml"}

class BurgundyPopupGoldLicenceTests(unittest.TestCase):
    def test_template_popup_white_text_normal_and_hover(self):
        s=(VIEWS/"GeneratorView.DesignerPanel.cs").read_text(encoding="utf-8")
        self.assertIn('ConvertFrom("#AF5472")',s)
        self.assertIn('ConvertFrom("#C47490")',s)
        self.assertIn('new Setter(Control.ForegroundProperty,Brushes.White)',s)
        self.assertIn('label.SetValue(TextElement.ForegroundProperty,Brushes.White)',s)
        self.assertIn('Header="قالب نجم نت الافتراضي",Style=themeItemStyle',s)
        self.assertIn('Header="اختيار صورة القالب",Style=themeItemStyle',s)
        self.assertIn("MenuItem.IsHighlightedProperty",s)
        self.assertIn("ChooseTemplateImage()",s)

    def test_um_calendar_popup_is_burgundy_white_without_changing_date_entry(self):
        s=(VIEWS/"UmUi.cs").read_text(encoding="utf-8")
        self.assertIn('var lightBurgundy=Solid("#AF5472")',s)
        self.assertIn('var highlightedBurgundy=Solid("#C47490")',s)
        self.assertIn("Calendar.CalendarDayButtonStyleProperty,days",s)
        self.assertIn("Calendar.CalendarButtonStyleProperty,monthButtons",s)
        self.assertIn("Calendar.CalendarItemStyleProperty,calendarItem",s)
        self.assertIn("Control.ForegroundProperty,Brushes.White",s)
        self.assertIn("Foreground=Ink,Background=Light",s)
        self.assertIn("CalendarStyle=calendar",s)

    def test_sidebar_has_gold_surface_burgundy_text_and_vector_icons(self):
        root=ET.parse(DESKTOP/"MainWindow.xaml").getroot()
        label=root.find(".//w:TextBlock[@{http://schemas.microsoft.com/winfx/2006/xaml/presentation}Name='SidebarLicense']",NS)
        # x:Name uses the XAML namespace, not the presentation namespace.
        label=root.find(".//w:TextBlock[@{http://schemas.microsoft.com/winfx/2006/xaml}Name='SidebarLicense']",NS)
        icon=root.find(".//w:Path[@{http://schemas.microsoft.com/winfx/2006/xaml}Name='SidebarLicenseIcon']",NS)
        self.assertIsNotNone(label)
        self.assertIsNotNone(icon)
        self.assertEqual("#591C39",label.attrib["Foreground"])
        self.assertEqual("#591C39",icon.attrib["Stroke"])
        self.assertIn("F9E5AE",(DESKTOP/"MainWindow.xaml").read_text(encoding="utf-8"))
        code=(DESKTOP/"MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('SidebarLicenseIcon.ToolTip="عداد التجربة المجانية"',code)
        self.assertIn('SidebarLicenseIcon.ToolTip="الترخيص المدفوع المفعل"',code)
        self.assertIn('SidebarLicenseIcon.Data=Geometry.Parse(',code)
        self.assertIn('SidebarLicense.Text="الترخيص المدفوع · المتبقي',code)
        self.assertIn('SidebarLicense.Text="التجربة المجانية · المتبقي',code)

if __name__=="__main__":
    unittest.main()
