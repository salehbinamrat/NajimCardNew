using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NajmCard.Core.Services;
using NajmCard.Desktop;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Views;

namespace NajmCard.VisualSmoke;

internal static partial class Program
{
    private static readonly List<object> ReleaseFrames=[];
    private static readonly List<object> InteractionChecks=[];

    private static void Pump(int milliseconds=80)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(DispatcherPriority.Background)
            {Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};
        timer.Start();Dispatcher.PushFrame(frame);
    }

    private static string Caption(FrameworkElement element)=>element switch
    {
        ContentControl {Content:string caption}=>caption,
        ContentControl {Content:TextBlock label}=>label.Text,
        TextBlock label=>label.Text,
        _=>element.Name.Length>0?element.Name:element.ToolTip?.ToString()??element.GetType().Name
    };

    private static bool Shown(DependencyObject element)
    {
        // IsVisible also depends on an HWND. The offscreen audit intentionally
        // renders detached production controls, so inspect their visibility chain.
        for(DependencyObject? current=element;current is not null;current=VisualTreeHelper.GetParent(current))
            if(current is UIElement {Visibility:not Visibility.Visible})return false;
        return true;
    }

    private static void CaptureDpiVariants(Grid root,int width,int height,string destination)
    {
        var page=Path.GetFileNameWithoutExtension(destination);
        foreach(var scale in new[]{1d,1.25d,1.5d})
        {
            // SetRootDpi changes WPF text/layout DPI, not merely PNG upsampling.
            // This does not substitute for moving a real window across physical monitors.
            VisualTreeHelper.SetRootDpi(root,new DpiScale(scale,scale));
            root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));
            root.UpdateLayout();
            var measured=VisualTreeHelper.GetDpi(root);
            if(Math.Abs(measured.DpiScaleX-scale)>0.001)
                throw new InvalidDataException("WPF did not apply the requested DPI.");
            var path=scale==1?destination:Path.Combine(Path.GetDirectoryName(destination)!,
                page+"-dpi"+Math.Round(scale*96).ToString(CultureInfo.InvariantCulture)+".png");
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*scale),
                (int)Math.Ceiling(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=File.Create(path))encoder.Save(stream);
            if(new FileInfo(path).Length<10_000)throw new InvalidDataException("Empty WPF render: "+page);
            var controls=VisualChildren<FrameworkElement>(root)
                .Where(c=>Shown(c)&&c.ActualWidth>0&&c.ActualHeight>0&&
                    c is ButtonBase or ComboBox or DatePicker or TabItem or TextBox or PasswordBox)
                .Select(c=>
                {
                    var bounds=c.TransformToAncestor(root).TransformBounds(new Rect(c.RenderSize));
                    return new {type=c.GetType().Name,name=c.Name,label=Caption(c),enabled=c.IsEnabled,
                        x=bounds.X,y=bounds.Y,width=bounds.Width,height=bounds.Height,
                        withinFrame=bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=width+1&&bounds.Bottom<=height+1};
                }).ToArray();
            ReleaseFrames.Add(new{page,dpi=96*scale,width=bitmap.PixelWidth,height=bitmap.PixelHeight,
                controls,screenshot=Path.GetFileName(path)});
            Console.WriteLine("WPF screenshot: "+path);
        }
        VisualTreeHelper.SetRootDpi(root,new DpiScale(1,1));
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
    }

    private static void AuditAdditionalPages(Grid root,ContentControl host,MainWindow shell,
        RouterGateway gateway,VoucherStore store,int width,int height,string folder)
    {
        var pages=new (string Name,int Nav,Func<FrameworkElement> Create)[]
        {
            ("cards",2,()=>new CardsView(gateway,store)),
            ("profiles",3,()=>new ProfilesView(gateway,audit:store)),
            ("sessions",5,()=>new SessionsView(gateway)),
            ("servers",6,()=>new ServersView(gateway)),
            ("reports",7,()=>new ReportsView(store)),
            ("activity",8,()=>new ActivityView(store)),
            ("network-maintenance",9,()=>new NetworkMaintenanceView(gateway,"127.0.0.1")),
            ("hotspot-templates",9,()=>new ToolsView(gateway,"127.0.0.1")),
            ("settings",10,()=>new SettingsView(store))
        };
        foreach(var (name,nav,create) in pages)
        {
            ((ListBox)shell.FindName("NavList")).SelectedIndex=nav;
            ((RowDefinition)shell.FindName("WorkspaceHeaderRow")).Height=new GridLength(88);
            host.Content=create();root.UpdateLayout();Pump();root.UpdateLayout();
            AuditPage(name,(FrameworkElement)host.Content,root,width,height,folder);
            var tabs=VisualChildren<TabControl>((FrameworkElement)host.Content).ToArray();
            foreach(var tab in tabs)
            {
                var selected=tab.SelectedIndex;
                for(var i=0;i<tab.Items.Count;i++)
                {
                    tab.SelectedIndex=i;root.UpdateLayout();Pump();
                    AuditPage(name+"-tab"+i,(FrameworkElement)host.Content,root,width,height,folder);
                }
                tab.SelectedIndex=selected;
            }
        }
        // Production non-modal progress and recovery dialogs, with real long Arabic text.
        foreach(var name in new[]{"VoucherOperationProgressWindow","VoucherDeliveryHistoryWindow"})
        {
            var type=typeof(MainWindow).Assembly.GetType("NajmCard.Desktop.Views."+name)!;
            var args=name=="VoucherOperationProgressWindow"
                ?new object[]{"إرسال الدفعة والتحقق من الحفظ والاتصال"}:new object?[]{store,gateway};
            var dialog=(Window)Activator.CreateInstance(type,args)!;
            if(name=="VoucherOperationProgressWindow")type.GetMethod("UpdateStage")!.Invoke(dialog,
                new object[]{"تعذر تأكيد نتيجة العملية بعد انقطاع الاتصال. يجب مراجعة سجل الدفعة ومطابقتها مع الراوتر الصحيح قبل أي محاولة استكمال."});
            var content=(FrameworkElement)dialog.Content;dialog.Content=null;
            var frame=new Grid{FlowDirection=FlowDirection.RightToLeft};frame.Children.Add(content);
            content.Measure(new Size(dialog.Width,double.PositiveInfinity));
            var dialogHeight=dialog.SizeToContent==SizeToContent.Height
                ?Math.Max(dialog.MinHeight,Math.Ceiling(content.DesiredSize.Height)):dialog.Height;
            CaptureDpiVariants(frame,(int)dialog.Width,(int)dialogHeight,
                Path.Combine(folder,name+".png"));dialog.Close();
            if(name=="VoucherOperationProgressWindow")
                foreach(var text in VisualChildren<TextBlock>(content).Where(t=>t.ActualHeight>0))
                {
                    var bounds=text.TransformToAncestor(frame).TransformBounds(new Rect(text.RenderSize));
                    if(bounds.Bottom>dialogHeight+1)
                        throw new InvalidDataException("A long progress message hides the confirmed count or progress indicator.");
                }
        }
    }

    private static void AuditLiveShell(RouterGateway gateway,string folder)
    {
        // Only the test assembly injects this read-only gateway. The shipped
        // login, trial and paid-license checks are unchanged.
        var shell=new MainWindow();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        if(typeof(MainWindow).GetField("_storedPaidCode",flags)!.GetValue(shell) is string code&&code.Length>0)
            throw new InvalidOperationException("The live UI audit requires a disposable profile without a paid code.");
        shell.Show();Pump(200);shell.UpdateLayout();
        var work=SystemParameters.WorkArea;
        if(shell.Width>work.Width+1||shell.Height>work.Height+1)
            throw new InvalidDataException("The real main window does not fit the Windows work area.");
        var connect=(Button)shell.FindName("ConnectButton");
        var bounds=connect.TransformToAncestor(shell).TransformBounds(new Rect(connect.RenderSize));
        if(bounds.Bottom>shell.ActualHeight+1||bounds.Top<0||bounds.Width<=0)
            throw new InvalidDataException("The actual login action is clipped.");

        // Exercise input guards without any attempt to connect or activate.
        ((TextBox)shell.FindName("LicenseCodeInput")).Text="invalid";
        ((Button)shell.FindName("ActivateLicenseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(!((TextBlock)shell.FindName("LicenseNotice")).Text.Contains("32",StringComparison.Ordinal))
            throw new InvalidDataException("Invalid activation code was not rejected locally.");
        ((Button)shell.FindName("TrialButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((TextBox)shell.FindName("PortInput")).Text="invalid";
        connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        if(!((TextBlock)shell.FindName("LoginStatus")).Text.Contains("منفذ API غير صالح",StringComparison.Ordinal))
            throw new InvalidDataException("The invalid-port login guard failed.");
        InteractionChecks.Add(new{page="login",type="guards",outcome="invalid license and invalid port rejected without network"});

        typeof(MainWindow).GetField("_gateway",flags)!.SetValue(shell,gateway);
        ((Grid)shell.FindName("LoginLayer")).Visibility=Visibility.Collapsed;
        ((Grid)shell.FindName("WorkspaceLayer")).Visibility=Visibility.Visible;
        var nav=(ListBox)shell.FindName("NavList");nav.SelectedIndex=-1;
        var host=(ContentControl)shell.FindName("PageHost");
        var expected=new[]{typeof(DashboardView),typeof(VoucherWorkspaceView),typeof(CardsView),
            typeof(ProfilesView),typeof(UserManagerView),typeof(SessionsView),typeof(ServersView),
            typeof(ReportsView),typeof(ActivityView),typeof(NetworkMaintenanceView),typeof(SettingsView)};
        for(var i=0;i<expected.Length;i++)
        {
            nav.SelectedIndex=i;Pump();shell.UpdateLayout();
            if(host.Content?.GetType()!=expected[i])
                throw new InvalidDataException("Actual sidebar navigation failed: "+i);
            InteractionChecks.Add(new{page=expected[i].Name,type="sidebar",outcome="actual window selection and destination verified"});
        }
        var logout=VisualChildren<Button>(shell).Single(b=>Equals(b.Content,"تسجيل الخروج"));
        logout.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        if(((Grid)shell.FindName("LoginLayer")).Visibility!=Visibility.Visible||host.Content is not null||
           typeof(MainWindow).GetField("_gateway",flags)!.GetValue(shell) is not null)
            throw new InvalidDataException("Logout left an active page or router context.");
        InteractionChecks.Add(new{page="shell",type="logout",outcome="login restored; page and gateway cleared"});
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(shell.ActualWidth),
            (int)Math.Ceiling(shell.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(shell);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(Path.Combine(folder,"actual-window-login.png")))encoder.Save(stream);
        shell.Close();Pump();
    }

    private static void AuditPage(string name,FrameworkElement page,Grid root,int width,int height,string folder)
    {
        var combos=VisualChildren<ComboBox>(page).Where(c=>Shown(c)&&c.IsEnabled).ToArray();
        foreach(var combo in combos)
        {
            var original=combo.SelectedIndex;
            var exercised=0;
            foreach(var index in new[]{0,combo.Items.Count-1}.Distinct().Where(i=>i>=0&&i<combo.Items.Count))
            {
                combo.SelectedIndex=index;root.UpdateLayout();Pump();exercised++;
            }
            combo.SelectedIndex=original;root.UpdateLayout();
            InteractionChecks.Add(new{page=name,type="ComboBox",label=Caption(combo),items=combo.Items.Count,
                exercised,outcome=exercised>0?"selection changed and restored":"empty fixture; field population pending"});
        }
        foreach(var button in VisualChildren<Button>(page).Where(Shown).ToArray())
        {
            var label=Caption(button);
            // Read/filter commands use the in-memory gateway. Writes, OS dialogs and
            // external processes are explicitly catalogued rather than called verified.
            var read=button.IsEnabled&&(label.StartsWith("تحديث",StringComparison.Ordinal)||
                label.StartsWith("بحث",StringComparison.Ordinal)||label.StartsWith("قراءة",StringComparison.Ordinal));
            if(read)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(150);root.UpdateLayout();
            }
            InteractionChecks.Add(new{page=name,type="Button",label,enabled=button.IsEnabled,
                outcome=read?"invoked using offline fixture":"rendered; see action acceptance matrix"});
        }
        CaptureDpiVariants(root,width,height,Path.Combine(folder,"release-"+name+".png"));
    }

    private static void WriteReleaseAudit(string folder)
    {
        var expected=Environment.GetEnvironmentVariable("NAJM_EXPECT_ARCH");
        var architecture=RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        if(expected is not null&&architecture!=expected)
            throw new InvalidDataException("Visual audit ran in "+architecture+" instead of "+expected);
        File.WriteAllText(Path.Combine(folder,"release-ui-audit.json"),JsonSerializer.Serialize(new
        {
            architecture,IntPtr.Size,approvedWindow=new{width=1260,height=730},
            dpiMethod="WPF SetRootDpi 96/120/144; physical monitor changes require field acceptance",
            frames=ReleaseFrames,interactions=InteractionChecks,
            realRouterUsed=false,physicalPrinterUsed=false
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
