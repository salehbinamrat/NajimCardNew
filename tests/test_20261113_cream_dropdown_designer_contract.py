"""Owner-requested cream dropdown and advanced card designer visual contract."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src/NajmCard.Desktop"
VIEWS = DESKTOP / "Views"


def read(path):
    return path.read_text(encoding="utf-8")


class CreamDropdownAndDesignerTests(unittest.TestCase):
    def test_global_dropdowns_use_cream_black_and_maroon(self):
        theme = read(DESKTOP / "Themes/BurgundyGold.xaml")
        self.assertIn('<Style x:Key="DarkComboBox" TargetType="ComboBox">', theme)
        self.assertIn('<Setter Property="Background" Value="{StaticResource CreamPanelBrush}"/>', theme)
        self.assertIn('<Setter Property="Foreground" Value="#000000"/>', theme)
        self.assertIn('<Setter Property="BorderBrush" Value="{StaticResource MaroonLineBrush}"/>', theme)
        self.assertIn('<Border Background="{TemplateBinding Background}" BorderBrush="{StaticResource MaroonLineBrush}"', theme)
        self.assertIn('<Setter Property="Background" Value="{StaticResource CreamPanelBrush}"/>', theme)
        self.assertIn('<Setter Property="Foreground" Value="#000000"/>', theme)
        um = read(VIEWS / "UmUi.cs")
        self.assertIn('Foreground=Brushes.Black', um)
        self.assertIn('BorderBrush=FromTheme("MaroonLineBrush","#75203E")', um)
        self.assertIn('Control.ForegroundProperty,Brushes.Black', um)

    def test_print_batch_dropdowns_follow_same_palette(self):
        style = read(VIEWS / "PrintingView.BurgundyDropdowns.cs")
        self.assertIn('ConvertFrom("#FFF4DF")', style)
        self.assertIn('Control.ForegroundProperty,Brushes.Black', style)
        self.assertIn('ConvertFrom("#75203E")', style)
        self.assertNotIn('Brushes.White', style)

    def test_advanced_window_is_cream_with_maroon_frames_and_black_text(self):
        designer = read(VIEWS / "GeneratorView.DesignerPanel.cs")
        self.assertIn('Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!', designer)
        self.assertIn('Foreground=Brushes.Black', designer)
        self.assertIn('BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!', designer)
        self.assertIn('entry.Visibility=Visibility.Collapsed', designer)
        self.assertIn('palette.Children.Add(square)', designer)

    def test_color_fields_show_swatch_only_without_hex_input(self):
        designer = read(VIEWS / "GeneratorView.DesignerPanel.cs")
        start = designer.index('TextBox ColorChoice(')
        end = designer.index('var name=Editable(', start)
        block = designer[start:end]
        self.assertIn('entry.Visibility=Visibility.Collapsed', block)
        self.assertIn('entry.Text=key', block)
        self.assertNotIn('row.Children.Add(entry)', block)
        self.assertIn('palette.Children.Add(square)', block)

    def test_preview_card_option_frames_have_equal_dimensions(self):
        designer = read(VIEWS / "GeneratorView.DesignerPanel.cs")
        self.assertIn('new GridLength(1,GridUnitType.Star)', designer)
        self.assertIn('field.Height=30', designer)
        self.assertIn('field.MinHeight=30', designer)
        self.assertIn('field.MaxHeight=30', designer)

    def test_most_used_packages_show_only_package_and_used_card_count(self):
        dashboard = read(VIEWS / "DashboardView.xaml.cs")
        self.assertIn('var cards=Label($"عدد الكروت المستخدمة: {p.Used:N0}"', dashboard)
        self.assertNotIn('var sessions=Label(', dashboard)
        self.assertNotIn('الجلسات:', dashboard)
        stats = read(VIEWS / "DashboardView.PackageStatistics.cs")
        self.assertIn('("عدد الكروت المستخدمة","Used",200)', stats)
        self.assertNotIn('"Total"', stats)
        self.assertNotIn('"Sessions"', stats)
        self.assertNotIn('x=>x.Total', stats)


if __name__ == "__main__":
    unittest.main()
