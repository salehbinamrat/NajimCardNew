using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NajmCard.Desktop.Views;

/// <summary>Printing/selected-batch dropdowns only, without altering global
/// ComboBox styles elsewhere in the application or voucher batch selection.</summary>
public partial class PrintingView
{
    private static Style? cachedBurgundyItem;

    private static Style BurgundyDropdownItemStyle()
    {
        if(cachedBurgundyItem is not null)return cachedBurgundyItem;
        var normal=(Brush)new BrushConverter().ConvertFrom("#AF5472")!;
        var highlighted=(Brush)new BrushConverter().ConvertFrom("#C47490")!;
        var selected=(Brush)new BrushConverter().ConvertFrom("#873151")!;
        var style=new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty,normal));
        style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6)));
        var template=new ControlTemplate(typeof(ComboBoxItem));
        var border=new FrameworkElementFactory(typeof(Border));
        border.Name="OptionSurface";
        border.SetBinding(Border.BackgroundProperty,new Binding(nameof(Control.Background))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.PaddingProperty,new Binding(nameof(Control.Padding))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var caption=new FrameworkElementFactory(typeof(TextBlock));
        caption.SetBinding(TextBlock.TextProperty,new Binding(nameof(ContentControl.Content))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        caption.SetValue(TextBlock.ForegroundProperty,Brushes.White);
        caption.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Arial"));
        caption.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);
        caption.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        border.AppendChild(caption);
        template.VisualTree=border;
        var hover=new Trigger{Property=ComboBoxItem.IsHighlightedProperty,Value=true};
        hover.Setters.Add(new Setter(Control.BackgroundProperty,highlighted));
        template.Triggers.Add(hover);
        var active=new Trigger{Property=ComboBoxItem.IsSelectedProperty,Value=true};
        active.Setters.Add(new Setter(Control.BackgroundProperty,selected));
        template.Triggers.Add(active);
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.6));
        template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty,template));
        style.Seal();
        cachedBurgundyItem=style;
        return style;
    }

    private static void ApplyBurgundyDropdown(ComboBox selector)
    {
        var style=new Style(typeof(ComboBox),
            (Style)Application.Current.FindResource("DarkComboBox"));
        style.Setters.Add(new Setter(Control.BackgroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#AF5472")!));
        style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            (Brush)new BrushConverter().ConvertFrom("#75203E")!));
        style.Setters.Add(new Setter(ComboBox.MaxDropDownHeightProperty,235d));
        style.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("Arial")));
        style.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));
        selector.Style=style;
        selector.ItemContainerStyle=BurgundyDropdownItemStyle();
    }
}
