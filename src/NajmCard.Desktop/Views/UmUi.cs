using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Scoped navy/cyan visual language for User Manager screens only.
/// No global MainWindow styles or card printing templates are modified.
/// </summary>
internal static class UmUi
{
    internal static readonly Brush Navy=Solid("#072447");
    internal static readonly Brush Blue=Solid("#0B4F82");
    internal static readonly Brush Cyan=Solid("#007E86");
    internal static readonly Brush Ink=Solid("#10253D");
    internal static readonly Brush Light=Solid("#F1F7FC");
    internal static readonly Brush Gray=Solid("#546A80");

    internal static SolidColorBrush Solid(string hex)
    {
        var brush=(SolidColorBrush)new BrushConverter().ConvertFromInvariantString(hex)!;
        brush.Freeze();
        return brush;
    }

    internal static TextBlock Label(string value,int size=14,bool bold=false,
        Brush? foreground=null)
        => new()
        {
            Text=value,FontFamily=new FontFamily("Arial"),FontSize=size,
            FontWeight=bold?FontWeights.Bold:FontWeights.Normal,
            Foreground=foreground??Ink,TextWrapping=TextWrapping.Wrap,
            VerticalAlignment=VerticalAlignment.Center,
            TextTrimming=TextTrimming.CharacterEllipsis
        };

    internal static Border Panel(UIElement child,Thickness? margin=null,
        Brush? background=null)
        => new()
        {
            Child=child,Padding=new Thickness(11),
            Margin=margin??new Thickness(4),CornerRadius=new CornerRadius(12),
            BorderBrush=Solid("#BFCFDA"),BorderThickness=new Thickness(1),
            Background=background??Solid("#FCFEFF")
        };

    internal static StackPanel Stack(params UIElement[] controls)
    {
        var stack=new StackPanel {Orientation=Orientation.Vertical};
        foreach(var item in controls)stack.Children.Add(item);
        return stack;
    }

