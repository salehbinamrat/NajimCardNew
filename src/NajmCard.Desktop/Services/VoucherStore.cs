using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Separate NajmCard-only SQLite store. Windows DPAPI protects voucher passwords;
/// migration uses a single SQLite transaction and never alters Hadramout databases.
/// </summary>
public sealed partial class VoucherStore : IDisposable
{
    private readonly string _filename;
    public string DirectoryPath { get; }

    public VoucherStore(string? rootDirectory = null)
    {
        DirectoryPath = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NajmCardNew", "Data"));
        Directory.CreateDirectory(DirectoryPath);
        _filename = Path.Combine(DirectoryPath, "najmcard.db");
        _lifetimeLease=SharedLease();
        try
        {
            using var db = Open(configureWal:true);
            EnsureSchema(db);
        }
        catch{Dispose();throw;}
    }

    private SqliteConnection Open(bool configureWal=false)
    {
        var release=TrackConnection();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {DataSource=_filename,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
        connection.StateChange+=(_,e)=>{if(e.CurrentState==System.Data.ConnectionState.Closed)release();};
        try
        {
            connection.Open();
            using var pragma = connection.CreateCommand();
            // journal_mode is persisted by SQLite in the database itself.
            // Re-issuing journal_mode=WAL on every read may acquire an avoidable
            // database lock for every dashboard query and voucher receipt.
            // Initialize it once per store instance, but preserve FULL sync
            // and the busy timeout on EVERY connection for durable vouchers.
            pragma.CommandText = configureWal
                ? "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;"
                : "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;";
            pragma.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();release();
            throw;
        }
    }

    private void EnsureSchema(SqliteConnection db)
    {
        EnsureRouterDeliverySchema(db);
        EnsureIndexedQuerySchema(db);
        EnsureActivitySchema(db);
    }

    // No password is a legitimate Hadramout code_only voucher. Do not call
    // Windows DPAPI with an empty byte array; store a non-secret marker.
    private const string EmptyPasswordMarker="no-password:v1";
    private static string Protect(string raw) => raw.Length==0
        ? EmptyPasswordMarker
        : Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(raw), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string coded) => coded==EmptyPasswordMarker
        ? ""
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(coded), null, DataProtectionScope.CurrentUser));

    private static void Validate(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if(voucher.RouterId is null||voucher.RouterId.Length>128||voucher.RouterId.Any(char.IsControl))
            throw new ArgumentException("هوية الراوتر غير صالحة.");
        if (voucher.Service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الكرت غير مدعومة.", nameof(voucher));
        if (string.IsNullOrWhiteSpace(voucher.Username)
            || string.IsNullOrWhiteSpace(voucher.Profile)
            || voucher.Password is null)
            throw new ArgumentException("بيانات الكرت غير مكتملة.", nameof(voucher));
        if (voucher.Service == "userman" && string.IsNullOrWhiteSpace(voucher.Customer))
            throw new ArgumentException("حساب مدير User Manager v6 مطلوب.", nameof(voucher));
    }

    public void SaveBatch(IReadOnlyList<Voucher> vouchers,
        VoucherBatchPrintSnapshot? printSnapshot=null)
        =>SaveBatchCore(vouchers,printSnapshot,false);

    public void SavePreparedBatch(IReadOnlyList<Voucher> vouchers,
        VoucherBatchPrintSnapshot? printSnapshot=null)
        =>SaveBatchCore(vouchers,printSnapshot,true);

    private void SaveBatchCore(IReadOnlyList<Voucher> vouchers,
        VoucherBatchPrintSnapshot? printSnapshot,bool recheckNames)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.SQLite);
        ArgumentNullException.ThrowIfNull(vouchers);
        if(vouchers.Count==0)return;
        if(vouchers.Count>10000)
            throw new ArgumentOutOfRangeException(nameof(vouchers),
                "لا يمكن حفظ أكثر من 10000 كرت في دفعة واحدة.");
        foreach(var voucher in vouchers)Validate(voucher);
        if(printSnapshot is not null)
        {
            printSnapshot.Validate();
            if(vouchers.Any(v=>v.Service!=printSnapshot.Service ||
                v.BatchId!=printSnapshot.BatchId ||
                v.Profile!=printSnapshot.Profile ||
                v.Customer!=printSnapshot.Customer || v.RouterId!=printSnapshot.RouterId))
                throw new ArgumentException(
                    "قالب الطباعة لا يطابق خدمة أو دفعة أو باقة الكروت.");
        }
        // DPAPI can be slow; finish it before holding SQLite's writer lock.
        var protectedPasswords=vouchers.Select(v=>Protect(v.Password)).ToArray();
        using var db=Open();
        using var tx=db.BeginTransaction();
        if(recheckNames)
        {
            var firstRouter=vouchers[0].RouterId;RequireRouter(firstRouter);
            if(vouchers.Any(v=>v.RouterId!=firstRouter))throw new ArgumentException("اختر راوترا واحدا للدفعة.");
            var occupied=ReadReservedAcrossServices(db,tx,firstRouter);
            if(vouchers.Any(v=>!occupied.Add(v.Username)))throw new InvalidOperationException(
                "رقم كرت محجوز محليا في خدمة أخرى أو في سجل قديم غير محدد الملكية؛ لم تُحفظ الدفعة.");
        }
        // One prepared INSERT and its parameter objects are reused across the
        // entire SQLite transaction. This eliminates thousands of command and
        // SQLite compilation allocations on large RouterOS batches.
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO vouchers(service,username,customer,password_protected,
                profile,batch_id,created_at,sent,delivery_status,router_id,updated_utc)
            VALUES($svc,$u,$customer,$p,$profile,$b,$c,$s,$state,$router,$c)
            """;
        var router=cmd.Parameters.Add("$router",SqliteType.Text);
        var svc=cmd.Parameters.Add("$svc",SqliteType.Text);
        var username=cmd.Parameters.Add("$u",SqliteType.Text);
        var customer=cmd.Parameters.Add("$customer",SqliteType.Text);
        var password=cmd.Parameters.Add("$p",SqliteType.Text);
        var profile=cmd.Parameters.Add("$profile",SqliteType.Text);
        var batch=cmd.Parameters.Add("$b",SqliteType.Text);
        var created=cmd.Parameters.Add("$c",SqliteType.Text);
        var sent=cmd.Parameters.Add("$s",SqliteType.Integer);
        var state=cmd.Parameters.Add("$state",SqliteType.Text);
        cmd.Prepare();
        using var history=CreateDeliveryEventCommand(db,tx);
        int position=0;
        foreach(var voucher in vouchers)
        {
            router.Value=voucher.RouterId;
            svc.Value=voucher.Service;
            username.Value=voucher.Username;
            customer.Value=voucher.Customer;
            password.Value=protectedPasswords[position++];
            profile.Value=voucher.Profile;
            batch.Value=voucher.BatchId;
            created.Value=voucher.CreatedAt.ToString("O",CultureInfo.InvariantCulture);
            sent.Value=voucher.SentToRouter?1:0;
            state.Value=voucher.SentToRouter?"SENT":"CREATED";
            cmd.ExecuteNonQuery();
            WriteDeliveryEvent(db,tx,voucher,"",voucher.SentToRouter?"SENT":"CREATED","حفظ الكرت محليا",history);
        }
        // Register each batch once, instead of reexecuting an ignored INSERT
        // for every voucher. Keep the audit and print snapshot in this FULL commit.
        foreach(var voucher in vouchers.DistinctBy(v=>(v.RouterId,v.Service,v.BatchId)))
        {
            using var entry=db.CreateCommand();entry.Transaction=tx;
            entry.CommandText="INSERT OR IGNORE INTO voucher_batches VALUES($router,$service,$batch,$created)";
            entry.Parameters.AddWithValue("$router",voucher.RouterId);
            entry.Parameters.AddWithValue("$service",voucher.Service);
            entry.Parameters.AddWithValue("$batch",voucher.BatchId);
            entry.Parameters.AddWithValue("$created",voucher.CreatedAt.ToString("O"));
            entry.ExecuteNonQuery();
        }
        // Same transaction: a batch is never committed without its activity.
        var first=vouchers[0];
        WriteActivity(db,tx,ActivityBatchKey(first.Service,first.BatchId,first.RouterId),
            "CARDS","إنشاء دفعة كروت","INFO",
            "خدمة "+first.Service+" | عدد الكروت "+vouchers.Count.ToString(CultureInfo.InvariantCulture));
        // Credential rows and their print-template snapshot must commit as one
        // durable SQLite transaction. Do not leave a printable but unstyled batch.
        if(printSnapshot is not null)
            WriteBatchPrintSnapshot(db,tx,printSnapshot);
        tx.Commit();
    }

    // A single SELECT holds one SQLite read snapshot through the entire export.
    // UI list limits must never silently truncate a file. Public exports do not
    // select encrypted password bytes or require the Windows DPAPI key.
    public IReadOnlyList<Voucher> GetVouchersForExport(bool includePasswords=false)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        var secret=includePasswords?"password_protected":"NULL";
        cmd.CommandText=$"""
            SELECT username,{secret},profile,batch_id,created_at,
                sent,service,customer,delivery_status,router_id,attempt_id,updated_utc,last_error,router_record_id
            FROM vouchers ORDER BY created_at DESC,router_id,service,username
            """;
        return ReadVoucherRows(cmd);
    }

    public IReadOnlyList<Voucher> GetVouchers(int limit = 10000)
    {
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT username,password_protected,profile,batch_id,created_at,
                sent,service,customer,delivery_status,router_id,attempt_id,updated_utc,last_error,router_record_id FROM vouchers
            ORDER BY created_at DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,10000));
        return ReadVoucherRows(cmd);
    }

    public IReadOnlySet<string> ReservedNames(string service = "hotspot",string? routerId=null)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT username FROM vouchers WHERE service=$svc AND ($router IS NULL OR router_id=$router)";
        cmd.Parameters.AddWithValue("$svc", service);
        cmd.Parameters.AddWithValue("$router",(object?)routerId??DBNull.Value);
        using var reader = cmd.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public IReadOnlySet<string> ReservedNamesAcrossServices(string routerId)
    {
        RequireRouter(routerId);using var db=Open();return ReadReservedAcrossServices(db,null,routerId);
    }
    private static HashSet<string> ReadReservedAcrossServices(SqliteConnection db,SqliteTransaction? tx,string routerId)
    {
        using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT username FROM vouchers WHERE router_id=$router OR router_id=''";
        cmd.Parameters.AddWithValue("$router",routerId);
        using var reader=cmd.ExecuteReader();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(reader.Read())names.Add(reader.GetString(0));return names;
    }

    [Obsolete("Use a durable send lease and its exact receipt, or verified reconciliation.")]
    public void MarkSent(IEnumerable<string> usernames,string service="hotspot")=>
        throw new InvalidOperationException("لا يمكن تأكيد إرسال الكروت بالاسم وحده؛ يلزم إيصال مرتبط بالراوتر والمحاولة.");

    /// <summary>
    /// Persist an exclusive pre-send lease before network I/O. A crash must
    /// leave a SENDING row that cannot be silently reissued at next launch.
    /// Only CREATED / explicitly failed requests can enter SENDING.
    /// </summary>
    public Voucher BeginSend(Voucher voucher)
    {
        using var db = Open();
        return BeginSendOnConnection(db, voucher,out _);
    }

    private static Voucher BeginSendOnConnection(SqliteConnection db, Voucher voucher,out string previousState)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.SQLite);
        Validate(voucher);
        RequireRouter(voucher.RouterId);
        var lease=voucher with {AttemptId=Guid.NewGuid().ToString("N"),DeliveryStatus="SENDING",UpdatedUtc=DateTimeOffset.UtcNow.ToString("O")};
        using var tx = db.BeginTransaction();

        using (var read = db.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT password_protected,delivery_status FROM vouchers
                WHERE service=$service AND username=$name AND batch_id=$batch
                  AND customer=$customer AND profile=$profile AND router_id=$router
                """;
            read.Parameters.AddWithValue("$router",voucher.RouterId);
            read.Parameters.AddWithValue("$service", voucher.Service);
            read.Parameters.AddWithValue("$name", voucher.Username);
            read.Parameters.AddWithValue("$batch", voucher.BatchId);
            read.Parameters.AddWithValue("$customer", voucher.Customer);
            read.Parameters.AddWithValue("$profile", voucher.Profile);
            using var reader=read.ExecuteReader();
            if(!reader.Read() || reader.IsDBNull(0))
                throw new InvalidOperationException("بيانات الكرت ليست محفوظة في هذه الدفعة.");
            var coded=reader.GetString(0);
            previousState=reader.GetString(1);
            if(!string.Equals(Unprotect(coded),voucher.Password,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("بيانات الكرت تغيرت أو ليست محفوظة في هذه الدفعة.");
        }

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE vouchers SET delivery_status='SENDING',attempt_id=$attempt,updated_utc=$updated,
                last_error='بدأ الإرسال؛ افحص الراوتر إذا توقف البرنامج'
            WHERE service=$service AND username=$name AND batch_id=$batch
              AND customer=$customer AND profile=$profile AND router_id=$router AND sent=0
              AND delivery_status IN ('CREATED','FAILED','READY')
            """;
        cmd.Parameters.AddWithValue("$router",voucher.RouterId);
        cmd.Parameters.AddWithValue("$attempt",lease.AttemptId);
        cmd.Parameters.AddWithValue("$updated",lease.UpdatedUtc);
        cmd.Parameters.AddWithValue("$service", voucher.Service);
        cmd.Parameters.AddWithValue("$name", voucher.Username);
        cmd.Parameters.AddWithValue("$batch", voucher.BatchId);
        cmd.Parameters.AddWithValue("$customer", voucher.Customer);
        cmd.Parameters.AddWithValue("$profile", voucher.Profile);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                "الكرت مُرسل أو قيد الإرسال أو نتيجته غير مؤكدة؛ لا يمكن إعادة إرساله.");
        WriteDeliveryEvent(db,tx,lease,previousState,"SENDING","بدأت محاولة إرسال محفوظة قبل الاتصال");
        tx.Commit();
        return lease;
    }

    /// <summary>Record the authoritative result; never convert UNKNOWN into retryable FAILED.</summary>
    public void FinishSend(Voucher voucher, string state, string error = "")
    {
        using var db = Open();
        FinishSendOnConnection(db, voucher, state, error);
    }

    private sealed record BatchProgress(long Total,long Successful,long Uncertain,long Failed)
    {
        internal BatchProgress WithOutcome(string state) => new(
            Total,Successful+(state=="SENT"?1:0),
            Uncertain+(state=="UNKNOWN"?1:0),
            Failed+(state=="FAILED"?1:0));
    }

    private static BatchProgress ReadBatchProgress(
        SqliteConnection db,SqliteTransaction? tx,Voucher voucher)
    {
        using var counts=db.CreateCommand();
        counts.Transaction=tx;
        counts.CommandText="""
            SELECT COUNT(*),
                COALESCE(SUM(CASE WHEN delivery_status='SENT' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN delivery_status='UNKNOWN' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN delivery_status='FAILED' THEN 1 ELSE 0 END),0)
            FROM vouchers WHERE service=$svc AND batch_id=$batch AND router_id=$router
            """;
        counts.Parameters.AddWithValue("$router",voucher.RouterId);
        counts.Parameters.AddWithValue("$svc",voucher.Service);
        counts.Parameters.AddWithValue("$batch",voucher.BatchId);
        using var reader=counts.ExecuteReader();
        if(!reader.Read())throw new InvalidDataException("تعذر قراءة حالة دفعة الكروت.");
        return new BatchProgress(reader.GetInt64(0),reader.GetInt64(1),
            reader.GetInt64(2),reader.GetInt64(3));
    }

    private static long ReadDataVersion(SqliteConnection db)
    {
        using var cmd=db.CreateCommand();
        cmd.CommandText="PRAGMA data_version";
        return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
    }

    private static BatchProgress FinishSendOnConnection(
        SqliteConnection db, Voucher voucher, string state, string error,
        BatchProgress? cached=null)
    {
        using var measurement=VoucherPipelineOperation.Measure(VoucherPipelineStage.SQLite);
        ArgumentNullException.ThrowIfNull(voucher);
        RequireRouter(voucher.RouterId);
        if(string.IsNullOrEmpty(voucher.AttemptId))throw new InvalidOperationException("إيصال الإرسال لا يطابق محاولة محفوظة.");
        if (state is not ("SENT" or "FAILED" or "UNKNOWN"))
            throw new ArgumentException("حالة تسليم الكرت غير صالحة.", nameof(state));
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE vouchers SET delivery_status=$state, last_error=$error,updated_utc=$updated,router_record_id=$remote,
                sent=CASE WHEN $state='SENT' THEN 1 ELSE 0 END
            WHERE service=$service AND username=$name AND batch_id=$batch
              AND customer=$customer AND profile=$profile AND router_id=$router
              AND delivery_status='SENDING' AND attempt_id=$attempt
            """;
        cmd.Parameters.AddWithValue("$router",voucher.RouterId);
        cmd.Parameters.AddWithValue("$attempt",voucher.AttemptId);
        cmd.Parameters.AddWithValue("$updated",DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$remote",voucher.RouterRecordId);
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$error", error.Length > 1000 ? error[..1000] : error);
        cmd.Parameters.AddWithValue("$service", voucher.Service);
        cmd.Parameters.AddWithValue("$name", voucher.Username);
        cmd.Parameters.AddWithValue("$batch", voucher.BatchId);
        cmd.Parameters.AddWithValue("$customer", voucher.Customer);
        cmd.Parameters.AddWithValue("$profile", voucher.Profile);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                "تعذر مطابقة كرت قيد الإرسال؛ توقفت العملية لحماية سجل الراوتر.");
        // The fast batch session keeps an aggregate in memory after the first
        // authoritative read. It still commits this audit together with EACH
        // voucher receipt. Direct one-off calls always re-count from SQLite.
        // PRAGMA data_version invalidates the session cache if another writer
        // changes the database between vouchers.
        var progress=cached is null
            ?ReadBatchProgress(db,tx,voucher):cached.WithOutcome(state);
        if(progress.Successful+progress.Uncertain+progress.Failed>progress.Total)
            throw new InvalidDataException("عداد الدفعة غير متطابق مع الكروت المحفوظة.");
        string outcome=progress.Uncertain>0?"ERROR":
            progress.Successful==progress.Total?"SUCCESS":
            progress.Failed>0?"PARTIAL":"INFO";
        WriteActivity(db,tx,ActivityBatchKey(voucher.Service,voucher.BatchId,voucher.RouterId),
            "ROUTER","إرسال دفعة كروت",outcome,
            "خدمة "+voucher.Service+" | ناجح "+progress.Successful+
            " من "+progress.Total+" | فاشل "+progress.Failed+
            " | غير مؤكد "+progress.Uncertain);
        WriteDeliveryEvent(db,tx,voucher,"SENDING",state,"نتيجة محاولة الإرسال");
        tx.Commit();
        return progress;
    }

    /// <summary>
    /// Keep one WAL connection for a whole issuance batch, but use independent
    /// synchronous=FULL transactions before and after EVERY RouterOS write.
    /// Never group SENDING leases or acknowledgements into a late batch commit:
    /// a crash must not make an already-issued voucher retryable.
    /// </summary>
    public SendSession OpenSendSession() => new(this);

    public sealed class SendSession : IDisposable
    {
        private readonly SqliteConnection _db;
        private BatchProgress? _progress;
        private string? _service;
        private string? _batchId;
        private string? _routerId;
        private long _dataVersion=-1;

        internal SendSession(VoucherStore store) => _db = store.Open();

        public Voucher BeginSend(Voucher voucher)
        {
            // A manual retry may legitimately move FAILED -> SENDING.
            // Drop the fast summary so that a formerly failed row is not
            // counted twice when its next receipt becomes SENT.
            var lease=BeginSendOnConnection(_db,voucher,out var previous);
            if(previous=="FAILED")_progress=null;
            return lease;
        }

        public void FinishSend(Voucher voucher, string state, string error = "")
        {
            ArgumentNullException.ThrowIfNull(voucher);
            // SQLite's data_version changes for commits from *other*
            // connections. Never trust a cached summary after a concurrent
            // process changes card delivery states.
            var currentVersion=ReadDataVersion(_db);
            if(_progress is null || _service!=voucher.Service ||
                _batchId!=voucher.BatchId || _routerId!=voucher.RouterId || _dataVersion!=currentVersion)
            {
                _progress=ReadBatchProgress(_db,null,voucher);
                _service=voucher.Service;
                _batchId=voucher.BatchId;
                _routerId=voucher.RouterId;
                _dataVersion=currentVersion;
            }
            // Only advance the in-memory counters AFTER the SQL commit.
            // On any failed transaction the caller stops; the old cache does
            // not mistakenly confirm a card or mark it printable.
            _progress=FinishSendOnConnection(_db,voucher,state,error,_progress);
        }

        public void Dispose() => _db.Dispose();
    }

    public string Backup()
    {
        string directory = Path.Combine(DirectoryPath, "Backups");
        Directory.CreateDirectory(directory);
        // Never overwrite an earlier backup when the button is clicked twice
        // in the same second. Keep the filename sortable and unique.
        string backup = Path.Combine(directory, "NajmCardNew-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        using var original = Open();
        using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder
            {DataSource=backup,Pooling=false}.ToString());
        snapshot.Open();
        original.BackupDatabase(snapshot);
        return backup;
    }

}
