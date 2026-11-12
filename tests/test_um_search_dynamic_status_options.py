"""User Manager search must show only actual, unique statuses of selected catalog."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"

class UserManagerSearchStatusContract(unittest.TestCase):
    def test_status_options_are_derived_from_real_router_rows(self):
        code=(VIEWS/"UserManagerSearchScreen.cs").read_text(encoding="utf-8")
        self.assertIn('UmUi.Combo(new[]{"جميع الحالات"}',code)
        self.assertIn('items.Select(x=>x.Status)',code)
        self.assertIn('Distinct(StringComparer.Ordinal)',code)
        self.assertIn('OrderBy(x=>x,StringComparer.Ordinal)',code)
        self.assertIn('status.Items.Add(value)',code)
        self.assertNotIn('new[]{"جميع الحالات","نشطة","منتهية","معطل"',code)

    def test_user_accounts_filters_include_every_actual_status_and_profile(self):
        code=(VIEWS/"UserManagerAccountsScreen.cs").read_text(encoding="utf-8")
        self.assertIn('UmUi.Combo(new[]{"كل الحالات"}',code)
        self.assertIn('all.Select(x=>x.Status)',code)
        self.assertIn('all.Select(x=>x.Profile)',code)
        self.assertIn('.Distinct(StringComparer.Ordinal)',code)
        self.assertIn('void SyncFilters()',code)
        self.assertIn('statusFilter.IsEnabled=all.Count>0&&!busy;',code)
        self.assertIn('profileFilter.IsEnabled=all.Count>0&&!busy;',code)
        self.assertNotIn('new[]{"كل الحالات","صالح","استخدم","باقة نشطة"',code)

    def test_after_edit_and_failed_load_stale_account_filters_are_cleared(self):
        code=(VIEWS/"UserManagerAccountsScreen.cs").read_text(encoding="utf-8")
        self.assertIn('all=[];version=null;SyncFilters();Draw();',code)
        self.assertIn('if(resetFilters)return;',code)

    def test_only_meaningful_catalogs_can_filter_by_status(self):
        code=(VIEWS/"UserManagerSearchScreen.cs").read_text(encoding="utf-8")
        self.assertIn('x!="من الراوتر"',code)
        self.assertIn('status.IsEnabled=options.Length>0&&!busy;',code)
        self.assertIn('status.IsEnabled=false;',code)

    def test_changing_source_or_failed_load_invalidates_previous_state(self):
        code=(VIEWS/"UserManagerSearchScreen.cs").read_text(encoding="utf-8")
        self.assertIn('source.SelectionChanged+=(_,_)=>',code)
        self.assertIn('items=[];page=0;SyncStatuses();Draw();',code)
        self.assertIn('items=[];SyncStatuses();Draw();',code)
        self.assertIn('if(resetStatus)return;',code)

if __name__=="__main__":
    unittest.main()
