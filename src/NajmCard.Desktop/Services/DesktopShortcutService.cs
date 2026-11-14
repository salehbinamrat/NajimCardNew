using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Explicit user-directed shortcut creation; no startup tasks, services,
/// registry writes or administrator rights. Installer and Portable share the
/// same desktop link name, so repeated clicks update rather than duplicate.
/// </summary>
public static class DesktopShortcutService
{
    private const string LinkName="تريم كارد.lnk";

    public static string ResolveExecutablePath(string? applicationDirectory=null)
    {
        var directory=Path.GetFullPath(applicationDirectory??AppContext.BaseDirectory);
        var app=Path.Combine(directory,"NajmCard.exe");
        if(!File.Exists(app))
            throw new FileNotFoundException(
                "تعذر العثور على ملف تشغيل تريم كارد في مجلد البرنامج.",app);

        // An organized Portable/Installer keeps all runtime libraries in
        // App/, but the shortcut must launch the icon-bearing ROOT EXE.
        if(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))
               .Equals("App",StringComparison.OrdinalIgnoreCase))
        {
            var parent=Directory.GetParent(directory)?.FullName;
            if(parent is not null)
            {
                var launcher=Path.Combine(parent,"NajmCard.exe");
                if(File.Exists(launcher))return launcher;
            }
        }
        // Developer builds or old Portable layouts have only one EXE.
        return app;
    }

    public static string CreateOrUpdateShortcut(string? applicationDirectory=null,
        string? desktopDirectory=null)
    {
        if(!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "اختصار سطح المكتب مدعوم على Windows فقط.");

        // Resolve first. A missing/incomplete ZIP must never create a
        // broken shortcut pointing at a non-existent application.
        string target=ResolveExecutablePath(applicationDirectory);
        string desktop=desktopDirectory??
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if(string.IsNullOrWhiteSpace(desktop)||!Directory.Exists(desktop))
            throw new DirectoryNotFoundException(
                "تعذر الوصول إلى مجلد سطح المكتب لهذا المستخدم.");
        string linkPath=Path.Combine(desktop,LinkName);

        Type shellType=Type.GetTypeFromProgID("WScript.Shell",throwOnError:true)
            ??throw new NotSupportedException("خدمة اختصارات Windows غير متاحة.");
        object shell=Activator.CreateInstance(shellType)
            ??throw new NotSupportedException("تعذر بدء خدمة اختصارات Windows.");
        try
        {
            object shortcut=shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod,null,shell,[linkPath])
                ??throw new IOException("تعذر إنشاء اختصار Windows.");
            try
            {
                var kind=shortcut.GetType();
                void Set(string key,string value)=>kind.InvokeMember(key,
                    BindingFlags.SetProperty,null,shortcut,[value]);
                Set("TargetPath",target);
                Set("WorkingDirectory",Path.GetDirectoryName(target)!);
                Set("IconLocation",target+",0");
                Set("Description","تريم كارد - طباعة الكروت وإدارة الشبكات");
                kind.InvokeMember("Save",BindingFlags.InvokeMethod,
                    null,shortcut,null);
            }
            finally
            {
                if(Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            if(Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
        if(!File.Exists(linkPath))
            throw new IOException("لم يؤكد Windows إنشاء ملف الاختصار.");
        return linkPath;
    }
}
