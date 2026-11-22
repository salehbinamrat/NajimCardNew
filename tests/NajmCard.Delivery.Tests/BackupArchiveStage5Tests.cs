using System.IO.Compression;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;
namespace NajmCard.Delivery.Tests;

public sealed class BackupArchiveStage5Tests:IDisposable
{
    private readonly string _root=Directory.CreateTempSubdirectory("najm-backup-stage5-").FullName;
    private string Folder(string name)=>Path.Combine(_root,name);
    private static Voucher Card(string name)=>new(name,"","gold","batch",DateTimeOffset.UtcNow){RouterId="router-a"};
    private static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6hCkAAAAASUVORK5CYII=");
    private VoucherCardAppearance Artwork(VoucherStore store)
    {
        var path=Path.Combine(_root,"صورة القالب.png");File.WriteAllBytes(path,Png);
        var appearance=new VoucherCardAppearance(TemplateImagePath:path);
        store.SaveCardAppearance(appearance);return appearance;
    }

    [Fact]
    public void ArchiveRestoresImagesAndAllReferencesToDifferentUnicodePathAndRetainsRollback()
    {
        using var source=new VoucherStore(Folder("source"));var style=Artwork(source);
        source.SaveBatch([Card("0001")],new VoucherBatchPrintSnapshot("hotspot","batch","gold","",new CardPrintOptions(),style){RouterId="router-a"});
        source.SavePackagePreset(new VoucherPackagePreset("router-a","hotspot","gold","",1,"",8,"0123456789",true,8,"0123456789",false,new CardPrintOptions(),style));
        var archive=source.BackupArchive();
        var targetPath=Folder("استعادة كاملة;اسم='تجربة'");
        using var target=new VoucherStore(targetPath);target.SaveBatch([Card("keep-in-rollback")]);
        var result=target.RestoreBackup(archive);
        Assert.True(File.Exists(result.RecoveryBackupPath));
        Assert.Throws<InvalidOperationException>(()=>target.GetVouchers()); // old views must restart
        using var restored=new VoucherStore(targetPath);
        Assert.Equal("0001",Assert.Single(restored.GetVouchers()).Username);
        var image=restored.LoadCardAppearance().TemplateImagePath;
        Assert.StartsWith(Path.GetFullPath(targetPath),image);
        Assert.Equal(Png,File.ReadAllBytes(image));
        Assert.Equal(image,restored.LoadBatchPrintSnapshot("hotspot","batch","router-a")!.Appearance.TemplateImagePath);
        Assert.Equal(image,restored.LoadPackagePreset("router-a","hotspot","gold")!.Appearance.TemplateImagePath);
        using var rollback=new VoucherStore(Folder("rollback"));rollback.RestoreBackup(result.RecoveryBackupPath);
        using var old=new VoucherStore(Folder("rollback"));
        Assert.Equal("keep-in-rollback",Assert.Single(old.GetVouchers()).Username);
    }

    [Fact]
    public void LegacyDatabaseWithoutImagesRestoresWithoutDroppingMoreThanTenThousandRows()
    {
        using var source=new VoucherStore(Folder("source"));
        source.SaveBatch(Enumerable.Range(0,10000).Select(i=>Card(i.ToString("D6"))).ToArray());
        source.SaveBatch([Card("10001")]);var legacy=source.Backup();
        using var target=new VoucherStore(Folder("target"));target.RestoreBackup(legacy);
        using var restored=new VoucherStore(Folder("target"));Assert.Equal(10001,restored.GetVouchersForExport().Count);
    }

    [Fact]
    public void MissingReferencedImageFailsBackupWithoutPublishingPartialArchive()
    {
        using var source=new VoucherStore(Folder("source"));Artwork(source);
        File.Delete(source.LoadCardAppearance().TemplateImagePath);
        Assert.ThrowsAny<IOException>(()=>source.BackupArchive());
        Assert.Empty(Directory.GetFiles(Path.Combine(source.DirectoryPath,"Backups"),"*.najmbackup"));
    }

