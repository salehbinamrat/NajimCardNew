"""UM user-requested regression contracts: accurate counts, useful tables,
and independent recovery when historical profile/limitation links are orphaned."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]
CORE=ROOT/"src/NajmCard.Core/Services"
VIEWS=ROOT/"src/NajmCard.Desktop/Views"

def read(root,name):
    return (root/name).read_text(encoding="utf-8")

class UserManagerPagesRegression(unittest.TestCase):
    def test_orphaned_links_are_reported_without_hiding_real_profiles(self):
        source=read(CORE,"RouterGateway.UserManagerLiveRows.cs")
        self.assertIn("unresolved++;",source)
        self.assertIn("IsComplete=unresolved==0",source)
        self.assertIn("Diagnostic=unresolved==0",source)
        self.assertNotIn("وجدت روابط Profile/Limitations غير معروفة؛ أُوقف عرض الباقات.",source)

    def test_report_independent_of_profile_relation_read_failure(self):
        src=read(VIEWS,"UserManagerReportsView.cs")
        self.assertLess(src.index("var connections=await gateway.ReadUmSessionsAsync("),
                        src.index("try\n                {\n                    plans=await gateway.ReadUmPlansAsync("))
        self.assertIn("planWarning=\"تعذرت قراءة جدول الباقات:",src)
        self.assertIn("users.Rows.Select(x=>x.Profile)",src)
        self.assertNotIn("counts.Take(6)",src)

    def test_single_card_session_history_has_real_total_and_paging(self):
        src=read(VIEWS,"UserManagerAccountsScreen.cs")
        self.assertIn("var sessionGrid=UmUi.Table(",src)
        self.assertIn("history.Length:N0",src)
        self.assertIn("filtered.Skip(sessionPage*size).Take(size)",src)
        self.assertIn("ReadUmSessionsForAccountAsync(",src)
        gateway=read(CORE,"RouterGateway.UserManagerLiveRows.cs")
        self.assertIn("string.Equals(row.Name,username,StringComparison.OrdinalIgnoreCase)",gateway)
        self.assertIn('ReadUmSessionsForAccountAsync(',gateway)
        self.assertIn("عدد سجلات الكرت على الراوتر",src)
        self.assertIn("UmCsv.Export(picker.FileName",src)
        self.assertNotIn("string.Join(\"\\n\",history.Take(20)",src)

    def test_packages_editor_is_below_full_width_table(self):
        src=read(VIEWS,"UserManagerPlansScreen.cs")
        self.assertIn("var inventoryPanel=UmUi.Panel(tableArea)",src)
        self.assertIn("var editorPanel=UmUi.Panel(",src)
        self.assertLess(src.index("header,inventoryPanel,"),src.index("editorPanel,UmUi.Panel(status)"))
        self.assertNotIn("contentGrid.ColumnDefinitions",src)

    def test_dashboard_shows_every_current_package_and_explains_metric(self):
        src=read(VIEWS,"DashboardView.xaml.cs")
        markup=read(VIEWS,"DashboardView.xaml")
        self.assertIn(".OrderByDescending(x=>x.Used)",src)
        self.assertIn(".Take(4).ToArray()",src)
        package_view=read(VIEWS,"DashboardView.PackageStatistics.cs")
        self.assertIn("private void AllPackages_Click(",package_view)
        self.assertIn("inventory.Users",package_view)
        self.assertIn("g.Count(x=>x.Started)",package_view)
        self.assertIn("x=>x.Started &&",src)
        self.assertIn('Text=$"بدأ استخدامها: {p.Used:N0}"',src)
        self.assertIn('Text=$"إجمالي الكروت: {p.Count:N0}"',src)
        self.assertIn('Content="عرض جميع الباقات"',markup)
        self.assertNotIn("<ScrollViewer",markup)
        self.assertIn("أكثر الباقات استخداما | عدد الكروت",markup)
        self.assertIn('x:Name="ProfileBars" Columns="2" Rows="2"',markup)

    def test_calendar_days_have_scoped_legible_foreground(self):
        src=read(VIEWS,"UmUi.cs")
        self.assertIn("var days=new Style(typeof(CalendarDayButton))",src)
        self.assertIn("CalendarStyle=calendar",src)
        self.assertIn("ForegroundProperty,Ink",src)

if __name__=="__main__":
    unittest.main()
