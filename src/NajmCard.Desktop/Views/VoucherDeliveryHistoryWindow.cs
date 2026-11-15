using System.Windows;
using System.Windows.Controls;
using NajmCard.Desktop.Services;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

internal sealed class VoucherDeliveryHistoryWindow : Window
{
    public VoucherDeliveryHistoryWindow(VoucherStore store,RouterGateway? gateway=null)
    {
        Title = "نجم كارد | سجل الدفعات";
        Width = 900;
        Height = 560;
        MinWidth = 460;
        MinHeight = 330;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;

        var root = new DockPanel();
        var header = PageUi.Column();
        header.Children.Add(PageUi.Title("سجل الدفعات"));
        var batches = new ComboBox
        {
            DisplayMemberPath = nameof(VoucherDeliveryBatch.DisplayName),
            MinHeight = 32,
            Style = (Style)Application.Current.FindResource("DarkComboBox")
        };
        header.Children.Add(batches);
        var status = PageUi.Hint("جار تحميل الدفعات المحفوظة…");
        status.Margin = new Thickness(0, 8, 0, 8);
        header.Children.Add(status);
        var actions=new WrapPanel();
        var review=PageUi.Button("مطابقة مع الراوتر",false);
        var resume=PageUi.Button("استكمال الكروت المثبت غيابها",false);
        actions.Children.Add(review);actions.Children.Add(resume);header.Children.Add(actions);
        bool busy=false;
        void UpdateActions()
        {
            var selected=batches.SelectedItem as VoucherDeliveryBatch;
            review.IsEnabled=resume.IsEnabled=!busy&&gateway is not null&&selected?.RouterId.Length>0;
        }
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var close = PageUi.Button("إغلاق", false);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var cards = PageUi.Table(("رقم الكرت", nameof(VoucherDeliveryEntry.Number)),
            ("حالة الإرسال", nameof(VoucherDeliveryEntry.Status)),
            ("آخر تحديث UTC",nameof(VoucherDeliveryEntry.UpdatedUtc)),("النتيجة",nameof(VoucherDeliveryEntry.Result)));
        cards.MinHeight = 0;
        cards.IsReadOnly = true;
        cards.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        var events=PageUi.Table(("الكرت",nameof(VoucherDeliveryEvent.Username)),
            ("من",nameof(VoucherDeliveryEvent.PreviousState)),("إلى",nameof(VoucherDeliveryEvent.State)),
            ("التوقيت UTC",nameof(VoucherDeliveryEvent.Timestamp)),("المحاولة",nameof(VoucherDeliveryEvent.AttemptId)),
            ("النتيجة",nameof(VoucherDeliveryEvent.Reason)));
        events.IsReadOnly=true;
        var tabs=new TabControl();
        tabs.Items.Add(new TabItem{Header="حالة الكروت",Content=cards});
        tabs.Items.Add(new TabItem{Header="تاريخ المحاولات والمطابقة",Content=events});
        root.Children.Add(tabs);
        Content = PageUi.Surface(root);

        async Task RefreshAsync(VoucherDeliveryBatch? selected=null)
        {
            var history=await Task.Run(store.ListDeliveryBatches);
            batches.ItemsSource=history;
            batches.SelectedItem=history.FirstOrDefault(x=>selected is not null&&x.RouterId==selected.RouterId&&
                x.Service==selected.Service&&x.BatchId==selected.BatchId)??history.FirstOrDefault();
            if(history.Count==0)status.Text="لا توجد دفعات محفوظة بعد.";
            UpdateActions();
        }
        async Task RecoverAsync(bool send)
        {
            if(busy||gateway is null||batches.SelectedItem is not VoucherDeliveryBatch batch)return;
            if(send&&!PageUi.Confirm("سيُستكمل فقط ما أثبتت المطابقة غيابه عن الراوتر الصحيح، بعد التحقق مجددا. هل تريد المتابعة؟"))return;
            busy=true;batches.IsEnabled=false;UpdateActions();
            try
            {
                var recovery=new VoucherBatchRecovery(gateway,store);
                if(send)
                {
                    var result=await recovery.ResumeAsync(batch.RouterId,batch.Service,batch.BatchId,
                        progress:new Progress<string>(message=>status.Text=message));
                    await RefreshAsync(batch);
                    status.Text=$"اكتمل الاستئناف: مؤكد {result.Succeeded}، رفض {result.Failed.Count}"+
                        (result.UncertainOutcome?"؛ توقفت العملية بنتيجة غير مؤكدة. راجع السجل ونفذ المطابقة مجددا.":".");
                }
                else
                {
                    status.Text="جار التحقق من هوية الراوتر ومطابقة الكروت؛ لا تُرسل أوامر إنشاء أثناء المطابقة…";
                    await recovery.ReviewAsync(batch.RouterId,batch.Service,batch.BatchId);
                    await RefreshAsync(batch);
                }
            }
            catch(Exception ex){status.Text="توقفت العملية بأمان: "+ex.Message;}
            finally{busy=false;batches.IsEnabled=true;UpdateActions();}
        }
        review.Click+=async (_,_)=>await RecoverAsync(false);
        resume.Click+=async (_,_)=>await RecoverAsync(true);
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};

        batches.SelectionChanged += async (_, _) =>
        {
            cards.ItemsSource = null;
            events.ItemsSource=null;UpdateActions();
            if (batches.SelectedItem is not VoucherDeliveryBatch batch) return;
            status.Text = "جار قراءة حالة الكروت…";
            try
            {
                var entries = await Task.Run(() => store.ReadDeliveryEntries(batch.Service, batch.BatchId,batch.RouterId));
                var timeline=await Task.Run(()=>store.ReadDeliveryEvents(batch.RouterId,batch.Service,batch.BatchId));
                if (!IsVisible || !ReferenceEquals(batches.SelectedItem, batch)) return;
                cards.ItemsSource = entries;
                events.ItemsSource=timeline;
                status.Text = $"العدد {batch.Total} · مؤكد {batch.Sent} · لم يرسل {batch.Pending}" +
                    $" · فشل {batch.Failed} · يحتاج مراجعة {batch.NeedsReview}"+
                    (batch.RouterId.Length==0?" | ملكية الراوتر غير معروفة؛ هذه الدفعة محفوظة ولا تُرسل أو تُنسب تلقائيا إلى الاتصال الحالي.":
                        " | الراوتر "+batch.RouterLabel);
            }
            catch (Exception ex)
            {
                if (IsVisible && ReferenceEquals(batches.SelectedItem, batch))
                    status.Text = "تعذر قراءة حالة الدفعة: " + ex.Message;
            }
        };

        Loaded += async (_, _) =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                if (IsVisible) status.Text = "تعذر تحميل سجل الدفعات: " + ex.Message;
            }
        };
    }
}
