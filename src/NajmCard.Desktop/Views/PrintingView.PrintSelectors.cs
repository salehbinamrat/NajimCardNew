using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Lazy, read-only selector loading. No RouterOS reads or password decryption
/// are performed when the user opens the print batch/printer dropdowns.
/// </summary>
public partial class PrintingView
{
    private ComboBox? _printerSelector;

    public VoucherOutputPreferences GetOutputPreferences() =>
        new VoucherOutputPreferences(
            _printerSelector?.SelectedItem?.ToString()??"الافتراضية",
            _additionalExport.IsChecked==true,
            _csvExport.IsChecked==true,
            _excelExport.IsChecked==true).Validate();

    public void ApplyOutputPreferences(VoucherOutputPreferences preferences)
    {
        preferences.Validate();
        _additionalExport.IsChecked=preferences.AdditionalExport;
        _csvExport.IsChecked=preferences.ExportCsv;
        _excelExport.IsChecked=preferences.ExportExcel;
        if(_printerSelector is { } printer)
        {
            // Preserve a printer saved on another PC, but do not silently
            // switch to a different physical queue if it is unavailable.
            if(!printer.Items.Contains(preferences.PrinterName))
                printer.Items.Add(preferences.PrinterName);
            printer.SelectedItem=preferences.PrinterName;
        }
    }

    public void ResetPrintBatchSelection()
    {
        if(_batchSelector is {Items.Count:>0} selector)
            selector.SelectedIndex=0;
    }

    private (ComboBox BatchSelector,ComboBox PrinterSelector) CreatePrintSelectors(
        VoucherStore store,Func<IReadOnlyList<Voucher>>? selectedBatch)
    {
        // Scope the requested light-burgundy / white theme to the BATCH
        // selector only. Printer, paper, orientation and other controls retain
        // their saved settings and independent appearance.
        var burgundy=(Brush)new BrushConverter().ConvertFrom("#B75D7D")!;
        var hoverBurgundy=(Brush)new BrushConverter().ConvertFrom("#C87692")!;
        var batchStyle=new Style(typeof(ComboBox),
            (Style)Application.Current.FindResource("DarkComboBox"));
        batchStyle.Setters.Add(new Setter(Control.BackgroundProperty,burgundy));
        batchStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        batchStyle.Setters.Add(new Setter(Control.BorderBrushProperty,
            (Brush)new BrushConverter().ConvertFrom("#76223F")!));
        var rowStyle=new Style(typeof(ComboBoxItem));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty,burgundy));
        rowStyle.Setters.Add(new Setter(Control.FontFamilyProperty,new FontFamily("Arial")));
        rowStyle.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,7,10,7)));
        var visual=new ControlTemplate(typeof(ComboBoxItem));
        var surface=new FrameworkElementFactory(typeof(Border));
        surface.Name="BatchOptionBackground";
        surface.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding(
            nameof(ComboBoxItem.Background))
        {
            RelativeSource=new System.Windows.Data.RelativeSource(
                System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        var text=new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(ContentPresenter.ContentSourceProperty,"Content");
        text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        text.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Brushes.White);
        surface.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding(
            nameof(ComboBoxItem.Padding))
        {
            RelativeSource=new System.Windows.Data.RelativeSource(
                System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        surface.AppendChild(text);
        visual.VisualTree=surface;
        var hovered=new Trigger{Property=ComboBoxItem.IsHighlightedProperty,Value=true};
        hovered.Setters.Add(new Setter(Control.BackgroundProperty,hoverBurgundy));
        visual.Triggers.Add(hovered);
        var chosen=new Trigger{Property=ComboBoxItem.IsSelectedProperty,Value=true};
        chosen.Setters.Add(new Setter(Control.BackgroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#8B3554")!));
        visual.Triggers.Add(chosen);
        rowStyle.Setters.Add(new Setter(Control.TemplateProperty,visual));
        batchStyle.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty,rowStyle));
        var batchSelector=new ComboBox
        {
            MinWidth=0,Style=batchStyle,Foreground=Brushes.White,
            ToolTip="الدفعة الحالية أو دفعة سابقة مؤكدة الإرسال، دون طباعة حالات فاشلة."
        };
        _batchSelector=batchSelector;
        ComboBoxItem BatchChoice(string caption,PrintableBatchSummary? saved=null)=>
            new(){Content=caption,Tag=saved,Style=rowStyle,Foreground=Brushes.White};
        batchSelector.Items.Add(BatchChoice(
            selectedBatch is null?"الدفعة الأخيرة":"الدفعة الحالية"));
        batchSelector.SelectedIndex=0;
        bool loadingBatches=false;
        batchSelector.DropDownOpened+=async (_,_) =>
        {
            if(loadingBatches)return;
            loadingBatches=true;
            try
            {
                var history=await Task.Run(()=>store.ListPrintableBatches());
                var previous=(batchSelector.SelectedItem as ComboBoxItem)?.Tag
                    as PrintableBatchSummary;
                batchSelector.Items.Clear();
                batchSelector.Items.Add(BatchChoice(
                    selectedBatch is null?"الدفعة الأخيرة":"الدفعة الحالية"));
                foreach(var info in history)
                {
                    var option=BatchChoice(info.DisplayName,info);
                    option.ToolTip=info.DisplayName;
                    batchSelector.Items.Add(option);
                }
                var selected=batchSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>
                    x.Tag is PrintableBatchSummary item && previous is not null &&
                    item.BatchId==previous.BatchId && item.Service==previous.Service);
                batchSelector.SelectedItem=selected??batchSelector.Items[0];
            }
            catch(Exception ex)
            {
                _status.Text="تعذر قراءة محفوظات الدفعات: "+ex.Message;
            }
            finally{loadingBatches=false;}
        };
        var selectedPrinter=new ComboBox
        {
            MinWidth=0,ToolTip="اختر طابعة مثبتة على Windows، أو اترك الافتراضية."
        };
        _printerSelector=selectedPrinter;
        selectedPrinter.Items.Add("الافتراضية");
        selectedPrinter.SelectedIndex=0;
        bool loadingPrinters=false;
        selectedPrinter.DropDownOpened+=async (_,_) =>
        {
            if(loadingPrinters)return;
            loadingPrinters=true;
            try
            {
                var printers=await Task.Run(()=>
                {
                    using var server=new LocalPrintServer();
                    return server.GetPrintQueues().Select(q=>q.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToArray();
                });
                var selected=selectedPrinter.SelectedItem?.ToString();
                selectedPrinter.Items.Clear();
                selectedPrinter.Items.Add("الافتراضية");
                foreach(var name in printers)selectedPrinter.Items.Add(name);
                if(selected is not null && !selectedPrinter.Items.Contains(selected))
                {
                    // Never substitute another printer for the saved one.
                    // Keeping the unavailable name makes GetPrintQueue fail
                    // closed at print time instead of printing elsewhere.
                    selectedPrinter.Items.Add(selected);
                    _status.Text="الطابعة المحفوظة «"+selected+
                        "» غير مثبتة على Windows الحالي. اختر طابعة صحيحة قبل الطباعة.";
                }
                selectedPrinter.SelectedItem=selected is not null &&
                    selectedPrinter.Items.Contains(selected)?selected: selectedPrinter.Items[0];
            }
            catch(Exception ex)
            {
                _status.Text="تعذر جلب قائمة الطابعات؛ ستبقى الطابعة الافتراضية: "+ex.Message;
            }
            finally{loadingPrinters=false;}
        };
        return (batchSelector,selectedPrinter);
    }
}
