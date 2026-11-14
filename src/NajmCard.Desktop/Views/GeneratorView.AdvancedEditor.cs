using System.Windows;
using System.Globalization;
using NajmCard.Core.Services;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Grouped advanced designer modal. The fields still write to the existing
/// appearance and print models; Creator and Printing services are unchanged.
/// </summary>
public partial class GeneratorView
{
    private sealed record CardPaletteEntry(string Name,string Hex)
    {
        public Brush Swatch => (Brush)new BrushConverter().ConvertFrom(Hex)!;
    }

    private void OpenAdvancedCardDesigner()
    {
            var originalAppearance=_currentAppearance;
            bool savedSuccessfully=false;
            var popup=new Window{
                Title="نجم كارد — التعديلات المتقدمة",
                Width=860,Height=630,MinWidth=740,MinHeight=540,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Owner=Window.GetWindow(this),FlowDirection=FlowDirection.RightToLeft,
                Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!
            };
            // One cohesive modal: fixed header/actions and grouped, uniformly
            // sized cream controls. The editable values remain in the original
            // appearance/print models; no new persistence format is introduced.
            var pane=new DockPanel{Margin=new Thickness(14),
                LastChildFill=true,FlowDirection=FlowDirection.RightToLeft};
            var headerLines=new StackPanel();
            headerLines.Children.Add(new TextBlock
            {
                Text="التعديلات المتقدمة للكرت",FontFamily=new FontFamily("Arial"),
                FontSize=19,FontWeight=FontWeights.ExtraBold,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#632C45")!
            });
            headerLines.Children.Add(new TextBlock
            {
                Text="اختر الألوان من القوائم، وعدل المقاسات ثم اضغط تطبيق التعديلات.",
                FontFamily=new FontFamily("Arial"),FontSize=12,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#754F62")!,
                Margin=new Thickness(0,3,0,0)
            });
            var header=new Border
            {
                Background=(Brush)new BrushConverter().ConvertFrom("#F3E0D1")!,
                BorderBrush=(Brush)new BrushConverter().ConvertFrom("#D6A99A")!,
                BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),
                Padding=new Thickness(15,10,15,10),
                Margin=new Thickness(0,0,0,9),Child=headerLines
            };
            DockPanel.SetDock(header,Dock.Top);
            pane.Children.Add(header);
            var save=PageUi.Button("تطبيق التعديلات",true);
            save.MinWidth=156;save.Height=38;
            save.Margin=new Thickness(3);save.Padding=new Thickness(13,5,13,5);
            save.Background=(Brush)new BrushConverter().ConvertFrom("#A5526C")!;
            save.Foreground=Brushes.White;
            var cancel=PageUi.Button("إلغاء",false);
            cancel.MinWidth=95;cancel.Height=38;cancel.Margin=new Thickness(3);
            cancel.Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
            cancel.Foreground=Brushes.Black;
            cancel.Click+=(_,_)=>popup.Close();
            var saveRow=new StackPanel
            {
                Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft,
                HorizontalAlignment=HorizontalAlignment.Right,
                Margin=new Thickness(0,8,0,0)
            };
            saveRow.Children.Add(save);saveRow.Children.Add(cancel);
            DockPanel.SetDock(saveRow,Dock.Bottom);
            pane.Children.Add(saveRow);