    [Theory]
    [InlineData("../escape.png")][InlineData("/absolute.png")][InlineData("artwork/../../escape.png")]
    public void UnsafeArchiveEntryRefusesRestoreAndPreservesExistingData(string name)
    {
        using var source=new VoucherStore(Folder("source"));var archive=source.BackupArchive();
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))
        using(var writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write("bad");
        using var target=new VoucherStore(Folder("target"));target.SaveBatch([Card("keep")]);
        Assert.Throws<InvalidDataException>(()=>target.RestoreBackup(archive));
        Assert.Equal("keep",Assert.Single(target.GetVouchers()).Username);
        Assert.False(File.Exists(Folder("escape.png")));
    }

    [Fact]
    public void ModifiedDatabaseDigestRefusesRestoreBeforeWritingTarget()
    {
        using var source=new VoucherStore(Folder("source"));var archive=source.BackupArchive();
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))
        {
            zip.GetEntry("database.db")!.Delete();
            using var writer=new StreamWriter(zip.CreateEntry("database.db").Open());writer.Write("corrupt");
        }
        using var target=new VoucherStore(Folder("target"));target.SaveBatch([Card("keep")]);
        Assert.Throws<InvalidDataException>(()=>target.RestoreBackup(archive));
        Assert.Equal("keep",Assert.Single(target.GetVouchers()).Username);
    }

    [Fact]
    public void AnotherStoreInstancePreventsRestore()
    {
        using var source=new VoucherStore(Folder("source"));var archive=source.BackupArchive();
        using var target=new VoucherStore(Folder("target"));
        using var other=new VoucherStore(Folder("target"));
        Assert.Throws<IOException>(()=>target.RestoreBackup(archive));
        target.SaveBatch([Card("still-usable")]);
    }

    [Fact]
    public void ActiveSendSessionBlocksRestoreAndKeepsItsDurableRecord()
    {
        using var source=new VoucherStore(Folder("source"));var archive=source.BackupArchive();
        using var target=new VoucherStore(Folder("target"));var card=Card("in-flight");target.SaveBatch([card]);
        using var sender=target.OpenSendSession();var lease=sender.BeginSend(card);
        Assert.Throws<IOException>(()=>target.RestoreBackup(archive));
        Assert.Equal("SENDING",Assert.Single(target.GetVouchers()).DeliveryStatus);
        sender.FinishSend(lease,"UNKNOWN","lost reply");
        Assert.Equal("UNKNOWN",Assert.Single(target.GetVouchers()).DeliveryStatus);
    }

    [Fact]
    public void LegacyPreRouterSchemaMigratesOnlyStagedCopyAndPreservesOriginal()
    {
        var legacy=Folder("old.db");
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=legacy,Pooling=false}.ToString()))
        {
            db.Open();using var cmd=db.CreateCommand();
            cmd.CommandText="CREATE TABLE vouchers(username TEXT PRIMARY KEY,password_protected TEXT,profile TEXT,batch_id TEXT,created_at TEXT,sent INTEGER); INSERT INTO vouchers VALUES('old','no-password:v1','gold','batch','2026-01-01T00:00:00Z',0)";
            cmd.ExecuteNonQuery();
        }
        var original=File.ReadAllBytes(legacy);
        using var target=new VoucherStore(Folder("target"));target.RestoreBackup(legacy);
        Assert.Equal(original,File.ReadAllBytes(legacy));
        using var reopened=new VoucherStore(Folder("target"));var card=Assert.Single(reopened.GetVouchers());
        Assert.Equal("old",card.Username);Assert.Equal("",card.RouterId);
    }

    [Fact]
    public void DuplicateArchiveNameIsRejectedWithoutAlteringCurrentDatabase()
    {
        using var source=new VoucherStore(Folder("source"));var archive=source.BackupArchive();
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Update))
        using(var writer=new StreamWriter(zip.CreateEntry("MANIFEST.JSON").Open()))writer.Write("{}");
        using var target=new VoucherStore(Folder("target"));target.SaveBatch([Card("keep")]);
        Assert.Throws<InvalidDataException>(()=>target.RestoreBackup(archive));
        Assert.Equal("keep",Assert.Single(target.GetVouchers()).Username);
    }

    public void Dispose()=>Directory.Delete(_root,true);
}
