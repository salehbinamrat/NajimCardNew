using System.Windows;
using System.Globalization;
using NajmCard.Core.Services;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    // Same visible swatches for the inline appearance editor and advanced options.
    private static readonly string[] CardPaletteColors=[
        "#4C1730","#74203D","#A33150","#D6263C","#F04438","#F29D38",
        "#F2C94C","#197C59","#2169A0","#543399","#111111","#FFFFFF"];
    private readonly Grid _designPreviewRow=new();
    private readonly TextBlock _designStatus=PageUi.Status();
    private ComboBox? _inlineWidth;
    private ComboBox? _inlineHeight;
    private bool _syncingInlineSize;

    public void RefreshInlineCardDimensions()
    {
        if(_syncingInlineSize || _inlineWidth is null || _inlineHeight is null ||
           PrintOptionsProvider is null)return;
        try
        {
            var options=PrintOptionsProvider();
            _syncingInlineSize=true;
            var cardWidth=options.CardWidthMm.ToString(CultureInfo.InvariantCulture);
            var cardHeight=options.CardHeightMm.ToString(CultureInfo.InvariantCulture);
            if(!_inlineWidth.Items.Contains(cardWidth))_inlineWidth.Items.Add(cardWidth);
            if(!_inlineHeight.Items.Contains(cardHeight))_inlineHeight.Items.Add(cardHeight);
            _inlineWidth.SelectedItem=cardWidth;
            _inlineHeight.SelectedItem=cardHeight;
        }
        catch(ArgumentException) { /* Do not replace valid input with a partially edited size. */ }
        catch(FormatException) { /* The left print fields may still be in progress. */ }
        finally {_syncingInlineSize=false;}
        RefreshPreviewCardGeometry();
    }

    private Button PaletteButton(string caption,TextBox target)
    {
        var button=PageUi.Button(caption,false);
        button.MinWidth=0;button.MinHeight=28;
        button.Padding=new Thickness(4,2,4,2);
        button.Margin=new Thickness(1);
        var popup=new Popup
        {
            PlacementTarget=button,Placement=PlacementMode.Bottom,
            StaysOpen=false,AllowsTransparency=true
        };
        var palette=new UniformGrid{Columns=6,Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!};
        foreach(var hex in CardPaletteColors)
        {
            var sample=new Button{Width=25,Height=25,Margin=new Thickness(2),
                Background=(Brush)new BrushConverter().ConvertFrom(hex)!,
                BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                BorderThickness=new Thickness(1),ToolTip="لون"};
            sample.Click+=(_,_)=>{target.Text=hex;popup.IsOpen=false;};
            palette.Children.Add(sample);
        }
        popup.Child=new Border
        {
            Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
            BorderThickness=new Thickness(2),Padding=new Thickness(5),
            Child=palette
        };
        button.Click+=(_,_)=>popup.IsOpen=!popup.IsOpen;
        button.ToolTip="اختر لونا من المربعات.";
        void Paint()
        {
            try
            {
                var color=(Color)ColorConverter.ConvertFromString(target.Text)!;
                button.Background=new SolidColorBrush(color);
                button.Content=new TextBlock
                {
                    Text=caption,Tag="ColorButtonLabel",Foreground=0.2126*color.R+0.7152*color.G+0.0722*color.B>135
                        ?PageUi.Muted:Brushes.FloralWhite
                };
            }
            catch(FormatException){ /* Keep previous color until a complete hex code is entered. */ }
        }
        target.TextChanged+=(_,_)=>Paint();
        Paint();
        return button;
    }

    private Border BuildDesignerPanel(bool embedded)
    {
        // The card keeps its aspect ratio. Size the row to the artwork and
        // appearance controls so spare viewport height stays outside the panel.
        var designer=new Grid();
        for(int i=0;i<7;i++)
            designer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        void AddPanel(UIElement element,int row)
        {
            Grid.SetRow(element,row);
            designer.Children.Add(element);
        }
        AddPanel(ReferenceHeader("معاينة وتصميم الكرت"),0);
        var toolbar=new UniformGrid{Columns=2,Margin=new Thickness(0,0,0,2)};
        var templateImage=PageUi.Button("تغيير صورة القالب",false);
        var advanced=PageUi.Button("تعديلات متقدمة",false);
        foreach(var button in new[]{templateImage,advanced})
        {
            button.MinWidth=0;button.Margin=new Thickness(2);
            button.Padding=new Thickness(3,1,3,1);
            button.FontSize=10;button.FontWeight=FontWeights.Bold;
            button.MinHeight=21;button.Foreground=Brushes.FloralWhite;
            button.Background=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
            toolbar.Children.Add(button);
        }
        // Explicit MenuItem controls need an explicit style; ContextMenu's
        // ItemContainerStyle alone can be ignored for prebuilt MenuItem rows.
        // Keep captions white in normal, hover and keyboard-highlight states.
        var popupBurgundy=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
        var hoveredBurgundy=(Brush)new BrushConverter().ConvertFrom("#F0E2CC")!;
        var popupBorder=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
        var themeMenu=new ContextMenu
        {
            PlacementTarget=templateImage,Placement=PlacementMode.Bottom,
            Background=popupBurgundy,Foreground=Brushes.Black,
            BorderBrush=popupBorder,BorderThickness=new Thickness(1),
            Padding=new Thickness(3)
        };
        var themeItemStyle=new Style(typeof(MenuItem));
        themeItemStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.Black));
        themeItemStyle.Setters.Add(new Setter(Control.BackgroundProperty,popupBurgundy));
        themeItemStyle.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));
        themeItemStyle.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("Arial")));
        themeItemStyle.Setters.Add(new Setter(Control.FontSizeProperty,12d));
        var itemTemplate=new ControlTemplate(typeof(MenuItem));
        var itemSurface=new FrameworkElementFactory(typeof(Border));
        itemSurface.Name="MenuItemSurface";
        itemSurface.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        itemSurface.SetValue(Border.PaddingProperty,new Thickness(14,8,14,8));
        itemSurface.SetValue(Border.BackgroundProperty,popupBurgundy);
        var label=new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(ContentPresenter.ContentSourceProperty,"Header");
        label.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        label.SetValue(TextElement.ForegroundProperty,Brushes.Black);
        label.SetValue(TextElement.FontFamilyProperty,new FontFamily("Arial"));
        itemSurface.AppendChild(label);
        itemTemplate.VisualTree=itemSurface;
        var hovered=new Trigger{Property=MenuItem.IsHighlightedProperty,Value=true};
        hovered.Setters.Add(new Setter
        {
            TargetName="MenuItemSurface",Property=Border.BackgroundProperty,
            Value=hoveredBurgundy
        });
        itemTemplate.Triggers.Add(hovered);
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.6));
        itemTemplate.Triggers.Add(disabled);
        themeItemStyle.Setters.Add(new Setter(Control.TemplateProperty,itemTemplate));
        themeMenu.ItemContainerStyle=themeItemStyle;
        var restoreTheme=new MenuItem
        {
            Header="قالب نجم نت الافتراضي",Style=themeItemStyle
        };
        restoreTheme.Click+=(_,_)=>
        {
            try
            {
                var style=(ReadCurrentAppearance() with
                {
                    NetworkTitle="نجم نت",TemplateImagePath="",BackgroundColor="#FFF6EB"
                }).Validate();
                LoadCardAppearanceFields(style);
            }
            catch(ArgumentException ex){_designStatus.Text=ex.Message;}
        };
        var importTheme=new MenuItem
        {
            Header="اختيار صورة القالب",Style=themeItemStyle
        };
        importTheme.Click+=(_,_)=>ChooseTemplateImage();
        themeMenu.Items.Add(restoreTheme);
        themeMenu.Items.Add(importTheme);
        templateImage.Click+=(_,_)=>themeMenu.IsOpen=true;

        var previewRow=_designPreviewRow;
        previewRow.FlowDirection=FlowDirection.LeftToRight;
        previewRow.Margin=new Thickness(0,0,0,2);
        previewRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        previewRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(168)});
        var options=PageUi.Column();
        options.FlowDirection=FlowDirection.RightToLeft;
        options.Children.Add(toolbar);
        options.Children.Add(ReferenceHeader("خيارات مظهر الكرت"));
        void Option(string label,FrameworkElement field)
        {
            field.MinWidth=0;field.Width=double.NaN;
            var line=new Grid{Margin=new Thickness(1,0,1,2)};
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(83)});
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var caption=new TextBlock{
                Text=label,FontFamily=new FontFamily("Arial"),FontSize=11,
                FontWeight=FontWeights.Bold,Foreground=
                    (Brush)new BrushConverter().ConvertFrom("#48192A")!,
                VerticalAlignment=VerticalAlignment.Center,
                TextAlignment=TextAlignment.Right,Margin=new Thickness(2)};
            Grid.SetColumn(caption,0);
            Grid.SetColumn(field,1);
            line.Children.Add(caption);
            line.Children.Add(field);
            options.Children.Add(line);
        }
        var sizes=Enumerable.Range(8,33).Select(size=>size.ToString(CultureInfo.InvariantCulture));
        var fontSizeChoice=PageUi.CompactNumberChoice(_designFontSize,sizes.ToArray());
        fontSizeChoice.ToolTip="حجم رقم الكرت من 8 إلى 40. يصغر النص الطويل ليلائم المساحة.";
        Option("حجم الخط",fontSizeChoice);
        Option("نوع الخط",_designFontFamily);
        Option("لون النص",PaletteButton("تغيير",_designElementColor));
        Option("لون رقم الكرت",PaletteButton("تغيير",_designTextColor));
        Grid.SetColumn(options,1);
        previewRow.Children.Add(options);
        AddPanel(previewRow,2);

        // Card size and styling are always visible directly beneath the preview.
        var shapeOptions=new Grid{Margin=new Thickness(0,2,0,3)};
        for(int i=0;i<6;i++)
            shapeOptions.ColumnDefinitions.Add(new ColumnDefinition
            {Width=new GridLength(1,GridUnitType.Star)});
        FrameworkElement ShapeOption(string label,FrameworkElement field)
        {
            var holder=PageUi.Column();
            holder.Margin=new Thickness(2,0,2,0);
            holder.Children.Add(new TextBlock
            {
                Text=label,FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold,
                FontSize=11,TextAlignment=TextAlignment.Center,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!
            });
            field.MinWidth=0;field.Height=30;field.MinHeight=30;field.MaxHeight=30;
            field.HorizontalAlignment=HorizontalAlignment.Stretch;
            holder.Children.Add(field);
            return holder;
        }
        _inlineWidth=new ComboBox{IsEditable=false};
        _inlineHeight=new ComboBox{IsEditable=false};
        foreach(var option in new[]{"35","40","45","50","55","60"})
            _inlineWidth.Items.Add(option);
        foreach(var option in new[]{"15","19","20","25","30"})
            _inlineHeight.Items.Add(option);
        _inlineWidth.SelectedItem="50";
        _inlineHeight.SelectedItem="19";
        void ApplyInlineSize()
        {
            if(_syncingInlineSize || PrintOptionsProvider is null || ApplyPrintOptions is null)return;
            try
            {
                var width=double.Parse((_inlineWidth.SelectedItem?.ToString()??_inlineWidth.Text).Trim().Replace(',','.'),
                    CultureInfo.InvariantCulture);
                var height=double.Parse((_inlineHeight.SelectedItem?.ToString()??_inlineHeight.Text).Trim().Replace(',','.'),
                    CultureInfo.InvariantCulture);
                var options=PrintOptionsProvider() with {CardWidthMm=width,CardHeightMm=height};
                CardPrintLayoutPlanner.Calculate(options,1);
                ApplyPrintOptions(options);
                _designStatus.Text="تم تحديث أبعاد الكرت؛ اضغط حفظ إعدادات الباقة من الأعلى.";
            }
            catch(Exception ex) when(ex is ArgumentException or FormatException or OverflowException)
            {
                _designStatus.Text="أبعاد الكرت غير صالحة: "+ex.Message;
            }
        }
        _inlineWidth.SelectionChanged+=(_,_)=>ApplyInlineSize();
        _inlineHeight.SelectionChanged+=(_,_)=>ApplyInlineSize();
        _inlineWidth.LostKeyboardFocus+=(_,_)=>ApplyInlineSize();
        _inlineHeight.LostKeyboardFocus+=(_,_)=>ApplyInlineSize();
        Loaded+=(_,_)=>RefreshInlineCardDimensions();
        void ShapeCell(string label,FrameworkElement control,int column)
        {
            var cell=ShapeOption(label,control);
            Grid.SetColumn(cell,column);
            shapeOptions.Children.Add(cell);
        }
        ShapeCell("العرض (مم)",_inlineWidth,0);
        ShapeCell("الطول (مم)",_inlineHeight,1);
        var qrSizes=new ComboBox{ItemsSource=new[]{"16","20","24","28","32","40","48","60"},
            SelectedItem="24"};
        qrSizes.SelectionChanged+=(_,_)=>
        {
            if(qrSizes.SelectedItem is string size)_designQrSize.Text=size;
        };
        _designQrSize.TextChanged+=(_,_)=>qrSizes.SelectedItem=_designQrSize.Text;
        ShapeCell("حجم QR",qrSizes,2);
        var borderSizes=new ComboBox{ItemsSource=new[]{"0","0.5","1","1.5","2","3","4","5"},
            SelectedItem="1"};
        borderSizes.SelectionChanged+=(_,_)=>
        {
            if(borderSizes.SelectedItem is string size)_designBorderWidth.Text=size;
        };
        _designBorderWidth.TextChanged+=(_,_)=>borderSizes.SelectedItem=_designBorderWidth.Text;
        ShapeCell("سمك الحدود",borderSizes,3);
        var elementSizes=new ComboBox
        {
            ItemsSource=new[]{"0.5","0.65","0.75","0.85","1","1.1","1.25","1.5","1.75","2","2.5"},
            SelectedItem="1",MinWidth=0
        };
        elementSizes.ToolTip="حجم نصوص الكرت الأخرى (العنوان، الباقة، الهاتف والتاريخ)، "+
            "ولا يغير حجم رقم الكرت وكلمة المرور المرتبطين معا.";
        elementSizes.SelectionChanged+=(_,_) =>
        {
            if(elementSizes.SelectedItem is string scale)_designElementScale.Text=scale;
        };
        _designElementScale.TextChanged+=(_,_) =>
        {
            var scale=_designElementScale.Text;
            if(!elementSizes.Items.Contains(scale))elementSizes.Items.Add(scale);
            elementSizes.SelectedItem=scale;
        };
        ShapeCell("حجم خط العناصر",elementSizes,4);
        ShapeCell("لون الحدود",PaletteButton("تغيير",_designBorderColor),5);
        AddPanel(shapeOptions,3);

        AddPanel(ReferenceHeader("عناصر الكرت"),4);
        var designChecks=new UniformGrid{Columns=5,Rows=2,Margin=new Thickness(0,1,0,1)};
        _designShowPassword.Content="كلمة المرور";
        _designShowProfile.Content="الباقة";
        foreach(var check in new[]{_designShowCapacity,_designShowPhone,_designShowQr,
            _designShowTime,_designShowDate,_designShowTitle,_designShowSequence,_designShowRoute,
            _designShowPassword,_designShowProfile})
        {
            // Explicit labels are independent of the global CheckBox control
            // template, which rendered the indicator but clipped its content.
            var caption=new TextBlock
            {
                Text=check.Content?.ToString()??"",
                FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold,
                FontSize=11,VerticalAlignment=VerticalAlignment.Center,
                TextTrimming=TextTrimming.CharacterEllipsis,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!
            };
            check.Content=null;
            check.Margin=new Thickness(2,0,2,0);
            caption.MouseLeftButtonUp+=(_,args)=>
            {
                if(check.IsEnabled)check.IsChecked=check.IsChecked!=true;
                args.Handled=true;
            };
            var option=new StackPanel
            {
                Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft,
                Margin=new Thickness(2,3,2,3),ToolTip=caption.Text
            };
            option.Children.Add(check);
            option.Children.Add(caption);
            designChecks.Children.Add(option);
        }
        AddPanel(designChecks,5);
        var designStatus=_designStatus;
        designStatus.Foreground=(Brush)new BrushConverter().ConvertFrom("#542037")!;
        designStatus.Margin=new Thickness(1,1,1,2);designStatus.FontSize=11;
        if(embedded)designStatus.Margin=new Thickness(1,0,1,1);
        AddPanel(designStatus,6);
        var designSurface=embedded?CompactSurface(designer,"#75203E")
            :PageUi.Surface(designer,"#75203E");

        advanced.Click+=(_,_)=>{
            var originalAppearance=_currentAppearance;
            bool savedSuccessfully=false;
            var popup=new Window{
                Title="نجم كارد — التعديلات المتقدمة",
                Width=790,Height=570,MinWidth=650,MinHeight=440,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Owner=Window.GetWindow(this),FlowDirection=FlowDirection.RightToLeft,
                Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!
            };
            var pane=new DockPanel{Margin=new Thickness(16)};
            var save=PageUi.Button("تطبيق التعديلات",true);
            save.MinWidth=0;save.Margin=new Thickness(2,8,2,1);
            var fields=new UniformGrid{Columns=2,Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!};
            TextBox Editable(string label,string value)
            {
                var entry=new TextBox{
                    Text=value,FontFamily=new FontFamily("Arial"),
                    Foreground=Brushes.Black,
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                    BorderThickness=new Thickness(1),
                    MinHeight=34,Margin=new Thickness(6,2,6,8)};
                var section=PageUi.Column();
                section.Children.Add(new TextBlock{
                    Text=label,Foreground=Brushes.Black,
                    FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold});
                section.Children.Add(entry);
                fields.Children.Add(section);
                return entry;
            }
            // Use a genuine editable TextBox alongside a burgundy popup.
            // The inherited WPF ComboBox template has no PART_EditableTextBox.
            TextBox Choices(string label,string current,string[] presets)
            {
                var entry=Editable(label,current);
                var section=(Panel)fields.Children[fields.Children.Count-1];
                section.Children.Remove(entry);
                var holder=new DockPanel{LastChildFill=true};
                var arrow=PageUi.Button("▾",false);
                arrow.Width=32;arrow.MinWidth=32;arrow.MinHeight=34;
                arrow.Margin=new Thickness(2,2,0,8);
                var burgundy=(Brush)new BrushConverter().ConvertFrom("#AF5472")!;
                arrow.Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
                arrow.Foreground=Brushes.Black;
                arrow.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
                entry.Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
                entry.Foreground=Brushes.Black;
                entry.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
                entry.CaretBrush=Brushes.Black;entry.Margin=new Thickness(2,2,2,8);
                DockPanel.SetDock(arrow,Dock.Left);
                holder.Children.Add(arrow);holder.Children.Add(entry);
                section.Children.Add(holder);
                var popupChoices=new Popup
                {
                    PlacementTarget=arrow,Placement=PlacementMode.Bottom,
                    StaysOpen=false,AllowsTransparency=true
                };
                var list=new WrapPanel{MaxWidth=290};
                foreach(var value in presets)
                {
                    var option=new Button
                    {
                        Content=new TextBlock{Text=value,Foreground=Brushes.Black,
                            FontFamily=new FontFamily("Arial"),FontSize=12,
                            TextAlignment=TextAlignment.Center},
                        MinWidth=63,MinHeight=31,
                        Margin=new Thickness(2),Padding=new Thickness(6,3,6,3),
                        FontFamily=new FontFamily("Arial"),FontSize=12,
                        Foreground=Brushes.Black,Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,
                        BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                        BorderThickness=new Thickness(1)
                    };
                    option.Click+=(_,_)=>{entry.Text=value;popupChoices.IsOpen=false;};
                    list.Children.Add(option);
                }
                popupChoices.Child=new Border
                {
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                    BorderThickness=new Thickness(1),Padding=new Thickness(4),
                    CornerRadius=new CornerRadius(7),Child=list
                };
                arrow.Click+=(_,_)=>popupChoices.IsOpen=!popupChoices.IsOpen;
                entry.ToolTip="اختر من القائمة أو أدخل قيمة مخصصة.";
                return entry;
            }
            TextBox ColorChoice(string label,string current)
            {
                // Keep the hex value internally for persistence, but show only
                // the selected color and the palette squares to the operator.
                var entry=Editable(label,current);
                var section=(Panel)fields.Children[fields.Children.Count-1];
                section.Children.Remove(entry);
                entry.Visibility=Visibility.Collapsed;
                section.Children.Add(entry);
                var panel=PageUi.Column();
                panel.Margin=new Thickness(0,1,0,3);
                var currentSwatch=new Border
                {
                    Width=30,Height=30,Margin=new Thickness(3,2,3,4),
                    CornerRadius=new CornerRadius(4),
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                    BorderThickness=new Thickness(1),
                    ToolTip="اللون المحدد حاليا"
                };
                panel.Children.Add(currentSwatch);
                var palette=new WrapPanel
                {
                    FlowDirection=FlowDirection.LeftToRight,
                    Margin=new Thickness(3,0,3,3),
                    ToolTip="اختر اللون من المربعات"
                };
                var squares=new Dictionary<string,Border>(StringComparer.OrdinalIgnoreCase);
                foreach(var hex in CardPaletteColors)
                {
                    var square=new Border
                    {
                        Width=25,Height=25,Margin=new Thickness(2),
                        Background=(Brush)new BrushConverter().ConvertFrom(hex)!,
                        BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                        BorderThickness=new Thickness(1),
                        CornerRadius=new CornerRadius(3),ToolTip="لون"
                    };
                    var key=hex;
                    square.MouseLeftButtonUp+=(_,args)=>
                    {
                        entry.Text=key;
                        args.Handled=true;
                    };
                    squares.Add(hex,square);
                    palette.Children.Add(square);
                }
                entry.TextChanged+=(_,_) =>
                {
                    var value=entry.Text.Trim();
                    foreach(var item in squares)
                        item.Value.BorderThickness=new Thickness(
                            string.Equals(item.Key,value,StringComparison.OrdinalIgnoreCase)?3:1);
                    try{currentSwatch.Background=(Brush)new BrushConverter().ConvertFromString(value)!;}
                    catch(FormatException){currentSwatch.Background=Brushes.Transparent;}
                };
                try{currentSwatch.Background=(Brush)new BrushConverter().ConvertFromString(current)!;}
                catch(FormatException){currentSwatch.Background=Brushes.Transparent;}
                panel.Children.Add(palette);
                section.Children.Add(panel);
                return entry;
            }
            var name=Editable("اسم الشبكة",_designTitle.Text);
            var background=ColorChoice("لون خلفية البطاقة",_designBackground.Text);
            var fontSize=Choices("حجم رقم الكرت",_designFontSize.Text,
                new[]{"12","14","16","18","20","22","24","28","32","36","40"});
            var numberColor=ColorChoice("لون رقم الكرت",_designTextColor.Text);
            var elementColor=ColorChoice("لون بقية النصوص",_designElementColor.Text);
            var borderColor=ColorChoice("لون الحدود",_designBorderColor.Text);
            var borderWidth=Choices("سمك الحدود",_designBorderWidth.Text,
                new[]{"0","0.5","1","1.5","2","3","4","5"});
            var elementsScale=Choices("حجم خط العناصر",_designElementScale.Text,
                new[]{"0.5","0.65","0.75","0.85","1","1.1","1.25","1.5","1.75","2","2.5"});
            var capacity=Editable("نص السعة (اختياري)",_designCapacityText.Text);
            // Keep the existing persisted PhoneText format so older presets
            // remain compatible, but let the operator edit both numbers.
            var phoneParts=_designPhoneText.Text.Split(" - ",2,StringSplitOptions.TrimEntries);
            var phone1=Editable("رقم الهاتف الأول (اختياري)",phoneParts[0]);
            var phone2=Editable("رقم الهاتف الثاني (اختياري)",
                phoneParts.Length>1?phoneParts[1]:"");
            var qrSize=Choices("حجم QR",_designQrSize.Text,
                new[]{"12","16","20","24","28","32","40","48","60","72"});

            // Advanced layout controls use the same live print model as the
            // inline editor; no shadow printer state or pretend dimensions.
            CardPrintOptions? livePrint=null;
            try{livePrint=PrintOptionsProvider?.Invoke();}
            catch(ArgumentException ex){_designStatus.Text=ex.Message;}
            // A dismissed modal must not commit the image picker or partial
            // field edits to the active card. Only a successful save commits.
            popup.Closed+=(_,_)=>{
                if(savedSuccessfully)return;
                _designTitle.Text=originalAppearance.NetworkTitle;
                _designFontFamily.SelectedItem=originalAppearance.FontFamily;
                _designFontSize.Text=originalAppearance.UsernameFontSize.ToString(
                    CultureInfo.InvariantCulture);
                _designBackground.Text=originalAppearance.BackgroundColor;
                _designTextColor.Text=originalAppearance.UsernameColor;
                _designElementColor.Text=originalAppearance.EffectiveElementColor;
                _designBorderColor.Text=originalAppearance.BorderColor;
                _designBorderWidth.Text=originalAppearance.BorderWidth.ToString(
                    CultureInfo.InvariantCulture);
                _designElementScale.Text=originalAppearance.ElementScale.ToString(
                    CultureInfo.InvariantCulture);
                _designShowPassword.IsChecked=originalAppearance.ShowPassword;
                _designShowProfile.IsChecked=originalAppearance.ShowProfile;
                _designShowPhone.IsChecked=originalAppearance.ShowPhone;
                _designCapacityText.Text=originalAppearance.CapacityText;
                _designPhoneText.Text=originalAppearance.PhoneText;
                _designQrSize.Text=originalAppearance.QrSize.ToString(CultureInfo.InvariantCulture);
                ApplyCardAppearance(originalAppearance);
                if(livePrint is not null)ApplyPrintOptions?.Invoke(livePrint);
            };
            var width=Editable("عرض الكرت (مم)",
                livePrint?.CardWidthMm.ToString(CultureInfo.InvariantCulture)??"50");
            var height=Editable("ارتفاع الكرت (مم)",
                livePrint?.CardHeightMm.ToString(CultureInfo.InvariantCulture)??"19");
            width.IsEnabled=height.IsEnabled=ApplyPrintOptions is not null;
            var font=Choices("نوع الخط",
                _designFontFamily.SelectedItem?.ToString()??"Arial",
                new[]{"Arial","Tahoma","Segoe UI","Calibri"});
            var showPassword=new CheckBox{
                Content="إظهار كلمة المرور",IsChecked=_designShowPassword.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                Margin=new Thickness(6,10,6,8)};
            var showProfile=new CheckBox{
                Content="إظهار الباقة",IsChecked=_designShowProfile.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                Margin=new Thickness(6,10,6,8)};
            var showPhone=new CheckBox
            {
                Content="عرض أرقام الهاتف على الكرت",
                IsChecked=_designShowPhone.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                Margin=new Thickness(6,10,6,8)
            };
            fields.Children.Add(showPassword);
            fields.Children.Add(showProfile);
            fields.Children.Add(showPhone);
            var chooseImage=PageUi.Button("اختيار صورة الكرت لهذه الباقة",false);
            chooseImage.MinWidth=0;chooseImage.Margin=new Thickness(6,6,6,8);
            chooseImage.Foreground=Brushes.White;
            chooseImage.Background=(Brush)new BrushConverter().ConvertFrom("#176D75")!;
            chooseImage.Click+=(_,_)=>ChooseTemplateImage();
            fields.Children.Add(chooseImage);
            save.Click+=(_,_)=>{
                try{
                    CardPrintOptions? newPrint=null;
                    if(ApplyPrintOptions is not null)
                    {
                        var current=PrintOptionsProvider?.Invoke()??new CardPrintOptions();
                        var w=double.Parse(width.Text.Trim().Replace(',','.'),
                            CultureInfo.InvariantCulture);
                        var h=double.Parse(height.Text.Trim().Replace(',','.'),
                            CultureInfo.InvariantCulture);
                        newPrint=current with{CardWidthMm=w,CardHeightMm=h};
                        CardPrintLayoutPlanner.Calculate(newPrint,1);
                    }
                    _designTitle.Text=name.Text;_designBackground.Text=background.Text;
                    _designFontSize.Text=fontSize.Text;_designTextColor.Text=numberColor.Text;
                    _designElementColor.Text=elementColor.Text;
                    _designBorderColor.Text=borderColor.Text;
                    _designBorderWidth.Text=borderWidth.Text;
                    _designElementScale.Text=elementsScale.Text;
                    _designCapacityText.Text=capacity.Text;
                    _designPhoneText.Text=string.Join(" - ",
                        new[]{phone1.Text.Trim(),phone2.Text.Trim()}
                            .Where(value=>value.Length>0));
                    _designShowPhone.IsChecked=showPhone.IsChecked;
                    _designQrSize.Text=qrSize.Text;
                    _designFontFamily.SelectedItem=font.Text.Trim();
                    _designShowPassword.IsChecked=showPassword.IsChecked;
                    _designShowProfile.IsChecked=showProfile.IsChecked;
                    ApplyCardAppearance(ReadCurrentAppearance());
                    if(newPrint is not null)ApplyPrintOptions?.Invoke(newPrint);
                    savedSuccessfully=true;
                    _designStatus.Text="تم تطبيق التعديلات. احفظ إعدادات الباقة من الزر العلوي.";
                    popup.DialogResult=true;
                }catch(Exception ex){
                    MessageBox.Show(ex.Message,"التحقق من التصميم",
                        MessageBoxButton.OK,MessageBoxImage.Warning);
                }
            };
            DockPanel.SetDock(save,Dock.Bottom);
            pane.Children.Add(save);
            pane.Children.Add(new ScrollViewer{
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=fields});
            popup.Content=pane;popup.ShowDialog();
        };


        return designSurface;
    }
}
