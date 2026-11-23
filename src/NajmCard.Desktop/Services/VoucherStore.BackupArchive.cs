using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
namespace NajmCard.Desktop.Services;

public sealed record VoucherRestoreResult(string RecoveryBackupPath,long Cards,int Images);

public sealed partial class VoucherStore
{
    // Explicit resource limits, never truncation. All file copies are streamed.
    private const long MaxBackupDatabase=2L*1024*1024*1024,MaxBackupImages=1024L*1024*1024;
    private const long MaxBackupImage=15L*1024*1024,MaxBackupManifest=8L*1024*1024;
    private const int MaxBackupAssets=4096;
    private sealed record BackupImage(string SourcePath,string EntryName,string Sha256,long Length);
    private sealed record BackupManifest(string Format,int Version,string DatabaseSha256,List<BackupImage> Artwork);
    private sealed record ArtworkReference(string Table,string Column,long RowId,JsonObject Json,JsonObject Appearance,string Path);
    private static readonly Regex AssetEntry=new(@"^artwork/[a-f0-9]{64}\.(png|jpg|jpeg|bmp|gif|tif|tiff|webp|ico|img)\z",RegexOptions.CultureInvariant);

    /// <summary>Full local backup; the legacy Backup() SQLite format remains supported.</summary>
    public string BackupArchive()
    {
        using var db=Open();
        return CreateBackupArchive(db,NewBackupPath(".najmbackup"));
    }
    private string NewBackupPath(string extension)
    {
        var folder=Path.Combine(DirectoryPath,"Backups");Directory.CreateDirectory(folder);
        return Path.Combine(folder,"NajmCardNew-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+
            "-"+Guid.NewGuid().ToString("N")+extension);
    }
    private static SqliteConnection SnapshotConnection(string path,SqliteOpenMode mode=SqliteOpenMode.ReadWriteCreate)
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=mode,Pooling=false}.ToString());
        try
        {
            db.Open();using var cmd=db.CreateCommand();
            cmd.CommandText="PRAGMA trusted_schema=OFF; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";cmd.ExecuteNonQuery();
            return db;
        }
        catch{db.Dispose();throw;}
    }
    private static void MakeSnapshot(SqliteConnection original,string path)
    {
        using var snapshot=SnapshotConnection(path);original.BackupDatabase(snapshot);
        using var cmd=snapshot.CreateCommand();cmd.CommandText="PRAGMA journal_mode=DELETE;";cmd.ExecuteNonQuery();
    }
    private static string Hash(string path)
    {
        using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    private static void DurableFile(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);file.Flush(true);
    }
    private static void ValidateSnapshot(SqliteConnection db)
    {
        using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA quick_check";
        if(!string.Equals(cmd.ExecuteScalar() as string,"ok",StringComparison.Ordinal))
            throw new InvalidDataException("قاعدة النسخة الاحتياطية تالفة.");
        // Backup is data, not executable schema. These are not created by NajmCard.
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view') OR upper(sql) LIKE '%CREATE VIRTUAL TABLE%'";
        if(Convert.ToInt64(cmd.ExecuteScalar())!=0)throw new InvalidDataException("بنية النسخة الاحتياطية تحتوي عناصر غير مدعومة.");
        cmd.CommandText="SELECT username,password_protected,profile,batch_id,created_at,sent FROM vouchers LIMIT 0";
        using var reader=cmd.ExecuteReader(); // identify a supported application database, not an arbitrary SQLite file
    }
    private static List<ArtworkReference> ArtworkReferences(SqliteConnection db)
    {
        var result=new List<ArtworkReference>();
        foreach(var (table,column,filter,nested,limit) in new[]{
            ("app_preferences","value"," WHERE key='voucher_card_appearance'",false,4096),
            ("card_package_presets","json","",true,12000),
            ("card_batch_print_snapshots","json","",true,16000)})
        {
            using var exists=db.CreateCommand();exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            exists.Parameters.AddWithValue("$table",table);if(Convert.ToInt64(exists.ExecuteScalar())==0)continue;
            using var cmd=db.CreateCommand();cmd.CommandText=$"SELECT rowid,{column} FROM {table}{filter}";
            using var reader=cmd.ExecuteReader();
            while(reader.Read())
            {
                var text=reader.GetString(1);
                if(text.Length>limit)throw new InvalidDataException("بيانات قالب في النسخة تتجاوز الحد المسموح.");
                var json=JsonNode.Parse(text) as JsonObject??throw new InvalidDataException("بيانات قالب غير صالحة.");
                var appearance=nested?json["Appearance"] as JsonObject:json;
                if(appearance is null)throw new InvalidDataException("مرجع مظهر القالب مفقود.");
                var path=appearance["TemplateImagePath"]?.GetValue<string>()??"";
                if(path.Length!=0)result.Add(new ArtworkReference(table,column,reader.GetInt64(0),json,appearance,path));
            }
        }
        return result;
    }
    private string CreateBackupArchive(SqliteConnection original,string output)
    {
        var stage=Path.Combine(DirectoryPath,".backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        var temporary=output+".tmp";
        try
        {
            var database=Path.Combine(stage,"database.db");MakeSnapshot(original,database);
            if(new FileInfo(database).Length>MaxBackupDatabase)throw new InvalidDataException("حد النسخة المحلية 2 GiB لقاعدة البيانات؛ لم يتم إنشاء نسخة جزئية.");
            List<ArtworkReference> references;
            using(var snapshot=SnapshotConnection(database,SqliteOpenMode.ReadOnly))
            {ValidateSnapshot(snapshot);references=ArtworkReferences(snapshot);}
            var assets=new List<BackupImage>();var copied=new HashSet<string>(StringComparer.Ordinal);long total=0;
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(database,"database.db",CompressionLevel.Fastest);
                foreach(var reference in references.DistinctBy(x=>x.Path,StringComparer.OrdinalIgnoreCase))
                {
                    if(assets.Count>=MaxBackupAssets)throw new InvalidDataException("تجاوزت صور النسخة الحد المعلن: 4096 مرجع صورة.");
                    var info=new FileInfo(reference.Path);
                    if(!info.Exists)throw new FileNotFoundException("صورة قالب مفقودة؛ لم تُنشر نسخة احتياطية ناقصة.",reference.Path);
                    if(info.Length<=0||info.Length>MaxBackupImage)throw new InvalidDataException("صورة النسخة فارغة أو تتجاوز 15 MiB.");
                    var file=Path.Combine(stage,Guid.NewGuid().ToString("N"));
                    using(var input=File.OpenRead(reference.Path))CopyBounded(input,file,MaxBackupImage);
                    var hash=Hash(file);var ext=Path.GetExtension(reference.Path).ToLowerInvariant();
                    var entry="artwork/"+hash+ext;
                    if(!AssetEntry.IsMatch(entry))throw new InvalidDataException("امتداد صورة النسخة غير مدعوم.");
                    if(string.Equals(Path.GetFullPath(Path.GetDirectoryName(reference.Path)!),Path.Combine(DirectoryPath,"CardArtwork"),StringComparison.OrdinalIgnoreCase)&&
                        !Path.GetFileNameWithoutExtension(reference.Path).Equals(hash,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("صورة محفوظة لا تطابق بصمتها؛ لم تُنشر نسخة ناقصة.");
                    var length=new FileInfo(file).Length;
                    if(copied.Add(entry))
                    {
                        total=checked(total+length);if(total>MaxBackupImages)throw new InvalidDataException("تجاوزت صور النسخة 1 GiB؛ لم تُنشر نسخة جزئية.");
                        zip.CreateEntryFromFile(file,entry,CompressionLevel.Fastest);
                    }
                    assets.Add(new BackupImage(reference.Path,entry,hash,length));File.Delete(file);
                }
                var manifest=new BackupManifest("NajmCardBackup",1,Hash(database),assets);
                var bytes=JsonSerializer.SerializeToUtf8Bytes(manifest);
                if(bytes.LongLength>MaxBackupManifest)throw new InvalidDataException("فهرس النسخة أكبر من 8 MiB.");
                using var entryStream=zip.CreateEntry("manifest.json").Open();entryStream.Write(bytes);
            }
            DurableFile(temporary);File.Move(temporary,output);return output;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);Directory.Delete(stage,true);}
    }
    private static void CopyBounded(Stream input,string output,long maximum)
    {
        using var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        var buffer=new byte[81920];long total=0;int read;
        while((read=input.Read(buffer,0,buffer.Length))!=0)
        {total=checked(total+read);if(total>maximum)throw new InvalidDataException("ملف النسخة يتجاوز الحد الآمن المعلن.");file.Write(buffer,0,read);}
        file.Flush(true);
    }
    private static BackupManifest UnpackArchive(string path,string stage)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>MaxBackupAssets+2)throw new InvalidDataException("عدد ملفات النسخة يتجاوز الحد المعلن.");
        var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);long images=0;
        foreach(var entry in zip.Entries)
        {
            if(!entries.TryAdd(entry.FullName,entry)||((entry.ExternalAttributes>>16)&0xF000)==0xA000||
                (entry.FullName!="database.db"&&entry.FullName!="manifest.json"&&!AssetEntry.IsMatch(entry.FullName)))
                throw new InvalidDataException("مسار ملف في النسخة غير آمن أو مكرر.");
            var maximum=entry.FullName=="database.db"?MaxBackupDatabase:entry.FullName=="manifest.json"?MaxBackupManifest:MaxBackupImage;
            if(entry.Length<=0||entry.Length>maximum)throw new InvalidDataException("حجم ملف في النسخة غير صالح أو يتجاوز الحد المعلن.");
            if(AssetEntry.IsMatch(entry.FullName))images=checked(images+entry.Length);
        }
        if(images>MaxBackupImages||!entries.ContainsKey("database.db")||!entries.ContainsKey("manifest.json"))
            throw new InvalidDataException("النسخة ناقصة أو يتجاوز حجم صورها 1 GiB.");
        BackupManifest manifest;
        using(var stream=entries["manifest.json"].Open())
        {
            var file=Path.Combine(stage,"manifest.json");CopyBounded(stream,file,MaxBackupManifest);
            manifest=JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(file))??throw new InvalidDataException("فهرس النسخة فارغ.");
        }
        if(manifest.Format!="NajmCardBackup"||manifest.Version!=1||manifest.Artwork is null||manifest.Artwork.Count>MaxBackupAssets)
            throw new InvalidDataException("صيغة النسخة أو إصدارها غير مدعوم.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var expected=new HashSet<string>(StringComparer.Ordinal){"manifest.json","database.db"};
        foreach(var asset in manifest.Artwork)
        {
            if(asset is null||string.IsNullOrWhiteSpace(asset.SourcePath)||!paths.Add(asset.SourcePath)||
                asset.EntryName is null||!AssetEntry.IsMatch(asset.EntryName)||!entries.TryGetValue(asset.EntryName,out var entry)||
                entry.Length!=asset.Length||Path.GetFileNameWithoutExtension(asset.EntryName)!=asset.Sha256)
                throw new InvalidDataException("مرجع صورة النسخة غير صالح أو مكرر.");
            expected.Add(asset.EntryName);
        }
        if(entries.Keys.Any(x=>!expected.Contains(x)))throw new InvalidDataException("تحتوي النسخة ملفات غير مسجلة في الفهرس.");
        foreach(var name in expected.Where(x=>x!="manifest.json"))
        {
            // Names have been allowlisted; flatten images to a private staging directory.
            var output=Path.Combine(stage,Path.GetFileName(name));
            using(var stream=entries[name].Open())CopyBounded(stream,output,name=="database.db"?MaxBackupDatabase:MaxBackupImage);
            var expectedHash=name=="database.db"?manifest.DatabaseSha256:Path.GetFileNameWithoutExtension(name);
            if(!string.Equals(Hash(output),expectedHash,StringComparison.Ordinal))throw new InvalidDataException("بصمة ملف النسخة لا تطابق محتواه.");
        }
        return manifest;
    }

    /// <summary>
    /// Validate in isolation, preserve a full rollback archive, then use SQLite's
    /// atomic backup transaction. No live file replacement or deletion of artwork.
    /// Every old store/view is invalidated after success; restart before delivery.
    /// </summary>
    public VoucherRestoreResult RestoreBackup(string path)
    {
        var stage=Path.Combine(DirectoryPath,".restore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            var database=Path.Combine(stage,"database.db");BackupManifest? manifest=null;
            if(Path.GetExtension(path).Equals(".najmbackup",StringComparison.OrdinalIgnoreCase))manifest=UnpackArchive(path,stage);
            else if(Path.GetExtension(path).Equals(".db",StringComparison.OrdinalIgnoreCase))
            {
                if(Path.GetFullPath(path).Equals(_filename,StringComparison.OrdinalIgnoreCase)||File.Exists(path+"-wal"))
                    throw new InvalidDataException("اختر نسخة SQLite مكتملة، وليس قاعدة بيانات قيد الاستخدام.");
                using var stream=File.OpenRead(path);CopyBounded(stream,database,MaxBackupDatabase);
            }
            else throw new InvalidDataException("اختر نسخة .najmbackup أو نسخة SQLite قديمة .db.");
            using var incoming=SnapshotConnection(database,SqliteOpenMode.ReadWrite);ValidateSnapshot(incoming);
            EnsureSchema(incoming); // migrations affect only the isolated copy, never the original backup
            var references=ArtworkReferences(incoming);
            var byPath=manifest?.Artwork.ToDictionary(x=>x.SourcePath,StringComparer.OrdinalIgnoreCase);
            var artworkFolder=Path.Combine(DirectoryPath,"CardArtwork");
            Directory.CreateDirectory(artworkFolder);
            if((File.GetAttributes(artworkFolder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("مجلد صور الاستعادة لا يمكن أن يكون رابطاً.");
            var publish=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var reference in references)
            {
                string source;
                if(byPath is not null)
                {
                    if(!byPath.TryGetValue(reference.Path,out var asset))throw new InvalidDataException("صورة القالب غير مدرجة في النسخة؛ لم تُستعد بيانات ناقصة.");
                    source=Path.Combine(stage,Path.GetFileName(asset.EntryName));
                }
                else
                {
                    // Legacy .db has no images. Only trust an existing managed,
                    // content-addressed file, never follow arbitrary imported paths.
                    source=Path.GetFullPath(reference.Path);
                    if(!string.Equals(Path.GetDirectoryName(source),artworkFolder,StringComparison.OrdinalIgnoreCase)||!File.Exists(source)||
                        !string.Equals(Hash(source),Path.GetFileNameWithoutExtension(source),StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("نسخة SQLite القديمة لا تتضمن صورة القالب. استخدم النسخة الكاملة أو أعد الصورة المفقودة قبل الاستعادة.");
                }
                var destination=Path.Combine(artworkFolder,Path.GetFileName(source));
                if(File.Exists(destination)&&((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0||Hash(destination)!=Hash(source)))
                    throw new InvalidDataException("صورة موجودة في وجهة الاستعادة لا تطابق النسخة؛ حُفظت البيانات الحالية.");
                publish[destination]=source;reference.Appearance["TemplateImagePath"]=destination;
            }
            using(var tx=incoming.BeginTransaction())
            {
                foreach(var reference in references)
                {
                    var json=reference.Json.ToJsonString();
                    int limit=reference.Table=="app_preferences"?4096:reference.Table=="card_package_presets"?12000:16000;
                    if(json.Length>limit)throw new InvalidDataException("مسار الاستعادة يجعل مرجع القالب أكبر من الحد المسموح؛ اختر مساراً أقصر.");
                    using var cmd=incoming.CreateCommand();cmd.Transaction=tx;
                    cmd.CommandText=$"UPDATE {reference.Table} SET {reference.Column}=$json WHERE rowid=$row";
                    cmd.Parameters.AddWithValue("$json",json);cmd.Parameters.AddWithValue("$row",reference.RowId);cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            using var count=incoming.CreateCommand();count.CommandText="SELECT COUNT(*) FROM vouchers";var cards=Convert.ToInt64(count.ExecuteScalar());
            var exclusive=EnterMaintenance();bool restored=false;
            try
            {
                using var current=SnapshotConnection(_filename,SqliteOpenMode.ReadWrite);
                var recovery=CreateBackupArchive(current,NewBackupPath(".najmbackup"));
                foreach(var (destination,source) in publish)
                    if(!File.Exists(destination))
                    {
                        var temporary=Path.Combine(artworkFolder,".restore-"+Guid.NewGuid().ToString("N"));
                        try{File.Copy(source,temporary);DurableFile(temporary);File.Move(temporary,destination);}
                        finally{if(File.Exists(temporary))File.Delete(temporary);}
                    }
                incoming.BackupDatabase(current);restored=true;
                return new VoucherRestoreResult(recovery,cards,publish.Count);
            }
            finally{LeaveMaintenance(exclusive,restored);}
        }
        catch(JsonException ex){throw new InvalidDataException("فهرس النسخة أو بيانات القالب تالفة.",ex);}
        finally
        {
            try{Directory.Delete(stage,true);}
            catch(IOException) { /* A committed restore remains committed. */ }
            catch(UnauthorizedAccessException) { }
        }
    }
}
