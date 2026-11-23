using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Password-free UTF-8 CSV with shared formula protection and atomic publication.</summary>
internal static class UmCsv
{
    internal static void Export(string path,IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)=>SpreadsheetCsv.Export(path,headers,rows);
}
