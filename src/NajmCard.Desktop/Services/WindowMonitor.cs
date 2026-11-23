using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NajmCard.Desktop.Services;

internal static class WindowMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo {public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);

    internal static Rect WorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(handle==IntPtr.Zero||!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))
            return SystemParameters.WorkArea;
        var area=new Rect(info.Work.Left,info.Work.Top,
            info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top);
        if(PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
            area.Transform(target.TransformFromDevice);
        return area;
    }
}
