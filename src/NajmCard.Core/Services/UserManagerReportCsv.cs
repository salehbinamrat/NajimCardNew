using System.Globalization;
using NajmCard.Core.Models;
namespace NajmCard.Core.Services;

/// <summary>Exports the already-selected display snapshot, never a new global query.</summary>
public static class UserManagerReportCsv
{
    public static IReadOnlyList<string> Headers {get;}=["المستخدم","وقت الاتصال","مدة الجلسة","IP","MAC",
        "التنزيل","الرفع","الحالة","نوع السجل","الباقة","من","إلى","إجمالي الجلسة بالبايت",
        "استهلاك الفترة بالبايت","الإسناد","مبلغ الدفع","العملة","مصدر البيانات","معرف السجل","وقت الانتهاء"];
    public static IEnumerable<IReadOnlyList<string>> Rows(IReadOnlyList<UmAccountRow> users,
        UserManagerReportFilters.TrafficSelection traffic,UserManagerReportFilters.ApprovedPayments? payments,
        DateTime? from,DateTime? to,string? profile,string source)
    {
        string[] Row(string type)
        {
            var row=Enumerable.Repeat("",Headers.Count).ToArray();
            row[8]=type;row[9]=profile??"كل الباقات";
            row[10]=from?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"";
            row[11]=to?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"";row[17]=source;
            return row;
        }
        static string Bytes(long? value)=>value?.ToString(CultureInfo.InvariantCulture)??"غير معروف";
        var summary=Row("summary");summary[7]=traffic.IsComplete&&traffic.KnownCount>0?"مكتمل":"جزئي / غير متاح";
        summary[13]=traffic.KnownCount>0?Bytes(traffic.KnownBytes):"غير معروف";
        summary[14]=$"جلسات بلا تاريخ صالح للفترة: {traffic.UncertainDateCount}؛ توزيع يومي غير معروف: {traffic.UnallocatedDailyCount}"+
            (traffic.HasUnknownProfile?"؛ باقة الجلسات التاريخية غير معروفة":"")+
            (payments is null?"؛ المدفوعات غير متاحة":!payments.IsComplete?"؛ بيانات المدفوعات ناقصة":"");
        yield return summary;
        foreach(var user in users)
        {
            var row=Row("account");row[0]=user.Name;row[7]=user.Status;row[18]=user.Id;row[9]=user.Profile;
            row[14]="حساب حالي؛ ليس سجلاً تاريخياً للفترة";yield return row;
        }
        foreach(var item in traffic.Rows)
        {
            var x=item.Session;var row=Row("session");
            row[19]=x.Ended;row[18]=x.Id;row[0]=x.Name;row[1]=x.Started;row[2]=x.Uptime;row[3]=x.Address;row[4]=x.Mac;
            row[5]=Bytes(x.DownloadBytes);row[6]=Bytes(x.UploadBytes);row[7]=x.Status;
            row[9]="غير معروف تاريخياً";row[12]=Bytes(x.TotalBytes);row[13]=Bytes(item.PeriodBytes);
            row[14]=item.Attribution;yield return row;
        }
        foreach(var payment in payments?.Rows??[])
        {
            var row=Row("payment");row[18]=payment.Id;row[1]=payment.Date?.ToString("O",CultureInfo.InvariantCulture)??"غير معروف";
            row[9]=payment.Profile;row[15]=payment.Amount.ToString(CultureInfo.InvariantCulture);row[16]=payment.Currency;
            yield return row;
        }
    }
}