    internal static WrapPanel Row(params UIElement[] controls)
    {
        var row=new WrapPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,5)};
        foreach(var item in controls)row.Children.Add(item);
        return row;
    }

    internal static Button Action(string caption,bool main=false,int minWidth=86)
    {
        var background=main?Cyan:Solid("#E8F0F8");
        var foreground=main?Brushes.White:Navy;
        var button=new Button
        {
            Content=caption,
            FontFamily=new FontFamily("Arial"),
            FontWeight=FontWeights.Bold,FontSize=12,
            MinWidth=minWidth,MinHeight=35,MaxHeight=42,
            Foreground=foreground,Background=background,
            BorderBrush=Solid(main?"#0C8F9A":"#BBCDDD"),
            BorderThickness=new Thickness(1),
            Margin=new Thickness(3),Padding=new Thickness(10,5,10,5),
            Cursor=System.Windows.Input.Cursors.Hand
        };
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border));
        border.Name="Frame";
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new Binding(nameof(Button.Background))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty,new Binding(nameof(Button.BorderBrush))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty,new Binding(nameof(Button.BorderThickness))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        // Honor the button's padding so long Arabic captions do not collide
        // with the rounded frame or disappear behind adjacent controls.
        border.SetBinding(Border.PaddingProperty,new Binding(nameof(Button.Padding))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        // A generated TextBlock must bind the actual foreground: the application
        // has an implicit light TextBlock style that overrides inherited colors.
        var content=new FrameworkElementFactory(typeof(TextBlock));
        content.Name="PART_ActionText";
        content.SetBinding(TextBlock.TextProperty,new Binding(nameof(Button.Content))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        content.SetBinding(TextBlock.ForegroundProperty,new Binding(nameof(Button.Foreground))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        content.SetValue(TextBlock.FontWeightProperty,FontWeights.Bold);
        content.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);
        content.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        content.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(content);template.VisualTree=border;
        var hover=new Trigger {Property=Button.IsMouseOverProperty,Value=true};
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,Cyan,"Frame"));
        template.Triggers.Add(hover);
        button.Template=template;
        return button;
    }

    internal static TextBox SearchBox(string hint="بحث في البيانات")
        => new()
        {
            Width=205,Height=36,FontSize=13,FontFamily=new FontFamily("Arial"),
            Background=Brushes.White,Foreground=Ink,
            BorderBrush=Solid("#AFC2D2"),BorderThickness=new Thickness(1),
            Padding=new Thickness(7,5,7,5),Margin=new Thickness(4),
            ToolTip=hint
        };

    internal static ComboBox Combo(IEnumerable<string> options,int width=125)
    {
        // This theme is intentionally scoped to UM's light surfaces. Do not
        // reuse the app's dark ComboBox template for these white controls.
        var cb=new ComboBox
        {
            Width=width,MinHeight=34,Background=Brushes.White,
            Foreground=Ink,Margin=new Thickness(4),FontSize=12,
            FontFamily=new FontFamily("Arial"),IsEditable=false,
            MaxDropDownHeight=280,HorizontalContentAlignment=HorizontalAlignment.Stretch
        };
        cb.Style=LightComboStyle();
        cb.ItemContainerStyle=LightComboItemStyle();
        foreach(var option in options.Distinct(StringComparer.Ordinal))
            cb.Items.Add(option);
        if(cb.Items.Count>0)cb.SelectedIndex=0;
        return cb;
    }

    private static Style? cachedComboStyle;
    private static Style? cachedItemStyle;
    private static Style LightComboStyle()
    {
        if(cachedComboStyle is not null)return cachedComboStyle;
        var style=new Style(typeof(ComboBox));
        var template=new ControlTemplate(typeof(ComboBox));
        var root=new FrameworkElementFactory(typeof(Grid));

        var toggle=new FrameworkElementFactory(typeof(ToggleButton));
        toggle.Name="DropdownToggle";
        toggle.SetValue(UIElement.FocusableProperty,false);
        toggle.SetValue(ToggleButton.ClickModeProperty,ClickMode.Press);
        toggle.SetBinding(ToggleButton.IsCheckedProperty,new Binding("IsDropDownOpen")
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent),
            Mode=BindingMode.TwoWay
        });
        var toggleTemplate=new ControlTemplate(typeof(ToggleButton));
        var toggleBorder=new FrameworkElementFactory(typeof(Border));
        toggleBorder.SetValue(Border.BackgroundProperty,Brushes.White);
        toggleBorder.SetValue(Border.BorderBrushProperty,Solid("#8CA9BD"));
        toggleBorder.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        toggleBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
        var arrow=new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        arrow.SetValue(System.Windows.Shapes.Path.DataProperty,
            Geometry.Parse("M 0,0 L 4,4 L 8,0"));
        arrow.SetValue(System.Windows.Shapes.Shape.StrokeProperty,Navy);
        arrow.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty,1.8);
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty,new Thickness(10,0,0,0));
        toggleBorder.AppendChild(arrow);
        toggleTemplate.VisualTree=toggleBorder;
        toggle.SetValue(Control.TemplateProperty,toggleTemplate);
        root.AppendChild(toggle);

        // Explicit selected label, not a ContentPresenter using the implicit
        // global TextBlock style (cream text, invisible on a white dropdown).
        var selected=new FrameworkElementFactory(typeof(TextBlock));
        selected.Name="PART_SelectedText";
        selected.SetBinding(TextBlock.TextProperty,new Binding("SelectionBoxItem")
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        selected.SetBinding(TextBlock.ForegroundProperty,new Binding(nameof(ComboBox.Foreground))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        selected.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        selected.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);
        selected.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Arial"));
        selected.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);
        selected.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        selected.SetValue(FrameworkElement.MarginProperty,new Thickness(32,0,9,0));
        selected.SetValue(UIElement.IsHitTestVisibleProperty,false);
        root.AppendChild(selected);

        var popup=new FrameworkElementFactory(typeof(Popup));
        popup.Name="PART_Popup";
        popup.SetValue(Popup.PlacementProperty,PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty,true);
        popup.SetValue(UIElement.FocusableProperty,false);
        popup.SetBinding(Popup.IsOpenProperty,new Binding("IsDropDownOpen")
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent),
            Mode=BindingMode.TwoWay
        });
        popup.SetBinding(Popup.PlacementTargetProperty,new Binding
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var popupBorder=new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty,Brushes.White);
        popupBorder.SetValue(TextElement.ForegroundProperty,Ink);
        popupBorder.SetValue(Border.BorderBrushProperty,Solid("#8CA9BD"));
        popupBorder.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        popupBorder.SetBinding(FrameworkElement.MinWidthProperty,new Binding("ActualWidth")
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        popupBorder.SetBinding(FrameworkElement.MaxHeightProperty,new Binding("MaxDropDownHeight")
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var scroll=new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty,true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        var presenter=new FrameworkElementFactory(typeof(ItemsPresenter));
        scroll.AppendChild(presenter);
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);
        template.VisualTree=root;

        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.65));
        template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty,template));
        style.Setters.Add(new Setter(Control.ForegroundProperty,Ink));
        style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.White));
        style.Seal();
        cachedComboStyle=style;
        return style;
    }

    private static Style LightComboItemStyle()
    {
        if(cachedItemStyle is not null)return cachedItemStyle;
        var style=new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty,Ink));
        style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.White));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,5,7,5)));
        var template=new ControlTemplate(typeof(ComboBoxItem));
        var border=new FrameworkElementFactory(typeof(Border));
        border.Name="ItemSurface";
        border.SetBinding(Border.BackgroundProperty,new Binding(nameof(ComboBoxItem.Background))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.PaddingProperty,new Binding(nameof(ComboBoxItem.Padding))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        var label=new FrameworkElementFactory(typeof(TextBlock));
        label.Name="ItemText";
        label.SetBinding(TextBlock.TextProperty,new Binding(nameof(ComboBoxItem.Content))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        label.SetBinding(TextBlock.ForegroundProperty,new Binding(nameof(ComboBoxItem.Foreground))
        {
            RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        label.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        label.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Arial"));
        label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(label);
        template.VisualTree=border;
        foreach(var triggerProperty in new[]{ComboBoxItem.IsHighlightedProperty,
            ComboBoxItem.IsSelectedProperty})
        {
            var highlighted=new Trigger{Property=triggerProperty,Value=true};
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty,
                Solid("#CCEAF0")));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty,Navy));
            template.Triggers.Add(highlighted);
        }
        style.Setters.Add(new Setter(Control.TemplateProperty,template));
        style.Seal();
        cachedItemStyle=style;
        return style;
    }

    internal static DataGrid Table(params (string Header,string Field,int Width)[] columns)
    {
        var grid=new DataGrid
        {
            IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,
            AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Single,
            SelectionUnit=DataGridSelectionUnit.FullRow,
            HeadersVisibility=DataGridHeadersVisibility.Column,
            GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush=Solid("#DBE4EC"),
            RowBackground=Brushes.White,AlternatingRowBackground=Solid("#EDF4FB"),
            AlternationCount=2,RowHeight=35,ColumnHeaderHeight=36,
            EnableRowVirtualization=true,EnableColumnVirtualization=true,
            BorderBrush=Solid("#BCCDDD"),BorderThickness=new Thickness(1),
            Background=Brushes.White,Foreground=Ink,FontSize=12,
            FontFamily=new FontFamily("Arial")
        };
        VirtualizingPanel.SetIsVirtualizing(grid,true);
        VirtualizingPanel.SetVirtualizationMode(grid,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid,true);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid,ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(grid,ScrollBarVisibility.Auto);
        var headers=new Style(typeof(DataGridColumnHeader));
        headers.Setters.Add(new Setter(Control.BackgroundProperty,Navy));
        headers.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        headers.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.Bold));
        headers.Setters.Add(new Setter(Control.FontSizeProperty,12d));
        headers.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,5,7,5)));
        headers.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Center));
        grid.ColumnHeaderStyle=headers;
        var cellText=new Style(typeof(TextBlock));
        cellText.Setters.Add(new Setter(TextBlock.ForegroundProperty,Ink));
        cellText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,
            TextTrimming.CharacterEllipsis));
        cellText.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,
            VerticalAlignment.Center));
        var cells=new Style(typeof(DataGridCell));
        cells.Setters.Add(new Setter(Control.ForegroundProperty,Ink));
        cells.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,2,5,2)));
        cells.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        grid.CellStyle=cells;
        foreach(var (header,property,width) in columns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header=header,Binding=new Binding(property),Width=new DataGridLength(width),
                ElementStyle=cellText,IsReadOnly=true
            });
        }
        return grid;
    }

    internal static Border Header(string title,string subtitle)
    {
        var inner=Stack(Label(title,23,true,Brushes.White),
            Label(subtitle,12,false,Solid("#CCE4F6")));
        inner.Margin=new Thickness(8,3,8,3);
        return new Border
        {
            Child=inner,Background=Navy,BorderBrush=Solid("#1F80A0"),
            BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),
            Padding=new Thickness(9),Margin=new Thickness(3,3,3,7)
        };
    }

    internal static Border Metric(string label,string value,Brush? accent=null)
    {
        var accentColor=accent??Cyan;
        var inner=new DockPanel {LastChildFill=true};
        var symbol=new Border
        {
            Width=7,Background=accentColor,
            CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,10,0)
        };
        DockPanel.SetDock(symbol,Dock.Right);
        inner.Children.Add(symbol);
        inner.Children.Add(Stack(Label(label,12,true,Ink),
            Label(value,22,true,Navy)));
        return Panel(inner,new Thickness(3,3,3,5),Solid("#F9FCFF"));
    }

    internal static Grid Metrics(params (string Label,string Value)[] values)
    {
        var grid=new Grid {Margin=new Thickness(0,2,0,3)};
        for(int i=0;i<values.Length;i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        for(int i=0;i<values.Length;i++)
        {
            var card=Metric(values[i].Label,values[i].Value);
            Grid.SetColumn(card,i);
            grid.Children.Add(card);
        }
        return grid;
    }

    internal static Border Tag(string content,Brush? background=null)
    {
        var item=new Border
        {
            Background=background??Solid("#E2F2F6"),
            Padding=new Thickness(7,4,7,4),
            CornerRadius=new CornerRadius(7),
            Margin=new Thickness(2)
        };
        item.Child=Label(content,12,true);
        return item;
    }

    /// <summary>
    /// Rate-limit local in-memory filters (never router calls per keystroke).
    /// Stop pending work when the screen is removed from the visual tree.
    /// </summary>
    internal static void Debounce(TextBox input,FrameworkElement owner,Action update,
        int milliseconds=180)
    {
        var timer=new DispatcherTimer(DispatcherPriority.Background,input.Dispatcher)
        {
            Interval=TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick+=(_,_) =>
        {
            timer.Stop();
            if(owner.IsLoaded)update();
        };
        input.TextChanged+=(_,_) =>
        {
            timer.Stop();
            if(owner.IsLoaded)timer.Start();
        };
        owner.Unloaded+=(_,_)=>timer.Stop();
    }

    internal static ScrollViewer Scroller(UIElement child)
        => new()
        {
            Content=child,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            Background=Light
        };

    internal static string Bytes(long? amount)
    {
        if(amount is not long size)return "غير متاح";
        var units=new[]{"B","KiB","MiB","GiB","TiB"};
        double n=size;int i=0;
        while(n>=1024 && i<units.Length-1){n/=1024;i++;}
        return n.ToString(i==0?"N0":"N2",
            System.Globalization.CultureInfo.InvariantCulture)+" "+units[i];
    }

    internal static string Number(int? n) =>
        n?.ToString("N0",System.Globalization.CultureInfo.InvariantCulture)??"غير متاح";
}
