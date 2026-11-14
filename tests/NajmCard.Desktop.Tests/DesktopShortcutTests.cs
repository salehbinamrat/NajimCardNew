using System.IO;
using System.Reflection;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class DesktopShortcutTests
{
    private static string NewFolder()
    {
        string folder=Path.Combine(Path.GetTempPath(),"najm-shortcut-test-"+
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void PortableSettings_AlwaysChooseRootLauncherOverInternalAppExecutable()
    {
        var root=NewFolder();
        try
        {
            var app=Path.Combine(root,"App");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(root,"NajmCard.exe"),"launcher");
            File.WriteAllText(Path.Combine(app,"NajmCard.exe"),"internal app");
            Assert.Equal(Path.Combine(root,"NajmCard.exe"),
                DesktopShortcutService.ResolveExecutablePath(app));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void DevelopmentBuildWithoutRootLauncher_UsesActualExecutable()
    {
        var root=NewFolder();
        try
        {
            var app=Path.Combine(root,"App");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app,"NajmCard.exe"),"exe");
            Assert.Equal(Path.Combine(app,"NajmCard.exe"),
                DesktopShortcutService.ResolveExecutablePath(app));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void MissingApplication_NeverCreatesBrokenDesktopShortcut()
    {
        var root=NewFolder();
        try
        {
            Assert.Throws<FileNotFoundException>(()=>
                DesktopShortcutService.CreateOrUpdateShortcut(root,root));
            Assert.Empty(Directory.GetFiles(root,"*.lnk"));
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void OptInShortcut_IsARealWindowsLinkAndRepeatedActionDoesNotDuplicate()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=NewFolder();
        try
        {
            var app=Path.Combine(root,"App");
            var desktop=Path.Combine(root,"Desktop");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(desktop);
            var launcher=Path.Combine(root,"NajmCard.exe");
            File.WriteAllText(launcher,"launcher");
            File.WriteAllText(Path.Combine(app,"NajmCard.exe"),"internal app");

            string first=DesktopShortcutService.CreateOrUpdateShortcut(app,desktop);
            string second=DesktopShortcutService.CreateOrUpdateShortcut(app,desktop);
            Assert.Equal(first,second);
            Assert.Equal("تريم كارد.lnk",Path.GetFileName(first));
            Assert.Single(Directory.GetFiles(desktop,"*.lnk"));
            Assert.DoesNotContain(Directory.GetFiles(desktop,"*.lnk"),
                path=>Path.GetFileName(path).StartsWith("Tarim Card",StringComparison.OrdinalIgnoreCase));

            // WScript.Shell cannot reliably reopen a Unicode-named .lnk
            // under non-Arabic Windows locales. Verify the actual delivered
            // file bytes via a temporary ASCII pathname; do not waive the
            // target test or change the Arabic desktop shortcut.
            var inspection=Path.Combine(desktop,"inspect-target.lnk");
            File.Copy(first,inspection);
            try
            {
                Type shellType=Type.GetTypeFromProgID("WScript.Shell",throwOnError:true)!;
                object shell=Activator.CreateInstance(shellType)!;
                try
                {
                    object shortcut=shellType.InvokeMember("CreateShortcut",
                        BindingFlags.InvokeMethod,null,shell,[inspection])!;
                    try
                    {
                        var target=shortcut.GetType().InvokeMember("TargetPath",
                            BindingFlags.GetProperty,null,shortcut,null)?.ToString();
                        Assert.Equal(launcher,target,StringComparer.OrdinalIgnoreCase);
                    }
                    finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);}
                }
                finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
            }
            finally{File.Delete(inspection);}
        }
        finally{Directory.Delete(root,true);}
    }
}
