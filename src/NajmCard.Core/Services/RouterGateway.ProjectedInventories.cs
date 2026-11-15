namespace NajmCard.Core.Services;

public sealed partial class RouterGateway
{
    // Used only inside the heavy-read gate; remember compatibility per menu.
    private readonly HashSet<string> _fullInventoryMenus=new(StringComparer.Ordinal);
    private async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadProjectedInventoryAsync(
        string path,string fields,Func<IReadOnlyDictionary<string,string>,bool> valid,CancellationToken ct)
    {
        bool full=_fullInventoryMenus.Contains(path);
        IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
        try{rows=await ReadInventoryCommandAsync(path,full?null:new Dictionary<string,string>{{".proplist",fields}},ct);}
        catch(RouterOsTrapException ex) when(!full&&UnsupportedProjection(ex))
        {full=true;rows=await ReadInventoryCommandAsync(path,cancellationToken:ct);}
        if(!full&&rows.Any(row=>!valid(row)))
        {full=true;rows=await ReadInventoryCommandAsync(path,cancellationToken:ct);}
        if(rows.Any(row=>!valid(row)))throw new InvalidDataException(
            "بيانات الحسابات أو الجلسات ناقصة؛ لم تعتمد قيما صفرية أو حالات اتصال غير مؤكدة.");
        if(full)_fullInventoryMenus.Add(path);
        return rows;
    }
    private static bool HasRecordIdentity(IReadOnlyDictionary<string,string> row,string name)=>
        !string.IsNullOrWhiteSpace(Get(row,".id"))&&!string.IsNullOrWhiteSpace(Get(row,name));
    private static bool HasDisabledState(IReadOnlyDictionary<string,string> row)=>
        Get(row,"disabled") is "no" or "yes" or "true" or "false" or "0" or "1";
}
