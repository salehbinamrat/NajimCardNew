using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NajmCard.Desktop.Services;

public sealed partial class VoucherStore
{
    private void EnsureRouterDeliverySchema(SqliteConnection db)
    {
        // Serialize schema inspection as well as DDL across app processes.
        // OS releases the handle on a crash; no stale persisted lock is inferred.
        using var schemaLock=new FileStream(Path.Combine(DirectoryPath,".schema.lock"),
            FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var columns=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        using(var info=db.CreateCommand())
        {
            info.CommandText="PRAGMA table_info(vouchers)";
            using var reader=info.ExecuteReader();
            while(reader.Read())columns.Add(reader.GetString(1),reader.GetInt64(5));
        }
        bool existing=columns.Count>0, migrate=existing&&!columns.ContainsKey("router_id");
        string[] required=["username","password_protected","profile","batch_id","created_at","sent"];
        if(existing&&required.Any(c=>!columns.ContainsKey(c)))
            throw new InvalidDataException("مخطط قاعدة الكروت غير معروف؛ لم تُعدّل البيانات.");
        if(migrate)
        {
            var known=required.Concat(["service","customer","delivery_status","last_error"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(columns.Keys.Any(c=>!known.Contains(c)))
                throw new InvalidDataException("توجد أعمدة غير معروفة؛ أُوقفت الترقية لحماية البيانات.");
            var backups=Path.Combine(DirectoryPath,"Backups");Directory.CreateDirectory(backups);
            using var backup=new SqliteConnection($"Data Source={Path.Combine(backups,"before-router-v2-"+Guid.NewGuid().ToString("N")+".db")};Pooling=False");
            backup.Open();db.BackupDatabase(backup);
        }
        if(existing&&!migrate&&(!columns.TryGetValue("router_id",out var key)||key!=1||
            columns.GetValueOrDefault("service")!=2||columns.GetValueOrDefault("username")!=3||
            !columns.ContainsKey("attempt_id")||!columns.ContainsKey("updated_utc")||
            !columns.ContainsKey("router_record_id")))
            throw new InvalidDataException("ترقية هوية الراوتر غير مكتملة؛ لم تُعدّل البيانات.");

        using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand();cmd.Transaction=tx;
        void Execute(string sql){cmd.CommandText=sql;cmd.ExecuteNonQuery();}
        if(!existing||migrate)
        {
            Execute("""
                CREATE TABLE vouchers_router_v2(
                    router_id TEXT NOT NULL DEFAULT '',
                    service TEXT NOT NULL COLLATE NOCASE DEFAULT 'hotspot',
                    username TEXT NOT NULL COLLATE NOCASE,
                    customer TEXT NOT NULL DEFAULT '',
                    password_protected TEXT NOT NULL,
                    profile TEXT NOT NULL,batch_id TEXT NOT NULL,created_at TEXT NOT NULL,
                    sent INTEGER NOT NULL DEFAULT 0,
                    delivery_status TEXT NOT NULL DEFAULT 'CREATED',last_error TEXT NOT NULL DEFAULT '',
                    attempt_id TEXT NOT NULL DEFAULT '',updated_utc TEXT NOT NULL DEFAULT '',
                    router_record_id TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY(router_id,service,username)
                );
                """);
            if(migrate)
            {
                string Source(string name,string fallback)=>columns.ContainsKey(name)?$"COALESCE({name},{fallback})":fallback;
                Execute($"""
                    INSERT INTO vouchers_router_v2(service,username,customer,password_protected,
                        profile,batch_id,created_at,sent,delivery_status,last_error)
                    SELECT {Source("service","'hotspot'")},username,{Source("customer","''")},password_protected,
                        profile,batch_id,created_at,sent,
                        {Source("delivery_status","CASE WHEN sent=1 THEN 'SENT' ELSE 'CREATED' END")},
                        {Source("last_error","''")} FROM vouchers;
                    DROP TABLE vouchers;
                    """);
            }
            Execute("ALTER TABLE vouchers_router_v2 RENAME TO vouchers;");
        }
        // Historical print snapshots are router scoped too. Their JSON and
        // encrypted voucher payloads are copied verbatim, without decryption.
        bool hasPrint=false, scopedPrint=false;
        using(var print=db.CreateCommand())
        {
            print.Transaction=tx;print.CommandText="PRAGMA table_info(card_batch_print_snapshots)";
            using var reader=print.ExecuteReader();
            while(reader.Read()){hasPrint=true;if(reader.GetString(1)=="router_id")scopedPrint=true;}
        }
        if(hasPrint&&!scopedPrint)
        {
            Execute("""
                CREATE TABLE card_batch_print_v2(router_id TEXT NOT NULL DEFAULT '',
                    service TEXT NOT NULL COLLATE NOCASE,batch_id TEXT NOT NULL,json TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,PRIMARY KEY(router_id,service,batch_id));
                INSERT INTO card_batch_print_v2(service,batch_id,json,updated_utc)
                    SELECT service,batch_id,json,updated_utc FROM card_batch_print_snapshots;
                DROP TABLE card_batch_print_snapshots;
                ALTER TABLE card_batch_print_v2 RENAME TO card_batch_print_snapshots;
                """);
        }
        Execute("""
            CREATE TABLE IF NOT EXISTS voucher_batches(router_id TEXT NOT NULL,service TEXT NOT NULL COLLATE NOCASE,
                batch_id TEXT NOT NULL,created_utc TEXT NOT NULL,PRIMARY KEY(router_id,service,batch_id));
            INSERT OR IGNORE INTO voucher_batches SELECT router_id,service,batch_id,MIN(created_at)
                FROM vouchers GROUP BY router_id,service,batch_id;
            CREATE TABLE IF NOT EXISTS voucher_delivery_events(id INTEGER PRIMARY KEY AUTOINCREMENT,
                router_id TEXT NOT NULL,service TEXT NOT NULL,batch_id TEXT NOT NULL,username TEXT NOT NULL,
                previous_state TEXT NOT NULL,state TEXT NOT NULL,at_utc TEXT NOT NULL,
                attempt_id TEXT NOT NULL,reason TEXT NOT NULL,router_record_id TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_delivery_events_batch
                ON voucher_delivery_events(router_id,service,batch_id,id);
            CREATE INDEX IF NOT EXISTS idx_vouchers_batch ON vouchers(batch_id);
            CREATE INDEX IF NOT EXISTS idx_vouchers_router_batch
                ON vouchers(router_id,service,batch_id,delivery_status,sent);
            CREATE TABLE IF NOT EXISTS najm_schema_migrations(version INTEGER PRIMARY KEY,applied_utc TEXT NOT NULL);
            """);
        if(migrate)
            Execute("""
                INSERT INTO voucher_delivery_events(router_id,service,batch_id,username,
                    previous_state,state,at_utc,attempt_id,reason,router_record_id)
                SELECT '',service,batch_id,username,'',delivery_status,strftime('%Y-%m-%dT%H:%M:%fZ','now'),
                    '','ترقية سجل قديم: ملكية الراوتر وتوقيت الإرسال الأصلي غير معروفين','' FROM vouchers;
                """);
        cmd.CommandText="INSERT OR IGNORE INTO najm_schema_migrations VALUES(2,$now)";
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();tx.Commit();
    }
}
