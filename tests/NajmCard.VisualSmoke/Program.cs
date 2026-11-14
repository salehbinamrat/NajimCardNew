using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NajmCard.Core.Services;
using NajmCard.Desktop;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Views;

namespace NajmCard.VisualSmoke;

/// <summary>
/// Renders the production WPF shell and card creator without a router.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var destination=Path.GetFullPath(args.Length>0
                ? args[0] : "out/creator-1260x730.png");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source=new Uri("pack://application:,,,/NajmCard;component/Themes/BurgundyGold.xaml")
            });
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source=new Uri("pack://application:,,,/NajmCard;component/Themes/VectorIcons.xaml")
            });
            VerifyTooltipTheme(application);
            var testData=Path.Combine(Path.GetTempPath(),
                "najmcard-ui-screenshot-"+Guid.NewGuid().ToString("N"));
            using var store=new VoucherStore(testData);
            var offline=new RouterGateway(new OfflineRouterClient());
            var creator=new VoucherWorkspaceView(offline,store,"offline-preview:8728");
            var window=new MainWindow();
            var width=(int)window.Width;
            var height=(int)window.Height;
            if(width!=1260 || height!=730)
                throw new InvalidDataException("Unexpected default window size.");
            if(window.ResizeMode!=ResizeMode.CanMinimize ||
               !window.ShowInTaskbar ||
               window.WindowState!=WindowState.Normal)
                throw new InvalidDataException(
                    "The fixed-size main window must minimize to the Windows taskbar.");

            ((Grid)window.FindName("LoginLayer")).Visibility=Visibility.Collapsed;
            ((Grid)window.FindName("WorkspaceLayer")).Visibility=Visibility.Visible;
            ((ListBox)window.FindName("NavList")).SelectedIndex=1;
            ((RowDefinition)window.FindName("WorkspaceHeaderRow")).Height=new GridLength(0);
            var pageHost=(ContentControl)window.FindName("PageHost");
            pageHost.Margin=new Thickness(12,7,12,7);
            pageHost.Content=creator;
            var root=(Grid)window.Content;
            root.FlowDirection=window.FlowDirection;
            window.Content=null;

            root.Measure(new Size(width,height));
            root.Arrange(new Rect(0,0,width,height));
            root.UpdateLayout();

            // Capture the actual fixed-size login form as well as both workspaces.
            var loginLayer=(Grid)window.FindName("LoginLayer");
            var workspaceLayer=(Grid)window.FindName("WorkspaceLayer");
            workspaceLayer.Visibility=Visibility.Collapsed;
            loginLayer.Visibility=Visibility.Visible;
            // Use a synthetic, non-secret history row to verify selecting a
            // saved connection fills the real login form without API activity.
            var example=new SavedRouterConnection("192.168.220.1",8728,"admin",
                false,"local");
            ((ComboBox)window.FindName("SavedConnectionsInput")).ItemsSource=
                new[]{example};
            var history=(DataGrid)window.FindName("HistoryConnectionsGrid");
            history.ItemsSource=new[]{example};
            ((Expander)window.FindName("SavedHistoryExpander")).IsExpanded=true;
            history.SelectedItem=example;
            root.UpdateLayout();
            if(((TextBox)window.FindName("HostInput")).Text!="192.168.220.1" ||
               ((TextBox)window.FindName("UserInput")).Text!="admin")
                throw new InvalidDataException("Selecting a saved router did not fill the login fields.");
            var connect=(Button)window.FindName("ConnectButton");
            var connectBounds=connect.TransformToAncestor(root)
                .TransformBounds(new Rect(connect.RenderSize));
            if(connectBounds.Width<=0 || connectBounds.Height<=0 ||
                connectBounds.Bottom>height || connectBounds.Top<0)
                throw new InvalidDataException("The login connect button is clipped at 1260x730.");
            Capture(root,width,height,Path.Combine(
                Path.GetDirectoryName(destination)!,"login-1260x730.png"));
            loginLayer.Visibility=Visibility.Collapsed;
            workspaceLayer.Visibility=Visibility.Visible;
            root.UpdateLayout();

            // Verify the production creator's actual first rendered state,
            // not only its source-code ordering or later tab transitions.
            var generator=VisualChildren<GeneratorView>(creator).Single();
            if(generator.CurrentService!="userman")
                throw new InvalidDataException("User Manager must be selected at first paint.");
            var serviceTabs=VisualChildren<UniformGrid>(creator)
                .FirstOrDefault(grid=>grid.Columns==3 &&
                    grid.Children.OfType<Button>().Any(button=>
                        button.Content is TextBlock caption && caption.Text=="يوزمنجر v6"));
            if(serviceTabs is null)
                throw new InvalidDataException("The three card-creation services are missing.");
            var serviceButtons=serviceTabs.Children.OfType<Button>().ToArray();
            var serviceNames=serviceButtons.Select(button=>
                (button.Content as TextBlock)?.Text??"").ToArray();
            if(!serviceNames.SequenceEqual(new[]{"يوزمنجر v6","هوتسبوت","برودباند PPPoE"}))
                throw new InvalidDataException("Creator tabs are not User Manager, Hotspot, Broadband.");
            if(serviceButtons[0].Background is not SolidColorBrush activeColor ||
                activeColor.Color!=Color.FromRgb(0xEA,0xC7,0x7C))
                throw new InvalidDataException("User Manager is not visually active at first paint.");

            var workspace=(DockPanel)creator.Content;
            var footer=(StackPanel)workspace.Children[0];
            var footerBounds=footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
            if(footer.ActualHeight<=0 || footerBounds.Bottom>height)
                throw new InvalidDataException("The creator action bar is outside the window.");

            var elements=VisualChildren<TextBlock>(root).Single(text=>text.Text=="عناصر الكرت");
            var bounds=elements.TransformToAncestor(root).TransformBounds(new Rect(elements.RenderSize));
            if(bounds.Height<=0 || bounds.Bottom>footerBounds.Top+1)
                throw new InvalidDataException("The default creator view clips the card design controls.");
            if(VisualChildren<UniformGrid>(root).Any(grid=>Equals(grid.Tag,"CardPalette")))
                throw new InvalidDataException("The removed color strip is still visible.");

            var creatorHeader=(Border)workspace.Children[1];
            VerifyNumberPreview(root);
            VerifyPasswordPreview(root);
            root.UpdateLayout();

            Capture(root,width,height,destination);

            var creatorHeaderHeight=creatorHeader.ActualHeight;
            var dashboard=new DashboardView(offline,store,_=>{},"192.168.88.1");
            ((ListBox)window.FindName("NavList")).SelectedIndex=0;
            pageHost.Margin=new Thickness(12,7,12,7);
            pageHost.Content=dashboard;
            root.UpdateLayout();
            VerifyDashboardPanels(dashboard);
            var dashboardHeader=(Border)((Grid)dashboard.Content).Children[0];
            if(creatorHeaderHeight!=dashboardHeader.ActualHeight ||
               !Equals(((SolidColorBrush)creatorHeader.Background).Color,
                   ((SolidColorBrush)dashboardHeader.Background).Color))
                throw new InvalidDataException("The creator and dashboard header styles differ.");
            Capture(root,width,height,Path.Combine(Path.GetDirectoryName(destination)!,"dashboard-1260x730.png"));

            ((RowDefinition)window.FindName("WorkspaceHeaderRow")).Height=new GridLength(88);
            var profiles=new ProfilesView(new RouterGateway(new OfflineRouterClient(withProfiles:true)));
            pageHost.Content=profiles;
            VerifyProfilesPage(root,profiles);

            // Render the actual first screen after clicking User Manager.
            ((RowDefinition)window.FindName("WorkspaceHeaderRow")).Height=new GridLength(0);
            pageHost.Margin=new Thickness(12,7,12,7);
            ((ListBox)window.FindName("NavList")).SelectedIndex=4;
            var userManager=new UserManagerView(offline,store);
            pageHost.Content=userManager;
            root.UpdateLayout();
            VerifyUserManagerHome(root,userManager);
            Capture(root,width,height,Path.Combine(Path.GetDirectoryName(destination)!,
                "user-manager-home-1260x730.png"));
            CaptureUserManagerSections(root,userManager,width,height,
                Path.GetDirectoryName(destination)!);

            // Render the production settings view at the same fixed
            // 1260x730 size, including the opt-in Portable shortcut action.
            // The shortcut must NOT be created during this render.
            var settings=new SettingsView(store);
            ((ListBox)window.FindName("NavList")).SelectedIndex=10;
            pageHost.Content=settings;
            root.UpdateLayout();
            var shortcutButton=VisualChildren<Button>(settings)
                .Single(x=>Equals(x.Content,"إنشاء اختصار على سطح المكتب"));
            var shortcutBounds=shortcutButton.TransformToAncestor(root)
                .TransformBounds(new Rect(shortcutButton.RenderSize));
            if(shortcutBounds.Height<28 || shortcutBounds.Left<0 ||
               shortcutBounds.Right>root.ActualWidth+1 ||
               shortcutBounds.Bottom>height)
                throw new InvalidDataException(
                    "Settings desktop shortcut button is clipped at 1260x730.");
            if(!VisualChildren<Border>(settings).Any(panel=>
                panel.Background is SolidColorBrush brush &&
                brush.Color==Color.FromRgb(0xE8,0xED,0xF5)))
                throw new InvalidDataException(
                    "Settings view must use the shared silver card background.");
            Capture(root,width,height,Path.Combine(Path.GetDirectoryName(destination)!,
                "settings-1260x730.png"));
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine("WPF visual smoke test failed: "+ex);
            return 1;
        }
    }

    private static void Capture(Grid root,int width,int height,string destination)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(destination))encoder.Save(stream);
        if(new FileInfo(destination).Length<10_000)
            throw new InvalidDataException("Rendered UI PNG appears empty or incomplete.");
        Console.WriteLine("WPF screenshot: "+destination);
    }

    private static void VerifyDashboardPanels(DashboardView dashboard)
    {
        // Stage one has four equal live counters, three equal middle panels,
        // and a wider lower control panel between two balanced side panels.
        // Each panel must remain fully visible at the owner's 1260x730 window.
        var layout=(Grid)dashboard.Content;
        var tiers=layout.Children.OfType<Grid>()
            .Where(grid=>Grid.GetRow(grid) is 1 or 2 or 3)
            .OrderBy(Grid.GetRow).ToArray();
        var expected=new[]{4,3,3};
        if(tiers.Length!=expected.Length)
            throw new InvalidDataException("Dashboard tier layout is incomplete.");
        for(int i=0;i<tiers.Length;i++)
        {
            var cards=tiers[i].Children.OfType<Border>()
                .Where(panel=>Equals(panel.Tag,"DashboardPanel")).ToArray();
            if(cards.Length!=expected[i])
                throw new InvalidDataException("Dashboard panels missing in tier "+(i+1)+".");
            var bounds=cards.Select(card=>card.TransformToAncestor(dashboard)
                .TransformBounds(new Rect(card.RenderSize))).ToArray();
            if(bounds.Any(card=>card.Width<=0 || card.Height<=0 || card.Left<0 ||
                card.Right>dashboard.ActualWidth+1 || card.Bottom>dashboard.ActualHeight+1))
                throw new InvalidDataException("A dashboard card is clipped.");
            if(i<2 && bounds.Max(card=>card.Width)-bounds.Min(card=>card.Width)>2)
                throw new InvalidDataException("Dashboard cards are misaligned in tier "+(i+1)+".");
            if(i==2 && (Math.Abs(bounds[0].Width-bounds[2].Width)>2 ||
                        bounds[1].Width<=bounds[0].Width))
                throw new InvalidDataException("Dashboard lower control panel lost its intended width.");
        }
    }

    private static void VerifyProfilesPage(FrameworkElement root,ProfilesView profiles)
    {
        var tabs=(TabControl)profiles.Content;
        tabs.SelectedIndex=1;
        root.UpdateLayout();
        var tab=(TabItem)tabs.SelectedItem;
        var scroll=(ScrollViewer)((DockPanel)tab.Content).Children[1];
        var grids=VisualChildren<DataGrid>(profiles).ToArray();
        if(grids.Length!=2 || grids[0].Items.Count!=1 ||
           !VisualChildren<TextBlock>(profiles).Any(x=>x.Text.Contains("تعذرت قراءة المحددات")))
            throw new InvalidDataException("User Manager profiles disappeared when loading their limits failed.");
        var refresh=VisualChildren<Button>(profiles).Single(x=>Equals(x.Content,"تحديث من الراوتر"));
        var top=refresh.TransformToAncestor(root).TransformBounds(new Rect(refresh.RenderSize)).Top;
        scroll.ScrollToEnd();
        root.UpdateLayout();
        if(scroll.VerticalOffset<=0 ||
           refresh.TransformToAncestor(root).TransformBounds(new Rect(refresh.RenderSize)).Top!=top)
            throw new InvalidDataException("Profile details are not scrollable below the fixed refresh button.");
    }

    private static void VerifyUserManagerHome(FrameworkElement root,UserManagerView manager)
    {
        if(manager.Content is not UserManagerHomeView home)
            throw new InvalidDataException("User Manager must open six-card home, not tables.");
        var cards=VisualChildren<Button>(home).Where(button=>button.Tag is string).ToArray();
        var expected=new[]{"users","packages","sessions","search","reports","maintenance"};
        if(!cards.Select(x=>x.Tag?.ToString()).SequenceEqual(expected))
            throw new InvalidDataException("User Manager home card order does not match reference.");
        foreach(var card in cards)
        {
            var bounds=card.TransformToAncestor(root).TransformBounds(new Rect(card.RenderSize));
            if(bounds.Width<160 || bounds.Height<120 || bounds.Left<0 ||
                bounds.Right>root.ActualWidth+1 || bounds.Top<0 ||
                bounds.Bottom>root.ActualHeight+1)
                throw new InvalidDataException(
                    "User Manager card is clipped at 1260x730: "+card.Tag);
        }
        if(!VisualChildren<TextBlock>(home).Any(x=>x.Text=="إدارة User Manager"))
            throw new InvalidDataException("User Manager landing banner is missing.");
        // Confirm a real card opens the existing explorer, then returns home.
        cards[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        root.UpdateLayout();
        if(manager.Content is not DockPanel)
            throw new InvalidDataException("Users card did not navigate to explorer.");
        var back=VisualChildren<Button>(manager).FirstOrDefault(b=>
            b.Content is string label && label.Contains("الرئيسية",StringComparison.Ordinal));
        if(back is null)
            throw new InvalidDataException("Explorer has no back to User Manager home.");
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        root.UpdateLayout();
        if(manager.Content is not UserManagerHomeView)
            throw new InvalidDataException("Returning to six-card home failed.");
    }

    /// <summary>
    /// Render the six REAL WPF subpages via their production home-card buttons.
    /// The test router remains offline, so no demo records are used.
    /// </summary>
    private static void CaptureUserManagerSections(Grid root,UserManagerView manager,
        int width,int height,string folder)
    {
        var pages=new (string Tag,string Title)[]
        {
            ("users","إدارة المستخدمين"),
            ("packages","إدارة الباقات"),
            ("sessions","الجلسات"),
            ("search","البحث المتقدم"),
            ("reports","التقارير"),
            ("maintenance","النسخ والصيانة")
        };
        foreach(var (tag,title) in pages)
        {
            if(manager.Content is not UserManagerHomeView home)
                throw new InvalidDataException(
                    "The User Manager home is not restored before "+tag);
            var card=VisualChildren<Button>(home).Single(x=>
                string.Equals(x.Tag?.ToString(),tag,StringComparison.Ordinal));
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            root.UpdateLayout();
            if(manager.Content is not DockPanel)
                throw new InvalidDataException(
                    "Clicking "+tag+" did not open a User Manager subpage.");
            if(!VisualChildren<TextBlock>(manager).Any(x=>
                x.Text.Equals(title,StringComparison.Ordinal)))
                throw new InvalidDataException(
                    "Selected User Manager subpage has no heading: "+title);
            VerifyUmControlContrast(manager,tag);
            var sidebar=VisualChildren<Button>(manager).FirstOrDefault(button=>
                button.Content is string label &&
                label.Contains("الرئيسية",
                    StringComparison.Ordinal));
            if(sidebar is null)
                throw new InvalidDataException("User Manager page has no back action.");
            Capture(root,width,height,Path.Combine(folder,
                "user-manager-"+tag+"-1260x730.png"));
            sidebar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            root.UpdateLayout();
        }
    }

    private static void VerifyUmControlContrast(UserManagerView manager,string section)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var n=value/255.0;
                return n<=0.04045?n/12.92:Math.Pow((n+0.055)/1.055,2.4);
            }
            return Channel(color.R)*0.2126+Channel(color.G)*0.7152+
                Channel(color.B)*0.0722;
        }
        static double Contrast(Brush first,Brush second)
        {
            if(first is not SolidColorBrush background ||
                second is not SolidColorBrush foreground)
                throw new InvalidDataException("UM control colors must be solid and measurable.");
            var a=Luminance(background.Color);
            var b=Luminance(foreground.Color);
            return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
        }
        int checkedButtons=0;
        foreach(var button in VisualChildren<Button>(manager)
            .Where(button=>button.Content is string))
        {
            button.ApplyTemplate();
            if(button.Template.FindName("PART_ActionText",button) is not TextBlock text)
                continue; // Existing legacy/global templates are tested separately.
            checkedButtons++;
            if(text.Text!=button.Content?.ToString())
                throw new InvalidDataException("A User Manager button label disappeared: "+section);
            if(Contrast(button.Background,text.Foreground)<4.5)
                throw new InvalidDataException("Low-contrast User Manager button: "+button.Content);
        }
        if(checkedButtons<3)
            throw new InvalidDataException("No real UM action buttons were contrast-tested: "+section);

        int checkedCombos=0;
        foreach(var combo in VisualChildren<ComboBox>(manager))
        {
            if(combo.ItemContainerStyle is null)continue;
            combo.ApplyTemplate();
            if(combo.Template.FindName("PART_SelectedText",combo) is not TextBlock selected)
                throw new InvalidDataException("No selected-text part in UM ComboBox: "+section);
            checkedCombos++;
            if(combo.SelectedItem is string value &&
                (selected.Text!=value || Contrast(combo.Background,selected.Foreground)<4.5))
                throw new InvalidDataException("Invisible selected dropdown text: "+section);
            var item=new ComboBoxItem {Content="اختبار القراءة",Style=combo.ItemContainerStyle};
            item.ApplyTemplate();
            if(item.Template.FindName("ItemText",item) is not TextBlock itemText ||
                itemText.Text!="اختبار القراءة" ||
                Contrast(item.Background,itemText.Foreground)<4.5)
                throw new InvalidDataException("Invisible dropdown list item: "+section);
        }
        if(section is "users" or "packages" or "sessions" or "search" or "reports")
        {
            if(checkedCombos==0)
                throw new InvalidDataException("No UM dropdown tested in "+section);
        }
    }

    private static void VerifyNumberPreview(FrameworkElement root)
    {
        var number=VisualChildren<TextBlock>(root).Single(text=>text.Name=="CardNumberPreview");
        double Width()=>number.TransformToAncestor(root).TransformBounds(new Rect(number.RenderSize)).Width;
        var initial=Width();
        number.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120)
        {
            RoutedEvent=Mouse.PreviewMouseWheelEvent
        });
        root.UpdateLayout();
        if(Width()>=initial-0.1)
            throw new InvalidDataException("Reducing the card number size did not shrink the rendered text.");
        number.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120)
        {
            RoutedEvent=Mouse.PreviewMouseWheelEvent
        });
        root.UpdateLayout();
    }

    private static void VerifyPasswordPreview(FrameworkElement root)
    {
        var mode=VisualChildren<ComboBox>(root).Single(box=>
            box.Items.Contains("اسم مستخدم وكلمة مرور"));
        var password=VisualChildren<TextBlock>(root).Single(text=>text.Name=="CardPasswordPreview");
        if(password.Visibility!=Visibility.Collapsed)
            throw new InvalidDataException("Username-only cards must hide the password.");
        mode.SelectedIndex=1;
        root.UpdateLayout();
        if(password.Visibility!=Visibility.Visible || password.Text.Length==0 ||
            password.ActualWidth<=0)
            throw new InvalidDataException("The separate password preview did not render.");
        var initialSize=password.FontSize;
        password.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120)
        {
            RoutedEvent=Mouse.PreviewMouseWheelEvent
        });
        if(password.FontSize<=initialSize)
            throw new InvalidDataException("The password did not resize with the mouse wheel.");
        mode.SelectedIndex=0;
        if(password.Visibility!=Visibility.Collapsed || password.Text.Length!=0)
            throw new InvalidDataException("The preview retained the previous password.");
    }

    private static void VerifyTooltipTheme(Application application)
    {
        if(application.TryFindResource(typeof(ToolTip)) is not Style tooltipStyle)
            throw new InvalidDataException("A global WPF ToolTip style is required.");
        var tooltip=new ToolTip
        {
            Content="تحديث الباقات من الراوتر",
            Style=tooltipStyle
        };
        tooltip.ApplyTemplate();
        if(tooltip.Background is not SolidColorBrush background ||
           background.Color!=Color.FromRgb(0xE8,0xED,0xF5))
            throw new InvalidDataException("Tooltip background is not the silver panel palette.");
        if(tooltip.Foreground is not SolidColorBrush foreground ||
           foreground.Color!=Color.FromRgb(0x0B,0x2C,0x60))
            throw new InvalidDataException("Tooltip text must have dark readable contrast.");
        if(tooltip.BorderBrush is not SolidColorBrush border ||
           border.Color!=Color.FromRgb(0x07,0x5D,0xFF))
            throw new InvalidDataException("Tooltip is missing the electric blue outline.");
        if(tooltip.Template?.FindName("TooltipFrame",tooltip) is not Border frame ||
           frame.CornerRadius.TopLeft<6)
            throw new InvalidDataException("Tooltip did not apply the rounded popup template.");
        if(!Equals(tooltip.Content,"تحديث الباقات من الراوتر"))
            throw new InvalidDataException("Tooltip text was changed by the theme.");
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var descendant in VisualChildren<T>(child))yield return descendant;
        }
    }

    private sealed class OfflineRouterClient(bool withProfiles=false) : IRouterCommandClient
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            // Read-only fake: offscreen constructor cannot contact a router.
            if(withProfiles && command=="/tool/user-manager/profile/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    [new Dictionary<string,string>{{".id","*1"},{"name","200RY"}}]);
            if(withProfiles && command=="/tool/user-manager/profile/limitation/print")
                throw new IOException("Limitation read deliberately unavailable.");
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                Array.Empty<IReadOnlyDictionary<string,string>>());
        }
    }
}
