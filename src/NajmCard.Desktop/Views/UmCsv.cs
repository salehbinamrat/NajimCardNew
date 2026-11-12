using System.IO;
using System.Text;

namespace NajmCard.Desktop.Views;

/// <summary>Password-free UTF-8 CSV with spreadsheet formula protection.</summary>
internal static class UmCsv
{
    internal static void Export(string path,IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        static string Quote(string value)
        {
            value??="";
            // Text from RouterOS must never execute as spreadsheet formula.
            var check=value.TrimStart(' ','\t','\r','\n');
            if(check.Length>0 && "=+-@".Contains(check[0]))value="'"+value;
            return "\"" + value.Replace("\"","\"\"") + "\"";
        }
        using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);
        using var writer=new StreamWriter(stream,new UTF8Encoding(true));
        writer.WriteLine(string.Join(",",headers.Select(Quote)));
        foreach(var row in rows)
            writer.WriteLine(string.Join(",",row.Select(Quote)));
    }
}
