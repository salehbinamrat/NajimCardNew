"""Dashboard/maintenance Hotspot toggles must converge on exact-ID bulk server UI."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"
CORE=ROOT/"src/NajmCard.Core/Services"

class HotspotServerBulkContract(unittest.TestCase):
    def test_dashboard_buttons_open_server_page_not_a_second_write_path(self):
        actions=(VIEWS/"DashboardView.RouterActions.cs").read_text(encoding="utf-8")
        self.assertIn('private void DisableHotspot_Click',actions)
        self.assertIn('private void RestoreHotspot_Click',actions)
        self.assertEqual(actions.count('_navigate("servers");'),2)
        self.assertNotIn('ChangeHotspotServerStateAsync(',actions)
        self.assertIn('RequestRebootAsync()',actions)

    def test_server_table_allows_individual_multi_and_all_selection(self):
        view=(VIEWS/"ServersView.cs").read_text(encoding="utf-8")
        for contract in ('DataGridSelectionMode.Extended','DataGridTemplateColumn',
                         'grid.SelectAll()','grid.UnselectAll()',
                         'ChangeHotspotServersStateAsync(chosen,disabled)',
                         'grid.ItemsSource=null;','await Load();'):
            self.assertIn(contract,view)

    def test_batch_mutations_are_verified_sequential_and_fail_closed(self):
        source=(CORE/"RouterGateway.HotspotMaintenance.cs").read_text(encoding="utf-8")
        self.assertIn('foreach(var server in changing)',source)
        self.assertIn('await ChangeHotspotServerStateAsync(server,disabled,ct)',source)
        self.assertIn('if(!result.Success)',source)
        self.assertNotIn('Task.WhenAll(changing',source)

if __name__=="__main__":
    unittest.main()