            var contentStack=new StackPanel
            {
                FlowDirection=FlowDirection.RightToLeft,
                Margin=new Thickness(0,0,0,3)
            };
            UniformGrid fields=null!;
            void Group(string title,int columns=2)
            {
                fields=new UniformGrid
                {
                    Columns=columns,FlowDirection=FlowDirection.RightToLeft,
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFFAF2")!
                };
                var group=new StackPanel();
                group.Children.Add(new TextBlock
                {
                    Text=title,FontFamily=new FontFamily("Arial"),
                    FontSize=14,FontWeight=FontWeights.ExtraBold,
                    Foreground=(Brush)new BrushConverter().ConvertFrom("#682E47")!,
                    Margin=new Thickness(5,1,5,7)
                });
                group.Children.Add(fields);
                contentStack.Children.Add(new Border
                {
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFFAF2")!,
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#D8B7A7")!,
                    BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),
                    Padding=new Thickness(10,8,10,9),
                    Margin=new Thickness(0,0,0,8),Child=group
                });
            }
            TextBox Editable(string label,string value)
            {
                var entry=new TextBox
                {
                    Text=value,FontFamily=new FontFamily("Arial"),
                    FontSize=13,Foreground=Brushes.Black,
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                    BorderThickness=new Thickness(1),Height=34,MinHeight=34,MaxHeight=34,
                    VerticalContentAlignment=VerticalAlignment.Center,
                    Padding=new Thickness(9,4,9,4),
                    Margin=new Thickness(0,5,0,0)
                };
                var section=new StackPanel
                {
                    MinHeight=72,Margin=new Thickness(5,2,5,2),
                    VerticalAlignment=VerticalAlignment.Top
                };
                section.Children.Add(new TextBlock
                {
                    Text=label,Foreground=(Brush)new BrushConverter().ConvertFrom("#512B3C")!,
                    FontSize=12,FontFamily=new FontFamily("Arial"),
                    FontWeight=FontWeights.Bold,TextTrimming=TextTrimming.CharacterEllipsis
                });
                section.Children.Add(entry);
                fields.Children.Add(section);
                return entry;
            }
            // Numeric fields remain editable: advanced card layouts may contain
            // valid custom dimensions not present in a fixed preset list.
            TextBox Choices(string label,string current,string[] presets)
            {
                var entry=Editable(label,current);
                var section=(Panel)fields.Children[fields.Children.Count-1];
                section.Children.Remove(entry);
                var holder=new DockPanel
                {
                    LastChildFill=true,Height=34,Margin=new Thickness(0,5,0,0)
                };
                var arrow=PageUi.Button("▾",false);
                arrow.Width=35;arrow.MinWidth=35;arrow.Height=34;
                arrow.Margin=new Thickness(3,0,0,0);
                arrow.Background=(Brush)new BrushConverter().ConvertFrom("#F1DBCC")!;
                arrow.Foreground=Brushes.Black;
                arrow.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
                entry.Margin=new Thickness(0);entry.Foreground=Brushes.Black;
                entry.CaretBrush=Brushes.Black;
                DockPanel.SetDock(arrow,Dock.Left);
                holder.Children.Add(arrow);holder.Children.Add(entry);
                section.Children.Add(holder);
                var popupChoices=new Popup
                {
                    PlacementTarget=arrow,Placement=PlacementMode.Bottom,
                    StaysOpen=false,AllowsTransparency=true
                };
                var list=new WrapPanel{MaxWidth=300};
                foreach(var value in presets)
                {
                    var option=PageUi.Button(value,false);
                    option.MinWidth=64;option.MinHeight=32;
                    option.Margin=new Thickness(2);option.Padding=new Thickness(7,4,7,4);
                    option.FontSize=12;option.Foreground=Brushes.Black;
                    option.Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
                    option.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
                    option.BorderThickness=new Thickness(1);
                    option.Click+=(_,_)=>{entry.Text=value;popupChoices.IsOpen=false;};
                    list.Children.Add(option);
                }
                popupChoices.Child=new Border
                {
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                    BorderThickness=new Thickness(1),Padding=new Thickness(6),
                    CornerRadius=new CornerRadius(7),Child=list
                };
                arrow.Click+=(_,_)=>popupChoices.IsOpen=!popupChoices.IsOpen;
                entry.ToolTip="اختر قيمة من القائمة أو أدخل قيمة مخصصة.";
                return entry;
            }
            TextBox ColorChoice(string label,string current)
            {
                // Real WPF ComboBox with labelled swatches, not a permanent
                // wall of palette squares. The HEX stays in a hidden existing
                // TextBox solely for the unchanged appearance-save workflow.
                var entry=Editable(label,current);
                var section=(Panel)fields.Children[fields.Children.Count-1];
                section.Children.Remove(entry);
                entry.Visibility=Visibility.Collapsed;
                var choices=new List<CardPaletteEntry>
                {
                    new("سكري فاتح","#FFF6EB"),new("سكري","#FFF4DF"),
                    new("عنابي","#4C1730"),new("عنابي متوسط","#74203D"),
                    new("وردي عنابي","#A33150"),new("أحمر","#D6263C"),
                    new("مرجاني","#F04438"),new("برتقالي","#F29D38"),
                    new("ذهبي فاتح","#F2C94C"),new("ذهبي","#B89159"),
                    new("أخضر","#197C59"),new("أزرق","#2169A0"),
                    new("بنفسجي","#543399"),new("أسود","#111111"),
                    new("أبيض","#FFFFFF")
                };
                if(!choices.Any(x=>string.Equals(x.Hex,current,StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        _=(Brush)new BrushConverter().ConvertFromString(current)!;
                        choices.Insert(0,new CardPaletteEntry("اللون المحفوظ",current));
                    }
                    catch(FormatException){ /* Invalid existing value remains untouched until edited. */ }
                    catch(NotSupportedException){ /* Same fail-closed behavior. */ }
                }
                var comboStyle=new Style(typeof(ComboBox),
                    (Style)Application.Current.FindResource("DarkComboBox"));
                comboStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,34d));
                comboStyle.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty,34d));
                var selector=new ComboBox
                {
                    Style=comboStyle,Height=34,MinHeight=34,MaxHeight=34,
                    ItemsSource=choices,MaxDropDownHeight=250,
                    Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!,
                    Foreground=Brushes.Black,
                    BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!,
                    BorderThickness=new Thickness(1),
                    Margin=new Thickness(0,5,0,0),
                    HorizontalAlignment=HorizontalAlignment.Stretch
                };
                var itemTemplate=new DataTemplate(typeof(CardPaletteEntry));
                var itemRow=new FrameworkElementFactory(typeof(StackPanel));
                itemRow.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
                itemRow.SetValue(StackPanel.FlowDirectionProperty,FlowDirection.RightToLeft);
                var swatch=new FrameworkElementFactory(typeof(Border));
                swatch.SetValue(FrameworkElement.WidthProperty,19d);
                swatch.SetValue(FrameworkElement.HeightProperty,19d);
                swatch.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));
                swatch.SetValue(Border.BorderThicknessProperty,new Thickness(1));
                swatch.SetValue(Border.BorderBrushProperty,
                    (Brush)new BrushConverter().ConvertFrom("#75203E")!);
                swatch.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,7,0));
                swatch.SetBinding(Border.BackgroundProperty,
                    new System.Windows.Data.Binding(nameof(CardPaletteEntry.Swatch)));
                itemRow.AppendChild(swatch);
                var caption=new FrameworkElementFactory(typeof(TextBlock));
                caption.SetValue(TextBlock.ForegroundProperty,Brushes.Black);
                caption.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Arial"));
                caption.SetValue(TextBlock.FontSizeProperty,12d);
                caption.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
                caption.SetBinding(TextBlock.TextProperty,
                    new System.Windows.Data.Binding(nameof(CardPaletteEntry.Name)));
                itemRow.AppendChild(caption);
                itemTemplate.VisualTree=itemRow;
                selector.ItemTemplate=itemTemplate;
                selector.SelectedItem=choices.FirstOrDefault(x=>
                    string.Equals(x.Hex,current,StringComparison.OrdinalIgnoreCase));
                selector.SelectionChanged+=(_,_) =>
                {
                    if(selector.SelectedItem is CardPaletteEntry selected)
                        entry.Text=selected.Hex;
                };
                selector.ToolTip="اختر اللون من القائمة المنسدلة؛ الرمز محفوظ داخليا فقط.";
                section.Children.Add(selector);
                section.Children.Add(entry);
                return entry;
            }

            Group("بيانات الكرت");
            var name=Editable("اسم الشبكة",_designTitle.Text);
            var capacity=Editable("نص السعة (اختياري)",_designCapacityText.Text);
            // Keep the old PhoneText storage format for existing saved presets.
            var phoneParts=_designPhoneText.Text.Split(" - ",2,StringSplitOptions.TrimEntries);
            var phone1=Editable("رقم الهاتف الأول (اختياري)",phoneParts[0]);
            var phone2=Editable("رقم الهاتف الثاني (اختياري)",
                phoneParts.Length>1?phoneParts[1]:"");

            Group("ألوان الكرت");
            var background=ColorChoice("لون خلفية البطاقة",_designBackground.Text);
            var numberColor=ColorChoice("لون رقم الكرت",_designTextColor.Text);
            var elementColor=ColorChoice("لون بقية النصوص",_designElementColor.Text);
            var borderColor=ColorChoice("لون الحدود",_designBorderColor.Text);

            Group("الخطوط والمقاسات");
            var fontSize=Choices("حجم رقم الكرت",_designFontSize.Text,
                new[]{"12","14","16","18","20","22","24","28","32","36","40"});
            var font=Choices("نوع الخط",
                _designFontFamily.SelectedItem?.ToString()??"Arial",
                new[]{"Arial","Tahoma","Segoe UI","Calibri"});
            var borderWidth=Choices("سمك الحدود",_designBorderWidth.Text,
                new[]{"0","0.5","1","1.5","2","3","4","5"});
            var elementsScale=Choices("حجم خط العناصر",_designElementScale.Text,
                new[]{"0.5","0.65","0.75","0.85","1","1.1","1.25","1.5","1.75","2","2.5"});

            // The physical dimensions go to the original print model only
            // after CardPrintLayoutPlanner accepts them.
            Group("مقاسات الطباعة",3);
            CardPrintOptions? livePrint=null;
            try{livePrint=PrintOptionsProvider?.Invoke();}
            catch(ArgumentException ex){_designStatus.Text=ex.Message;}
            var width=Editable("عرض الكرت (مم)",
                livePrint?.CardWidthMm.ToString(CultureInfo.InvariantCulture)??"50");
            var height=Editable("ارتفاع الكرت (مم)",
                livePrint?.CardHeightMm.ToString(CultureInfo.InvariantCulture)??"19");
            width.IsEnabled=height.IsEnabled=ApplyPrintOptions is not null;
            var qrSize=Choices("حجم QR",_designQrSize.Text,
                new[]{"12","16","20","24","28","32","40","48","60","72"});

            Group("العناصر والصورة");
            var showPassword=new CheckBox
            {
                Content="إظهار كلمة المرور",IsChecked=_designShowPassword.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#512B3C")!,
                Margin=new Thickness(7,9,7,7),MinHeight=32
            };
            var showProfile=new CheckBox
            {
                Content="إظهار الباقة",IsChecked=_designShowProfile.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#512B3C")!,
                Margin=new Thickness(7,9,7,7),MinHeight=32
            };
            var showPhone=new CheckBox
            {
                Content="عرض أرقام الهاتف على الكرت",
                IsChecked=_designShowPhone.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#512B3C")!,
                Margin=new Thickness(7,9,7,7),MinHeight=32
            };
            fields.Children.Add(showPassword);
            fields.Children.Add(showProfile);
            fields.Children.Add(showPhone);
            var chooseImage=PageUi.Button("اختيار صورة الكرت لهذه الباقة",false);
            chooseImage.MinWidth=0;chooseImage.Height=36;
            chooseImage.Margin=new Thickness(6,5,6,5);
            chooseImage.Foreground=Brushes.White;
            chooseImage.Background=(Brush)new BrushConverter().ConvertFrom("#17757B")!;
            chooseImage.Click+=(_,_)=>ChooseTemplateImage();
            fields.Children.Add(chooseImage);

            // Closing, including Escape/Cancel or the window close icon, must
            // not commit any partial design or template-image changes.
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
            pane.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Content=contentStack
            });
            popup.Content=pane;popup.ShowDialog();
    }
}
