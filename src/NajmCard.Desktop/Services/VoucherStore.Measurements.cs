using System.IO;
using System.Text.Json;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

public sealed partial class VoucherStore
{
    public string SavePipelineMeasurement(VoucherPipelineSnapshot measurement)
    {
        var folder=Path.Combine(DirectoryPath,"Performance");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json");
        var staged=path+".tmp";
        try
        {
            using(var file=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                JsonSerializer.Serialize(file,new{RecordedAtUtc=DateTimeOffset.UtcNow,Measurement=measurement});
                file.Flush(true);
            }
            File.Move(staged,path);return path;
        }
        finally{if(File.Exists(staged))File.Delete(staged);}
    }
}
