using System.Globalization;
using System.Text;

namespace NajmCard.Core.Services;

/// <summary>Shared spreadsheet-safe text encoding and durable CSV publication.</summary>
public static class SpreadsheetCsv
{
    public static string Quote(string? raw,bool singleLine=false)
    {
        var value=raw??"";
        var first=0;
        while(first<value.Length && (char.IsWhiteSpace(value[first]) ||
            char.IsControl(value[first]) || char.GetUnicodeCategory(value[first])==UnicodeCategory.Format))first++;
        // Quoting CSV delimiters alone does not prevent Excel formula execution.
        // Inspect before normalizing line endings, including Unicode whitespace
        // and format characters preceding an attacker-controlled formula.
        if(first<value.Length&&"=+-@".Contains(value[first]))value="'"+value;
        if(singleLine)value=value.Replace("\r"," ",StringComparison.Ordinal).Replace("\n"," ",StringComparison.Ordinal);
        return "\""+value.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
    }

    public static string Export(string path,IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);ArgumentNullException.ThrowIfNull(rows);
        if(headers.Count==0||!string.Equals(Path.GetExtension(path),".csv",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("يلزم ملف CSV وعناوين أعمدة صالحة.");
        var full=Path.GetFullPath(path);var parent=Path.GetDirectoryName(full)!;Directory.CreateDirectory(parent);
        var staged=Path.Combine(parent,".najm-report-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.SequentialScan))
            {
                using(var writer=new StreamWriter(stream,new UTF8Encoding(true),8192,leaveOpen:true))
                {
                    writer.WriteLine(string.Join(",",headers.Select(x=>Quote(x))));
                    foreach(var row in rows)
                    {
                        if(row is null||row.Count!=headers.Count)throw new InvalidDataException("عدد حقول التقرير لا يطابق العناوين.");
                        writer.WriteLine(string.Join(",",row.Select(x=>Quote(x))));
                    }
                    writer.Flush();
                }
                stream.Flush(true);
            }
            if(File.Exists(full))File.Replace(staged,full,null);else File.Move(staged,full);
            return full;
        }
        finally{if(File.Exists(staged))File.Delete(staged);}
    }
}
