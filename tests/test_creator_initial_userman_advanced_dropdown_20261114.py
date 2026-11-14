"""Regression contracts for the creator's selected service, geometry and advanced palette.

These source-level checks supplement the Windows WPF build and visual smoke
checks; they are not a substitute for inspecting an interactive Windows GUI.
"""
from pathlib import Path
import re
import unittest

ROOT=Path(__file__).resolve().parents[1] / "src" / "NajmCard.Desktop" / "Views"

def read(name):
    return (ROOT/name).read_text(encoding="utf-8")

class CreatorDefaultAndAdvancedEditorTests(unittest.TestCase):
    def test_user_manager_is_first_and_selected_before_view_is_shown(self):
        creator=read("GeneratorView.cs")
        workspace=read("VoucherWorkspaceView.cs")
        self.assertLess(creator.index('_service.Items.Add("User Manager v6")'),
                        creator.index('_service.Items.Add("Hotspot")'))
        self.assertLess(creator.index('_service.Items.Add("Hotspot")'),
                        creator.index('_service.Items.Add("PPPoE")'))
        self.assertIn('_service.SelectedIndex = 0;',creator)
        self.assertIn('0 => "userman"',creator)
        self.assertIn('1 => "hotspot"',creator)
        self.assertIn('"userman"=>0',creator)
        self.assertIn('"hotspot"=>1',creator)
        self.assertLess(workspace.index('("userman","يوزمنجر v6")'),
                        workspace.index('("hotspot","هوتسبوت")'))
        self.assertLess(workspace.index('("hotspot","هوتسبوت")'),
                        workspace.index('("pppoe","برودباند PPPoE")'))
        self.assertLess(workspace.index('creator.ServiceChanged+=PaintTabs;'),
                        workspace.index('creator.SelectPackageOnLoad(requestedService,requestedProfile);'))
        self.assertIn('creator.CurrentService',workspace)

    def test_creator_input_fields_have_one_visible_height(self):
        workspace=read("VoucherWorkspaceView.cs")
        editor=read("GeneratorView.DesignerPanel.cs")
        self.assertIn('new Setter(FrameworkElement.MinHeightProperty,30d)',workspace)
        self.assertIn('new Setter(FrameworkElement.MaxHeightProperty,30d)',workspace)
        self.assertIn('field.Height=30;field.MinHeight=30;field.MaxHeight=30;',editor)
        self.assertIn('btn.Height=31;',workspace)
        self.assertIn('panel.VerticalAlignment=VerticalAlignment.Stretch;',read("GeneratorView.ReferenceLayout.cs"))

    def test_advanced_colors_are_real_dropdowns_with_names_and_swatch(self):
        source=read("GeneratorView.DesignerPanel.cs")
        section=source[source.index('TextBox ColorChoice('):source.index('var name=Editable(',source.index('TextBox ColorChoice('))]
        self.assertIn('new ComboBox',section)
        self.assertIn('new ComboBoxItem',section)
        self.assertIn('ColorChoice',source)
        self.assertIn('entry.Visibility=Visibility.Collapsed',section)
        self.assertIn('entry.Text=selected',section)
        self.assertIn('item.Content',section)
        self.assertNotIn('palette.Children.Add(square)',section)
        self.assertNotIn('entry.Text=key',section)
        for label in ("لون خلفية البطاقة","لون رقم الكرت","لون بقية النصوص","لون الحدود"):
            self.assertIn('ColorChoice("'+label+'"',source)
        self.assertIn('("#FFF6EB"',source)
        self.assertIn('Hex',section)

    def test_advanced_dialog_has_grouped_equal_sized_fields_and_safe_save(self):
        source=read("GeneratorView.DesignerPanel.cs")
        self.assertIn('Group("بيانات الكرت"',source)
        self.assertIn('Group("ألوان الكرت"',source)
        self.assertIn('Group("الخطوط والمقاسات"',source)
        self.assertIn('Group("مقاسات الطباعة"',source)
        self.assertIn('MinHeight=72',source)
        self.assertIn('DockPanel.SetDock(saveRow,Dock.Bottom)',source)
        self.assertIn('popup.Closed+=',source)
        self.assertIn('ApplyCardAppearance(originalAppearance)',source)
        self.assertIn('CardPrintLayoutPlanner.Calculate(newPrint,1)',source)
        self.assertIn('ApplyCardAppearance(ReadCurrentAppearance())',source)
        self.assertIn('if(newPrint is not null)ApplyPrintOptions?.Invoke(newPrint)',source)
        self.assertIn('ChooseTemplateImage()',source)

if __name__=="__main__":
    unittest.main()
