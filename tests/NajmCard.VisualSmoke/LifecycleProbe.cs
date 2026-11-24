using System.IO;
using System.Text.Json;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Services.Licensing;

namespace NajmCard.VisualSmoke;

internal static partial class Program
{
    private const string FixturePassword="stage6-fixture-only";
    private const string FixtureLicense="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static int RunLifecycleProbe(string[] args)
    {
        // This probe is in a test executable, never in the shipped application.
        // The package script must first reserve an empty disposable CI profile.
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||
           args.Length!=3||!File.Exists(args[1]))
            throw new InvalidOperationException("Lifecycle fixtures require the disposable CI sentinel.");
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NajmCardNew");
        var sentinel=Path.GetFullPath(args[1]);
        if(!string.Equals(Path.GetDirectoryName(sentinel),root,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected CI profile location.");
        var license=new ProtectedPaidCodeStore();
        var connections=new SavedRouterConnectionStore();
        using var store=new VoucherStore();
        var image=Path.Combine(store.DirectoryPath,"fixture-image.png");
        if(args[0]=="--lifecycle-seed")
        {
            if(store.GetVouchers().Count!=0||connections.Load().Count!=0||license.Load().Length!=0)
                throw new InvalidOperationException("Refusing to replace existing CI profile data.");
            store.SaveBatch([new Voucher("stage6-keep",FixturePassword,"fixture-profile","stage6-batch",
                new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero)){RouterId="stage6-isolated-router"}]);
            connections.Save(new SavedRouterConnection("192.0.2.10",8728,"fixture",false,"local",FixturePassword),true);
            license.Save(FixtureLicense);
            File.WriteAllBytes(image,Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            store.SaveCardAppearance(new VoucherCardAppearance(NetworkTitle:"Stage 6 preserved settings",TemplateImagePath:image));
            File.Copy(store.BackupArchive(),Path.Combine(root,"before-install.najmbackup"));
        }
        else if(args[0]!="--lifecycle-verify")throw new ArgumentException("Unknown probe.");
        var cards=store.GetVouchers();
        if(cards.Count!=1||cards[0].Username!="stage6-keep"||cards[0].Password!=FixturePassword||
           cards[0].RouterId!="stage6-isolated-router"||cards[0].DeliveryStatus!="CREATED"||
           connections.Load().Single().Password!=FixturePassword||license.Load()!=FixtureLicense||
           store.LoadCardAppearance().NetworkTitle!="Stage 6 preserved settings"||
           !File.Exists(store.LoadCardAppearance().TemplateImagePath)||!File.Exists(Path.Combine(root,"before-install.najmbackup")))
            throw new InvalidDataException("Install, update or removal altered the preserved fixture data.");
        File.WriteAllText(args[2],JsonSerializer.Serialize(new
        {
            architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            cards=cards.Count,passwordDecrypted=true,connectionRetained=true,licenseRetained=true,
            settingsRetained=true,imageRetained=true,backupRetained=true,routerUsed=false
        },new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
