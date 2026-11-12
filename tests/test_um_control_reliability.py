"""Regression contracts for UM contrast, menu uniqueness and safe plan selection."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"

class UmControlReliabilityTests(unittest.TestCase):
    def test_local_button_template_uses_actual_foreground(self):
        code=(VIEWS/"UmUi.cs").read_text(encoding="utf-8")
        self.assertIn('nameof(Button.Foreground)',code)
        self.assertIn('nameof(Button.Content)',code)
        self.assertIn('ComboBoxItem',code)
        self.assertIn('PART_Popup',code)
        self.assertIn('PART_SelectedText',code)
        self.assertIn('Ink',code)
        self.assertIn('Brushes.White',code)

    def test_dropdown_items_have_explicit_text_and_selection_contrast(self):
        code=(VIEWS/"UmUi.cs").read_text(encoding="utf-8")
        self.assertIn('ItemContainerStyle',code)
        self.assertIn('IsHighlighted',code)
        self.assertIn('IsSelected',code)
        self.assertIn('ItemText',code)
        self.assertIn('TextBlock.ForegroundProperty',code)

    def test_sidebar_has_no_redundant_legacy_destination(self):
        code=(VIEWS/"UserManagerView.cs").read_text(encoding="utf-8")
        self.assertNotIn('("legacy","',code)
        self.assertIn('UserManagerAccountsScreen(gateway',code)
        # Advanced operations still available from the users page.
        self.assertIn('OpenExplorer(UserManagerCatalogSection.Users)',code)

    def test_reports_and_maintenance_use_single_shared_back(self):
        reports=(VIEWS/"UserManagerReportsView.cs").read_text(encoding="utf-8")
        maint=(VIEWS/"UserManagerMaintenanceView.cs").read_text(encoding="utf-8")
        for source in (reports,maint):
            self.assertNotIn('returnButton=UmUi.Action(',source)
        self.assertNotIn('UmUi.Action("النسخ الاحتياطية",true)',maint)

    def test_profile_selection_survives_redraw_without_overwriting_edited_fields(self):
        code=(VIEWS/"UserManagerPlansScreen.cs").read_text(encoding="utf-8")
        self.assertIn('suppressSelectionChanged',code)
        self.assertIn('try',code)
        self.assertIn('finally',code)
        self.assertIn('editing=null;',code)
        self.assertIn('Selected()',code)
        self.assertIn('table.SelectedItem=',code)

    def test_wpf_smoke_validates_real_selected_button_and_combo_text(self):
        smoke=(ROOT/"tests/NajmCard.VisualSmoke/Program.cs").read_text(encoding="utf-8")
        self.assertIn('VerifyUmControlContrast',smoke)
        self.assertIn('PART_SelectedText',smoke)
        self.assertIn('Foreground',smoke)

if __name__=="__main__":
    unittest.main()
