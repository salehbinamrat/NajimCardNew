using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Real router .umb inventory and explicitly confirmed v6/v7 database backup.
/// Restoration is intentionally disabled until a router-specific recoverability
/// protocol is proven. Local SQLite/DPAPI backups are a DIFFERENT category.
/// </summary>
public sealed class UserManagerMaintenanceView : UserControl
{
    private sealed record LocalBackup(string Name,string Size,string CreatedAt,string Path);
    public UserManagerMaintenanceView(RouterGateway gateway,VoucherStore? store,Action back)
    {
        var header=UmUi.Header("النسخ والصيانة",
            "نسخ قاعدة User Manager على الراوتر ومراجعة النسخ الفعلية وصيانة آمنة.");
        var warning=UmUi.Panel(UmUi.Stack(
            UmUi.Label("تنبيه هام",16,true,UmUi.Blue),
            UmUi.Label(
                "يجب إنشاء نسخة احتياطية قبل صيانة قاعدة User Manager. "+
                "لن تُنفذ إعادة بناء أو استعادة أو حذف للقاعدة من هذه الشاشة "+
                "دون فحوص توافق واختبارات استرجاع مؤكدة.",12),
            UmUi.Label(
                "ملفات .umb على المايكروتيك منفصلة عن نسخ قاعدة SQLite المحلية "+
                "ولا يمكن استخدام أحدهما بدلا من الآخر.",12,false,UmUi.Gray)
        ),new Thickness(3),UmUi.Light);
        var consent=new CheckBox
        {
            Content=new TextBlock
            {
                Text="أوافق على إنشاء نسخة جديدة من قاعدة User Manager دون الكتابة فوق نسخة موجودة",
                TextWrapping=TextWrapping.Wrap,Foreground=UmUi.Ink,FontSize=12
            },
            Margin=new Thickness(6,8,6,8)
        };
        var diagnose=UmUi.Action("فحص قاعدة البيانات",true,132);
        var createRouter=UmUi.Action("إنشاء نسخة RouterOS .umb",true,165);
        var maintenanceInfo=UmUi.Label(
            "صيانة قاعدة User Manager بوضع التشخيص فقط؛ لا توجد عملية غير مختبرة.",
            12,false,UmUi.Gray);
        var status=UmUi.Label("لم يتم فحص قاعدة User Manager بعد.",12,false,UmUi.Gray);
        var maintenance=UmUi.Stack(
            UmUi.Label("صيانة قاعدة البيانات",16,true),
            warning,consent,diagnose,createRouter,maintenanceInfo,status);
        var list=UmUi.Table(("اسم النسخة","Name",248),
            ("تاريخ الإنشاء","CreatedAt",154),("الحجم","Size",100));
        list.MinHeight=170;list.MaxHeight=220;
        var refresh=UmUi.Action("تحديث النسخ من الراوتر",true,155);
        var fileStatus=UmUi.Label(
            "اضغط تحديث لجلب ملفات .umb الحقيقية من RouterOS Files.",
            12,false,UmUi.Gray);
        var remote=UmUi.Stack(UmUi.Label("النسخ الاحتياطية والاستعادة",16,true),
            UmUi.Row(refresh),list,fileStatus,
            UmUi.Label(
                "لتحميل ملف .umb إلى الكمبيوتر استخدم قائمة Files في Winbox. "+
                "لا يمكن تنزيل بيانات الملف عبر استعلام RouterOS API القياسي؛ "+
                "زر الاستعادة محجوز حتى اختبار التوافق على الراوتر.",12,false,UmUi.Gray));
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.45,GridUnitType.Star)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.55,GridUnitType.Star)});
        var left=UmUi.Panel(UmUi.Scroller(maintenance));
        var right=UmUi.Panel(remote);
        Grid.SetColumn(left,0);Grid.SetColumn(right,1);
        grid.Children.Add(left);grid.Children.Add(right);

        var localTable=UmUi.Table(("اسم النسخة المحلية","Name",255),
            ("تاريخ الإنشاء","CreatedAt",160),("الحجم","Size",120));
        localTable.MinHeight=95;localTable.MaxHeight=135;
        var localBackup=UmUi.Action("إنشاء نسخة محلية",true,130);
        var localRefresh=UmUi.Action("تحديث الملفات المحلية");
        var localExport=UmUi.Action("تصدير النسخة المحددة");
        var localStatus=UmUi.Label(
            "نسخ نجم كارد المحلية لا تستعيد حسابات User Manager من الراوتر.",
            12,false,UmUi.Gray);
        var local=UmUi.Panel(UmUi.Stack(
            UmUi.Label("بيانات نجم كارد المحلية (SQLite + Windows DPAPI)",15,true),
            UmUi.Row(localBackup,localRefresh,localExport),
            localTable,localStatus));
        Content=UmUi.Scroller(UmUi.Stack(header,grid,local));

        bool busy=false;
        CancellationTokenSource? pending=null;
        void SetBusy(bool value)
        {
            busy=value;
            diagnose.IsEnabled=refresh.IsEnabled=!busy;
            createRouter.IsEnabled=!busy&&consent.IsChecked==true;
            localBackup.IsEnabled=store is not null&&!busy;
            localRefresh.IsEnabled=localExport.IsEnabled=store is not null&&!busy;
        }
        consent.Checked+=(_,_)=>SetBusy(busy);
        consent.Unchecked+=(_,_)=>SetBusy(busy);

        async Task RefreshRouter()
        {
            if(busy||!IsLoaded)return;
            SetBusy(true);
            fileStatus.Text="يجري قراءة ملفات .umb من المايكروتيك...";
            using var request=new CancellationTokenSource();pending=request;
            try
            {
                var files=await gateway.ReadUmRouterBackupsAsync(request.Token);
                if(!IsLoaded||request.IsCancellationRequested)return;
                list.ItemsSource=files.OrderByDescending(x=>x.CreatedAt).ToArray();
                fileStatus.Text="عدد النسخ .umb الموجودة على الراوتر: "+
                    files.Count+" | لم يتم تنزيل أو استعادة أي ملف.";
            }
            catch(OperationCanceledException) when(request.IsCancellationRequested){}
            catch(Exception ex)
            {
                if(IsLoaded){list.ItemsSource=null;fileStatus.Text=
                    "فشلت قراءة ملفات الراوتر: "+ex.Message;}
            }
            finally{pending=null;SetBusy(false);}
        }

        refresh.Click+=async(_,_)=>await RefreshRouter();
        diagnose.Click+=async(_,_) =>
        {
            if(busy||!IsLoaded)return;
            SetBusy(true);status.Text="يجري فحص بيانات القاعدة دون إجراء أي تعديل...";
            try
            {
                var version=await gateway.ReadRouterOsGenerationAsync();
                if(!IsLoaded)return;
                if(version==RouterOsGeneration.V6)
                {
                    var state=await gateway.ReadUserManagerV6DatabaseStatusAsync();
                    status.Text=state.Diagnostic+
                        " | حجم القاعدة: "+state.DbSize+
                        " | القرص الحر: "+state.FreeSpace;
                }
                else
                {
                    var preflight=await gateway.DiagnoseUserManagerV7RadiusAsync();
                    status.Text=preflight.Message+
                        (preflight.Issues.Count==0?"":" | "+string.Join(" | ",preflight.Issues));
                }
            }
            catch(Exception ex)
            {if(IsLoaded)status.Text="تعذر إجراء الفحص: "+ex.Message;}
            finally{SetBusy(false);}
        };
        createRouter.Click+=async(_,_) =>
        {
            if(busy||consent.IsChecked!=true)return;
            if(!PageUi.Confirm(
                "إنشاء نسخة جديدة من قاعدة User Manager على الراوتر بصيغة .umb؟ "+
                "لن تُستبدل أي نسخة سابقة ولن تُستعاد بيانات."))return;
            SetBusy(true);
            status.Text="يجري حفظ قاعدة User Manager إلى ملف جديد على الراوتر...";
            try
            {
                var result=await gateway.CreateUmRouterBackupAsync();
                status.Text=result.Message+
                    (result.Uncertain?" تحقق من Files قبل أي محاولة أخرى.":"");
                if(result.Success)
                {
                    SetBusy(false);
                    await RefreshRouter();
                }
            }
            catch(Exception ex){status.Text="لم يثبت اكتمال النسخ: "+ex.Message;}
            finally{SetBusy(false);}
        };

        void RefreshLocal()
        {
            if(store is null)return;
            try
            {
                var folder=Path.Combine(store.DirectoryPath,"Backups");
                var rows=Directory.Exists(folder)
                    ?Directory.EnumerateFiles(folder,"*.db",SearchOption.TopDirectoryOnly)
                        .Select(path=>new FileInfo(path))
                        .OrderByDescending(info=>info.CreationTimeUtc)
                        .Select(info=>new LocalBackup(info.Name,
                            UmUi.Bytes(info.Length),
                            info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),info.FullName)).ToArray()
                    :[];
                localTable.ItemsSource=rows;
                localStatus.Text="عدد نسخ SQLite المحلية: "+rows.Length+
                    " | لا تتضمن قاعدة User Manager على المايكروتيك.";
            }
            catch(Exception ex){localStatus.Text="فشل عرض النسخ المحلية: "+ex.Message;}
        }
        localBackup.Click+=(_,_) =>
        {
            if(busy||store is null)return;
            if(!PageUi.Confirm(
                "إنشاء نسخة احتياطية محلية لبيانات نجم كارد فقط، "+
                "وليس لقاعدة User Manager على الراوتر؟"))return;
            try
            {
                var file=store.Backup();
                localStatus.Text="تم إنشاء النسخة المحلية: "+file;
                RefreshLocal();
            }
            catch(Exception ex){localStatus.Text="فشل النسخ المحلي: "+ex.Message;}
        };
        localRefresh.Click+=(_,_)=>RefreshLocal();
        localExport.Click+=(_,_) =>
        {
            if(localTable.SelectedItem is not LocalBackup selected)return;
            var dialog=new SaveFileDialog {Filter="SQLite (*.db)|*.db",
                FileName=selected.Name};
            if(dialog.ShowDialog()!=true)return;
            try
            {
                if(Path.GetFullPath(dialog.FileName).Equals(selected.Path,
                    StringComparison.OrdinalIgnoreCase))
                    throw new IOException("اختر مسارا مختلفا عن ملف النسخة الأصلية.");
                File.Copy(selected.Path,dialog.FileName,overwrite:false);
                localStatus.Text="تم نسخ ملف SQLite؛ تظل كلمات المرور "+
                    "مرتبطة بحماية Windows DPAPI للمستخدم الحالي.";
            }
            catch(Exception ex){localStatus.Text="تعذر تصدير النسخة: "+ex.Message;}
        };
        Unloaded+=(_,_)=>pending?.Cancel();
        SetBusy(false);
        RefreshLocal();
    }
}
