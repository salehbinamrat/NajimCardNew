using System.Windows.Controls.Primitives;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public class ReportsView : UserControl
{
    public ReportsView(VoucherStore store)
    {
        var stack = PageUi.Column();
        var status = PageUi.Status();
        var exportCsv = PageUi.Button("تصدير CSV دون كلمات المرور");
        var exportCsvSecrets = PageUi.Button("تصدير CSV مع كلمات المرور", false);
        var exportExcel = PageUi.Button("تصدير Excel دون كلمات المرور");
        var exportExcelSecrets = PageUi.Button("تصدير Excel مع كلمات المرور", false);
        stack.Children.Add(PageUi.Hint(
            "ملفات Excel بصيغة XLSX وبأعمدة حضرموت الأصلية. الحقول غير المتوفرة في قاعدة نجم كارد الحالية تُترك فارغة. " +
            "ورقة Delivery تتضمن بيانات الإرسال المحفوظة فعلياً. لا يُصدّر أي رمز سري إلا بعد تأكيد مستقل."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(exportCsv); buttons.Children.Add(exportCsvSecrets);
        stack.Children.Add(buttons);
        var excelButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        excelButtons.Children.Add(exportExcel); excelButtons.Children.Add(exportExcelSecrets);
        stack.Children.Add(excelButtons);
        var legacyButtons=new StackPanel
        {
            Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)
        };
        var exportTxt=PageUi.Button("تصدير بيانات الكروت TXT",false);
        var exportRsc=PageUi.Button("تصدير سكربت MikroTik RSC",false);
        legacyButtons.Children.Add(exportTxt);
        legacyButtons.Children.Add(exportRsc);
        stack.Children.Add(legacyButtons);
        stack.Children.Add(PageUi.Hint(
            "TXT يحتوي أسماء المستخدمين وكلمات المرور، وRSC يحتوي أوامر إنشاء الكروت " +
            "لـ RouterOS 6، مع فحص التكرار. كلا الملفين نص مكشوف ولا يُرفع أو يُنفذ تلقائيا."));
        stack.Children.Add(status);
        Content = PageUi.Wrap("التقارير والتصدير",
            "تصدير CSV وExcel إلى ملفات محلية دون تعديل بيانات الشبكة.", PageUi.Surface(stack, "#377E98"));

        void SetExportBusy(bool busy)
        {
            foreach(var button in new[]{exportCsv,exportCsvSecrets,exportExcel,exportExcelSecrets,exportTxt,exportRsc})
                button.IsEnabled=!busy;
            if(busy)status.Text="جارٍ تصدير جميع السجلات؛ يرجى الانتظار...";
        }

        async Task ExportCsv(bool withPasswords)
        {
            if (withPasswords && !PageUi.Confirm(
                    "سيحتوي ملف CSV على كلمات مرور الكروت بنص مكشوف. تأكيد؟"))
                return;
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = "NajmCard-Cards-" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
                };
                if (dialog.ShowDialog() == true)
                {
                    SetExportBusy(true);
                    await Task.Run(()=>store.ExportCsv(dialog.FileName, withPasswords));
                    status.Text = "تم حفظ CSV: " + dialog.FileName;
                }
            }
            catch (Exception ex) { status.Text = "فشل تصدير CSV: " + ex.Message; }
            finally {SetExportBusy(false);}
        }

        async Task ExportExcel(bool withPasswords)
        {
            if (withPasswords && !PageUi.Confirm(
                    "سيحتوي ملف Excel على كلمات مرور جميع الكروت بنص قابل للقراءة. هل تؤكد؟"))
                return;
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel (*.xlsx)|*.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    FileName = "NajmCard-Cards-" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx"
                };
                if (dialog.ShowDialog() != true) return;
                SetExportBusy(true);
                var count=await Task.Run(()=>
                {
                    var rows=store.GetVouchersForExport(withPasswords);
                    VoucherExcelExporter.Export(rows,dialog.FileName,"NajmCard",withPasswords);
                    return rows.Count;
                });
                status.Text = "تم حفظ Excel: " + dialog.FileName + " | عدد الكروت: " + count;
            }
            catch (Exception ex) { status.Text = "فشل تصدير Excel: " + ex.Message; }
            finally {SetExportBusy(false);}
        }

        exportCsv.Click += async(_, _) => await ExportCsv(false);
        exportCsvSecrets.Click += async(_, _) => await ExportCsv(true);
        exportExcel.Click += async(_, _) => await ExportExcel(false);
        exportExcelSecrets.Click += async(_, _) => await ExportExcel(true);

        async Task ExportLegacy(bool asScript)
        {
            string kind=asScript?"RSC":"TXT";
            if(!PageUi.Confirm(
                asScript
                    ? "ملف RSC سيحتوي كلمات مرور الكروت وأوامر إضافتها إلى MikroTik. " +
                      "لا تستورده قبل التأكد من الراوتر والباقة. هل تريد حفظه محليا؟"
                    : "ملف TXT سيحتوي أسماء الكروت وكلمات مرورها بنص واضح غير مشفر. هل تؤكد؟"))
                return;
            try
            {
                var dialog=new SaveFileDialog
                {
                    Filter=asScript?"RouterOS Script (*.rsc)|*.rsc":"Text (*.txt)|*.txt",
                    DefaultExt=asScript?".rsc":".txt",
                    AddExtension=true,
                    FileName="NajmCard-Cards-"+DateTime.Now.ToString("yyyyMMdd")+
                        (asScript?".rsc":".txt")
                };
                if(dialog.ShowDialog()!=true)return;
                SetExportBusy(true);
                var count=await Task.Run(()=>
                {
                    var rows=store.GetVouchersForExport(true);
                    if(asScript)VoucherOfflineExporter.ExportRouterOs6Script(rows,dialog.FileName);
                    else VoucherOfflineExporter.ExportCredentialsText(rows,dialog.FileName);
                    return rows.Count;
                });
                status.Text="حُفظ "+kind+" محليا: "+dialog.FileName+
                    " | "+count+" كرت | لم يُرسل أي أمر إلى الراوتر.";
            }
            catch(Exception ex) { status.Text="فشل تصدير "+kind+": "+ex.Message; }
            finally {SetExportBusy(false);}
        }

        exportTxt.Click += async(_, _) => await ExportLegacy(false);
        exportRsc.Click += async(_, _) => await ExportLegacy(true);
    }
}
