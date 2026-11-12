using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// An explicit read-only User Manager report, backed by live RouterOS catalog
/// data. Export never includes credentials and never sends write commands.
/// </summary>
public sealed class UserManagerReportsView : UserControl
{
    public UserManagerReportsView(RouterGateway gateway,Action back)
    {
        var layout=PageUi.Column();
        var returnButton=PageUi.Button("العودة إلى إدارة User Manager",false);
        returnButton.Click+=(_,_)=>back();
        layout.Children.Add(returnButton);
        layout.Children.Add(PageUi.Title("تقارير User Manager"));
        layout.Children.Add(PageUi.Hint(
            "التقارير من بيانات User Manager المقروءة، وليست من جميع كروت Hotspot. "+
            "لا تصدر كلمات مرور ولا تنفذ أوامر تعديل."));

        var mode=new ComboBox {Width=165,Height=35,Margin=new Thickness(4)};
        foreach(var (title,value) in new[]
        {
            ("المستخدمون",UserManagerCatalogSection.Users),
            ("الباقات",UserManagerCatalogSection.Profiles),
            ("القيود",UserManagerCatalogSection.Limitations),
            ("الجلسات",UserManagerCatalogSection.Sessions)
        })
            mode.Items.Add(new ComboBoxItem {Content=title,Tag=value});
        mode.SelectedIndex=0;
        var load=PageUi.Button("قراءة التقرير");
        var export=PageUi.Button("تصدير CSV",false);
        export.IsEnabled=false;
        var actions=new WrapPanel();
        actions.Children.Add(PageUi.Label("نوع التقرير"));
        actions.Children.Add(mode);
        actions.Children.Add(load);
        actions.Children.Add(export);
        layout.Children.Add(actions);

        var status=PageUi.Status();
        status.Text="اختر نوع التقرير واضغط قراءة التقرير لجلب البيانات من الراوتر.";
        layout.Children.Add(status);
        var grid=PageUi.Table(
            ("الاسم","Name"),("الباقة","Profile"),("الحالة","Status"),
            ("السعة","Limit"),("الوقت","Uptime"),("التنزيل","Download"),
            ("الرفع","Upload"),("العنوان","Address"));
        grid.IsReadOnly=true;
        grid.CanUserAddRows=false;
        grid.CanUserDeleteRows=false;
        grid.MinHeight=275;
        grid.MaxHeight=380;
        layout.Children.Add(grid);
        Content=PageUi.Wrap("تقارير User Manager",
            "قراءة فعلية وتصدير محلي للنتائج غير الحساسة.",
            PageUi.Surface(layout,"#0A8F9A"));

        IReadOnlyList<UserManagerCatalogRow> snapshot=[];
        UserManagerCatalogSection? loadedSection=null;
        CancellationTokenSource? pending=null;
        bool busy=false;
        UserManagerCatalogSection Selected() =>
            mode.SelectedItem is ComboBoxItem {Tag:UserManagerCatalogSection section}
                ?section:UserManagerCatalogSection.Users;

        void Clear()
        {
            snapshot=[];
            loadedSection=null;
            grid.ItemsSource=null;
            export.IsEnabled=false;
        }

        mode.SelectionChanged+=(_,_)=>Clear();
        load.Click+=async (_,_) =>
        {
            if(busy || !IsLoaded)return;
            busy=true;
            load.IsEnabled=false;
            mode.IsEnabled=false;
            Clear();
            status.Text="يجري جلب التقرير من RouterOS...";
            using var request=new CancellationTokenSource();
            pending=request;
            try
            {
                var result=await gateway.ReadUserManagerCatalogAsync(Selected(),request.Token);
                if(!IsLoaded || request.IsCancellationRequested)return;
                snapshot=result.Rows;
                loadedSection=result.Section;
                grid.ItemsSource=snapshot;
                export.IsEnabled=true;
                var enabled=result.Section==UserManagerCatalogSection.Users
                    ?snapshot.Count(x=>x.Status=="مفعل"):0;
                var disabled=result.Section==UserManagerCatalogSection.Users
                    ?snapshot.Count(x=>x.Status=="معطل"):0;
                status.Text=$"RouterOS {(int)result.Generation} | النتائج: {snapshot.Count}"+
                    (result.Section==UserManagerCatalogSection.Users
                        ?$" | المفعلة: {enabled} | المعطلة: {disabled}":"")+
                    " | هذه إحصائيات القائمة المحملة فقط.";
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                Clear();
                status.Text="تعذر قراءة التقرير: "+ex.Message;
            }
            finally
            {
                pending=null;
                busy=false;
                load.IsEnabled=true;
                mode.IsEnabled=true;
            }
        };
        export.Click+=async (_,_) =>
        {
            if(busy || loadedSection is null || loadedSection!=Selected())return;
            var picker=new SaveFileDialog
            {
                Filter="CSV (*.csv)|*.csv",DefaultExt=".csv",AddExtension=true,
                FileName="NajimCardNew-UserManager-"+loadedSection+"-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".csv"
            };
            if(picker.ShowDialog()!=true)return;
            var rows=snapshot.ToArray();
            busy=true;
            load.IsEnabled=export.IsEnabled=false;
            try
            {
                static string Escape(string value)
                {
                    // Avoid interpreting router-controlled cells as Excel formulae.
                    if(value.Length>0 && "=+-@".Contains(value[0]))value="'"+value;
                    return "\"" + value.Replace("\"","\"\"") + "\"";
                }
                var path=picker.FileName;
                await Task.Run(()=>
                {
                    using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
                    writer.WriteLine("Id,Name,Profile,Status,Limit,Uptime,Download,Upload,Address,Extra");
                    foreach(var row in rows)
                        writer.WriteLine(string.Join(",",new[]{
                            row.Id,row.Name,row.Profile,row.Status,row.Limit,
                            row.Uptime,row.Download,row.Upload,row.Address,row.Extra
                        }.Select(Escape)));
                });
                status.Text="تم تصدير "+rows.Length+" سجلا بدون كلمات مرور إلى "+path;
            }
            catch(Exception ex){status.Text="فشل التصدير: "+ex.Message;}
            finally
            {
                busy=false;
                load.IsEnabled=true;
                export.IsEnabled=loadedSection==Selected();
            }
        };
        Unloaded+=(_,_)=>pending?.Cancel();
    }
}
