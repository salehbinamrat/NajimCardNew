"""Regression contract for read-only live connection filtering performance."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
VIEWS=ROOT/"src/NajmCard.Desktop/Views"

class ConnectionsUiLatencyTests(unittest.TestCase):
    def test_live_connections_do_not_recount_on_every_search_character(self):
        src=(VIEWS/"SessionsView.cs").read_text(encoding="utf-8")
        self.assertIn("UmUi.Debounce(search,grid,Draw",src)
        self.assertNotIn("search.TextChanged+=(_,_)=>Draw();",src)
        self.assertIn("void CountConnections()",src)
        self.assertIn("CountConnections();",src)
        draw=src[src.index("void Draw()"):src.index("async Task Load()")]
        self.assertNotIn("connections.Count(row=>",draw)

    def test_card_management_filters_wait_for_typing_to_stop(self):
        src=(VIEWS/"CardsView.cs").read_text(encoding="utf-8")
        self.assertIn("UmUi.Debounce(search,_table,",src)
        self.assertIn("UmUi.Debounce(umSearch,_umGrid,",src)
        self.assertNotIn("search.TextChanged+=(_,_)=>{page=0;FilterHotspot();};",src)
        self.assertNotIn("umSearch.TextChanged+=(_,_)=>{umPage=0;FilterUm();};",src)

    def test_incomplete_snapshot_never_treated_as_complete(self):
        code=(ROOT/"src/NajmCard.Core/Services/RouterGateway.UserManagerLiveRows.cs").read_text(
            encoding="utf-8")
        self.assertIn("IsComplete=inventory.IsComplete",code)
        self.assertIn("Diagnostic=inventory.Diagnostic",code)

if __name__=="__main__":unittest.main()
