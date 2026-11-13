"""Owner-requested UI, RouterOS 6 and statistics regression contracts."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
VIEW=ROOT/"src/NajmCard.Desktop/Views"
CORE=ROOT/"src/NajmCard.Core/Services"

def read(base,file): return (base/file).read_text(encoding="utf-8")

class UserManagerV6VisualParity(unittest.TestCase):
    def test_advanced_dropdowns_use_custom_editable_white_on_burgundy(self):
        s=read(VIEW,"GeneratorView.DesignerPanel.cs")
        self.assertIn('TextBox Choices(string label,string current,string[] presets)',s)
        self.assertIn('ConvertFrom("#AF5472")',s)
        self.assertIn('entry.Foreground=Brushes.White',s)
        self.assertIn('Foreground=Brushes.White,Background=burgundy',s)
        self.assertIn('popupChoices.IsOpen=!popupChoices.IsOpen',s)
        self.assertIn('entry.ToolTip="اختر من القائمة أو أدخل قيمة مخصصة."',s)
        self.assertNotIn('IsEditable=true,ItemsSource=presets',s)

    def test_advanced_colors_use_exact_existing_card_palette(self):
        s=read(VIEW,"GeneratorView.DesignerPanel.cs")
        self.assertIn('TextBox ColorChoice(string label,string current)',s)
        self.assertIn('foreach(var hex in CardPaletteColors)',s)
        self.assertIn('var numberColor=ColorChoice(',s)
        self.assertIn('var background=ColorChoice(',s)
        self.assertIn('var borderColor=ColorChoice(',s)

    def test_um_plan_editor_uses_four_across(self):
        s=read(VIEW,"UserManagerPlansScreen.cs")
        self.assertIn('Columns=4,Rows=2',s)
        self.assertIn('Field("سرعة التحميل TX",down)',s)
        self.assertIn('header,inventoryPanel,',s)
        self.assertNotIn('var editor=UmUi.Stack(UmUi.Label("إضافة / تعديل",17,true)',s)

    def test_per_card_session_falls_back_to_verified_account(self):
        core=read(CORE,"RouterGateway.UserManagerLiveRows.cs")
        panel=read(VIEW,"UserManagerAccountsScreen.cs")
        self.assertIn('ReadUmSessionsForAccountAsync(',core)
        self.assertIn('await client.CommandAsync("/tool/user-manager/session/print",null,ct)',core)
        self.assertIn('UmSessionField(row,v7?"user-address":"user-ip"',core)
        self.assertIn('ReadUmSessionsForAccountAsync(',panel)
        self.assertIn('("معرف الجهاز / MAC","CallingStation"',panel)
        self.assertIn('("نهاية الاتصال","Ended"',panel)
        self.assertIn('("عنوان IP","Address"',panel)

    def test_dashboard_numbers_are_labeled_as_cards_not_sessions(self):
        xml=read(VIEW,"DashboardView.xaml")
        render=read(VIEW,"DashboardView.xaml.cs")
        self.assertIn('x:Name="ProfileBars" Columns="2" Rows="2"',xml)
        self.assertIn('Text="أكثر الباقات استخداما | الباقة • الكروت • الجلسات"',xml)
        self.assertIn('Label($"عدد الكروت: {p.Used:N0}"',render)
        self.assertIn('ToolTip=$"الباقة: {p.Name} | الكروت التي بدأت استخدامها: {p.Used:N0}',render)
        self.assertIn('.OrderByDescending(g=>g.Used)',render)
        self.assertNotIn('barTrack.SizeChanged',render)

if __name__=="__main__":
    unittest.main()
