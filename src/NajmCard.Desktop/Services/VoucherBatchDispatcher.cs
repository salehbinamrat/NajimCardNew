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
        if((service is "hotspot" or "pppoe") &&
            batch.Count>=2 && gateway.SupportsTaggedSimpleAdd)
            return await SendTaggedWindowsAsync(batch,service,sendSession,
                cancellationToken,progress);
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
    /// Only independent Hotspot/PPPoE adds are pipelined; UM v6 still requires
    /// individual add, activate and verified exact-ID readback.
    ///
    /// Every card in a window is atomically leased on durable SQLite WAL
    /// BEFORE the first network byte. A lost/malformed tagged reply marks the
    /// whole window UNKNOWN and stops, never replaying potentially issued IDs.
    /// </summary>
    private async Task<BatchDeliveryResult> SendTaggedWindowsAsync(
        IReadOnlyList<Voucher> batch,string service,VoucherStore.SendSession session,
        CancellationToken cancellationToken,IProgress<string>? progress)
    {
        int sent=0;
        var failed=new List<string>();
        for(int offset=0;offset<batch.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int remaining=batch.Count-offset;
            int size=Math.Min(8,remaining);
            // Never leave a singleton tail: two or more commands per tagged
            // window are needed for actual latency amortization.
            if(remaining-size==1)size--;
            var window=batch.Skip(offset).Take(size).ToArray();
            progress?.Report($"إرسال مجموعة من {size} كروت، حتى الكرت "+
                $"{offset+size} من {batch.Count}...");
            session.BeginSendWindow(window);
            IReadOnlyList<RouterTaggedAddOutcome> replies;
            try
            {
                replies=await gateway.AddSimpleWindowAsync(window,cancellationToken);
                if(replies.Count!=window.Length)
                    throw new InvalidDataException(
                        "عدد ردود RouterOS لا يطابق عدد الكروت المرسلة.");
            }
            catch(Exception ex)
            {
                // We cannot know which tagged commands actually reached
                // RouterOS. Protect every leased voucher and stop the batch.
                foreach(var card in window)
                    session.FinishSend(card,"UNKNOWN",
                        "فقدت نتيجة مجموعة الإرسال؛ راجع الراوتر قبل التكرار: "+ex.Message);
                progress?.Report("تعذر تأكيد نتيجة المجموعة؛ أوقف الإرسال للمراجعة.");
                return new BatchDeliveryResult(sent,failed,true);
            }

            bool uncertain=false;
            for(int index=0;index<window.Length;index++)
            {
                var card=window[index];
                var receipt=replies[index];
                string state,error="";
                if(receipt.Rejected)
                {
                    state="FAILED";
                    error=receipt.Error??"رفض الراوتر إنشاء الحساب.";
                    failed.Add(card.Username);
                }
                else if(service=="pppoe" &&
                    (receipt.ReturnId is not string id ||
                     id.Length is < 2 or > 40 || id[0]!='*' ||
                     id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c))))
                {
                    // PPP issuance MUST have a newly returned internal ID.
                    // A successful !done with no usable =ret= is not proof.
                    state="UNKNOWN";
                    uncertain=true;
                    error="لم يُرجع RouterOS معرّف PPP واحدا صالحا؛ افحص الراوتر.";
                }
                else state="SENT";
                session.FinishSend(card,state,error);
                if(state=="SENT")sent++;
            }
            offset+=size;
            progress?.Report($"نتيجة المجموعة: تم تأكيد {sent} من {batch.Count}"+
                (uncertain?"؛ توجد نتيجة غير مؤكدة.":""));
            if(uncertain)return new BatchDeliveryResult(sent,failed,true);
        }
        return new BatchDeliveryResult(sent,failed,false);
    }

}
