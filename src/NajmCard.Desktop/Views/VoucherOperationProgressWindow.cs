using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Compact non-modal, RTL progress dialog. It never starts, retries, or cancels
/// a router write; it only reflects stages emitted by the issuing workflow.
/// The caller always closes it in finally, including on failure.
/// </summary>
internal sealed class VoucherOperationProgressWindow : Window
{
    private readonly TextBlock _stage;
    private readonly TextBlock _confirmed=new(){Foreground=new SolidColorBrush(Color.FromRgb(11,44,96)),FontSize=12,TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar _indicator;

    public VoucherOperationProgressWindow(string operation)
    {
        Width=395;
        Height=176;
        MinWidth=395;
        MaxWidth=395;
        MinHeight=176;
        SizeToContent=SizeToContent.Height;
        MaxHeight=Math.Max(MinHeight,SystemParameters.WorkArea.Height);
        ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ShowInTaskbar=false;
        Background=(Brush)new BrushConverter().ConvertFrom("#E8EDF5")!;
        FlowDirection=FlowDirection.RightToLeft;
        FontFamily=new FontFamily("Arial");

        var heading=new TextBlock
        {
            Text=operation,
            Foreground=(Brush)new BrushConverter().ConvertFrom("#0B2C60")!,
            FontSize=17,
            FontWeight=FontWeights.Bold,
            TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,11)
        };
        _stage=new TextBlock
        {
            Text="جار التحقق من إعدادات العملية...",
            Foreground=new SolidColorBrush(Color.FromRgb(11,44,96)),
            FontSize=13,
            TextWrapping=TextWrapping.Wrap,
            MinHeight=42,
            VerticalAlignment=VerticalAlignment.Center
        };
        _indicator=new ProgressBar
        {
            Height=8,
            IsIndeterminate=true,
            Foreground=(Brush)new BrushConverter().ConvertFrom("#075DFF")!,
            Background=(Brush)new BrushConverter().ConvertFrom("#DDE5F1")!,
            Margin=new Thickness(0,8,0,1)
        };
        var content=new StackPanel();
        content.Children.Add(heading);
        content.Children.Add(_stage);
        content.Children.Add(_confirmed);
        content.Children.Add(_indicator);
        Content=new Border
        {
            Background=(Brush)new BrushConverter().ConvertFrom("#E8EDF5")!,
            BorderBrush=(Brush)new BrushConverter().ConvertFrom("#075DFF")!,
            BorderThickness=new Thickness(2),
            CornerRadius=new CornerRadius(11),
            Padding=new Thickness(17,14,17,12),
            Child=new ScrollViewer
            {
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Content=content
            }
        };
    }

    public void UpdateStage(string stage)
    {
        if(!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(()=>UpdateStage(stage)));
            return;
        }
        _stage.Text=stage;
    }

    public void UpdateConfirmed(VoucherPipelineProgress progress)
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>UpdateConfirmed(progress)));return;}
        _indicator.IsIndeterminate=progress.Total<=0;
        _indicator.Maximum=Math.Max(1,progress.Total);_indicator.Value=progress.Confirmed;
        _confirmed.Text=$"مؤكد ومحفوظ: {progress.Confirmed:N0} / {progress.Total:N0} | فاشل: {progress.Failed:N0} | غير مؤكد: {progress.Unknown:N0}";
    }
}
