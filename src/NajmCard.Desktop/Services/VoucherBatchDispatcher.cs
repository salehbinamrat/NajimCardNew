using System.Diagnostics;
using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Durable, single-attempt RouterOS delivery. Mirrors Hadramout
/// core/delivery.py's SENDING -> SENT/FAILED/UNKNOWN transaction protocol.
/// </summary>
public sealed class VoucherBatchDispatcher(RouterGateway gateway, VoucherStore store)
{
    public async Task<BatchDeliveryResult> SendAsync(IReadOnlyList<Voucher> batch,
        CancellationToken cancellationToken = default,IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0 || batch.Count > 10000)
            throw new ArgumentException("عدد كروت الدفعة غير صالح.", nameof(batch));
        // Freeze the caller-owned collection before yielding. IReadOnlyList
        // does not prevent its owner from replacing cards during preflight.
        var snapshot=batch.ToArray();
        // SQLite FULL commits and DPAPI are synchronous. Keep the complete
        // sequential delivery transaction off the WPF dispatcher, including
        // its initial validation and continuations after each router reply.
        return await Task.Run(()=>SendSnapshotAsync(snapshot,cancellationToken,progress),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<BatchDeliveryResult> SendSnapshotAsync(
        IReadOnlyList<Voucher> batch,CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = batch[0] ?? throw new ArgumentException("الكرت الأول غير صالح.");
        var service = first.Service;
        if (service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الدفعة غير معروفة.");
        if (string.IsNullOrWhiteSpace(first.BatchId))
            throw new ArgumentException("معرف دفعة الكروت مطلوب.");
        var customer = first.Customer;
        if (service == "userman" && string.IsNullOrWhiteSpace(customer))
            throw new ArgumentException("حساب مدير User Manager مطلوب.");
        if (batch.Any(x => x is null || x.Service != service ||
            x.BatchId != first.BatchId ||
            (service == "userman" && x.Customer != customer)))
            throw new ArgumentException("لا ترسل كروت خدمات أو حسابات مدير مختلفة معاً.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (batch.Any(x => !seen.Add(x.Username)))
            throw new ArgumentException("الدفعة تحتوي اسم كرت مكرراً.");

        var persisted = store.GetBatchVouchers(service, first.BatchId).ToLookup(
            x => x.Service + "\u0000" + x.Username, StringComparer.OrdinalIgnoreCase);
        foreach (var voucher in batch)
        {
            var key = voucher.Service + "\u0000" + voucher.Username;
            if (!persisted[key].Any(x => x.BatchId == voucher.BatchId
                && x.Profile == voucher.Profile && x.Customer == voucher.Customer
                && x.Password == voucher.Password && !x.SentToRouter
                && x.DeliveryStatus is ("CREATED" or "FAILED")))
                throw new InvalidOperationException(
                    "الكرت مُرسل أو قيد التحقق أو غير محفوظ؛ راجع الراوتر أولاً.");
        }

        // A complete cross-service read is mandatory; do not lease a card or send
        // any RouterOS writes if any service is unavailable/forbidden or names collide.
        progress?.Report("التحقق من الأرقام الموجودة على الراوتر...");
        var existingNames = await gateway.ReadExistingVoucherNamesAsync(cancellationToken);
        var collisions = batch.Where(v => existingNames.Contains(v.Username))
            .Select(v => v.Username).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (collisions.Length > 0)
            throw new ArgumentException(
                "رقم الكرت موجود مسبقاً في Hotspot أو PPPoE أو User Manager. أُوقف إرسال الدفعة.");

        // An active User Manager account is not a working Hotspot voucher
        // unless the local NAS is configured for RADIUS. Do this *before*
        // the SQLite SENDING lease and any RouterOS mutation.
        if(service=="userman")
        {
            if(batch.Any(v=>v.Profile!=first.Profile))
                throw new ArgumentException(
                    "لا يمكن إصدار باقات مختلفة داخل دفعة User Manager واحدة.");
            progress?.Report("فحص جاهزية User Manager وربط Hotspot مع RADIUS...");
            await gateway.VerifyUserManagerV6HotspotReadinessAsync(
                first.Profile,customer,cancellationToken);
        }

        // On a real resilient RouterOS connection, a verified UM6 readback
        // uses an independent inventory lane. Start at most ONE following card
        // while the previous card is waiting for its exact-ID verification.
        // The normal path remains unchanged for single-lane/test clients.
        if(service=="userman" && batch.Count>1 &&
            gateway.SupportsOverlappedUserManagerV6Readback)
            return await SendUserManagerWithOverlappedReadbackAsync(
                batch,customer,cancellationToken,progress).ConfigureAwait(false);

        var failed = new List<string>();
        int succeeded = 0;
        int processed = 0;
        // For thousands of vouchers, avoid flooding the WPF dispatcher with two
        // notifications per card. Still report the first, last, ~80 checkpoints,
        // and every non-success outcome, without hiding the actual results.
        int progressStride = Math.Max(1, batch.Count / 80);
        // One open WAL connection per batch; every card still has TWO separate
        // durable FULL commits (lease before network, result after receipt).
        using var sendSession = store.OpenSendSession();
        foreach (var voucher in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int number = processed + 1;
            bool reportCard = batch.Count <= 24 || number == 1 ||
                number == batch.Count || number % progressStride == 0;
            if (reportCard)
                progress?.Report($"إرسال الكرت {number} من {batch.Count} إلى الراوتر...");
            // COMMIT to WAL+FULL synchronously BEFORE sending the first byte.
            sendSession.BeginSend(voucher);
            string state;
            string error = "";
            if (service == "userman")
            {
                try
                {
                    var result = await gateway.CreateAndActivateUserManagerV6Async(
                        voucher, customer, cancellationToken);
                    state = result.Success ? "SENT" :
                        result.UncertainOutcome ? "UNKNOWN" : "FAILED";
                    error = result.Error ?? "";
                }
                catch (Exception ex)
                {
                    // A partially processed router write may have reached the NAS.
                    state = "UNKNOWN";
                    error = "نتيجة إصدار User Manager غير مؤكدة: " + ex.Message;
                }
            }
            else if (service == "pppoe")
            {
                try
                {
                    var result = await gateway.CreatePppoeVoucherAsync(voucher, cancellationToken);
                    state = result.Success ? "SENT" :
                        result.UncertainOutcome ? "UNKNOWN" : "FAILED";
                    error = result.Error ?? "";
                }
                catch (Exception ex)
                {
                    state = "UNKNOWN";
                    error = "نتيجة إصدار PPPoE غير مؤكدة: " + ex.Message;
                }
            }
            else
            {
                try
                {
                    await gateway.AddHotspotUserAsync(voucher, cancellationToken);
                    state = "SENT";
                }
                catch (RouterOsTrapException ex)
                {
                    state = "FAILED"; // definitive rejection, not a lost reply
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    state = "UNKNOWN";
                    error = "نتيجة إصدار Hotspot غير مؤكدة: " + ex.Message;
                }
            }

            // If the DB write fails, the SENDING lease is retained. Stop and
            // investigate; never assume a potentially created card is absent.
            sendSession.FinishSend(voucher, state, error);
            if (reportCard || state != "SENT")
                progress?.Report($"تسجيل نتيجة الكرت {number} من {batch.Count}: "+
                    (state=="SENT"?"تم الإرسال":state=="FAILED"?"رفضه الراوتر":"النتيجة غير مؤكدة"));
            switch (state)
            {
                case "SENT":
                    succeeded++;
                    break;
                case "FAILED":
                    failed.Add(voucher.Username);
                    break;
                default:
                    return new BatchDeliveryResult(succeeded, failed, true);
            }
            processed++;
        }
        return new BatchDeliveryResult(succeeded, failed, false);
    }

    /// <summary>
    /// Hides the latency of a previous card's EXACT-ID RouterOS verification
    /// behind the next card's add/activate commands. ResilientRouterSession
    /// serializes ALL writes on one lane and reads receipts on an independent
    /// connection. At most two cards can be in flight; on any uncertain outcome
    /// no more leases are started, but the single already-started card is
    /// awaited and its actual result durably recorded before returning.
    ///
    /// Every card commits its SENDING lease before the first network byte and
    /// its individual receipt in a separate FULL SQLite transaction; a crash
    /// or timeout cannot silently authorize resending an unknown card.
    /// </summary>
    private async Task<BatchDeliveryResult> SendUserManagerWithOverlappedReadbackAsync(
        IReadOnlyList<Voucher> batch,string customer,CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        var startedAt=Stopwatch.GetTimestamp();
        var failed=new List<string>();
        var pending=new Queue<(Voucher Voucher, int Number,
            Task<(string State,string Error)> Result)>();
        int next=0,processed=0,succeeded=0;
        int stride=Math.Max(1,batch.Count/80);
        bool uncertain=false;
        Exception? writeFailure=null;

        // Convert every router outcome into a value: even if a pending API
        // call fails after an earlier one is uncertain, its SENDING lease has
        // an authoritative local outcome rather than a forgotten task.
        async Task<(string State,string Error)> IssueAsync(Voucher voucher)
        {
            try
            {
                var result=await gateway.CreateAndActivateUserManagerV6Async(
                    voucher,customer,cancellationToken).ConfigureAwait(false);
                return (result.Success?"SENT":
                    result.UncertainOutcome?"UNKNOWN":"FAILED",result.Error??"");
            }
            catch(Exception ex)
            {
                return ("UNKNOWN","نتيجة إصدار User Manager غير مؤكدة: "+ex.Message);
            }
        }

        // One local connection is owned exclusively by this coordinator.
        // Only one thread executes its SQLite lease and receipt operations;
        // the RouterOS tasks never touch the store or its open connection.
        using var sendSession=store.OpenSendSession();
        while((next<batch.Count && !uncertain && writeFailure is null &&
                !cancellationToken.IsCancellationRequested)||pending.Count>0)
        {
            while(next<batch.Count && pending.Count<2 && !uncertain &&
                  writeFailure is null && !cancellationToken.IsCancellationRequested)
            {
                // Consume any completed error before admitting another card.
                if(pending.Count>0 && pending.Peek().Result.IsCompleted)
                    break;
                var voucher=batch[next];
                var number=next+1;
                try
                {
                    // Durable first: no RouterOS operation before this returns.
                    sendSession.BeginSend(voucher);
                }
                catch(Exception ex)
                {
                    writeFailure=ex;
                    break;
                }
                if(number==1 || number==batch.Count || number%stride==0)
                    progress?.Report($"إصدار الكرت {number} من {batch.Count} عبر مساري RouterOS...");
                pending.Enqueue((voucher,number,IssueAsync(voucher)));
                next++;
            }
            if(pending.Count==0)break;

            var current=pending.Dequeue();
            var outcome=await current.Result.ConfigureAwait(false);
            try
            {
                // FULL commit after the exact-ID router readback, never after
                // a batch-level timer or a subsequent unrelated voucher.
                sendSession.FinishSend(current.Voucher,outcome.State,outcome.Error);
            }
            catch(Exception ex)
            {
                writeFailure??=ex;
                uncertain=true;
            }
            processed++;
            if(outcome.State=="SENT")
                succeeded++;
            else if(outcome.State=="FAILED")
                failed.Add(current.Voucher.Username);
            else
                uncertain=true;

            if(processed==batch.Count || processed==1 ||
                processed%stride==0 || outcome.State!="SENT")
            {
                var elapsed=Stopwatch.GetElapsedTime(startedAt);
                var secondsPerCard=elapsed.TotalSeconds/processed;
                progress?.Report($"اكتمل {processed} من {batch.Count} كرت | "+
                    $"المتوسط {secondsPerCard:F2} ثانية/كرت | "+
                    (outcome.State=="SENT"?"تم التأكيد":
                     outcome.State=="FAILED"?"رفض مؤكد":"النتيجة غير مؤكدة"));
            }
        }

        if(writeFailure is not null)
            throw new InvalidOperationException(
                "تعذر حفظ حالة كرت أثناء الإرسال. توقفت الدفعة؛ "+
                "افحص الحسابات وسجلات SQLite قبل المحاولة مجددا.",writeFailure);
        if(cancellationToken.IsCancellationRequested && !uncertain)
            cancellationToken.ThrowIfCancellationRequested();
        return new BatchDeliveryResult(succeeded,failed,uncertain);
    }
}
