using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
namespace NajmCard.Desktop.Services;

public sealed partial class VoucherStore
{
    private readonly object _accessGate=new();
    private FileStream? _lifetimeLease;
    private int _activeConnections;
    private bool _maintenance,_requiresRestart,_disposed;
    private string LeasePath=>Path.Combine(DirectoryPath,".najm-store-access.lock");
    private FileStream SharedLease()=>new(LeasePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);

    private Action TrackConnection()
    {
        lock(_accessGate)
        {
            if(_requiresRestart)throw new InvalidOperationException("تمت استعادة البيانات؛ أعد تشغيل البرنامج قبل استخدام القوائم أو إرسال الكروت.");
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(_maintenance)throw new IOException("استعادة البيانات جارية؛ انتظر اكتمال العملية.");
            _activeConnections++;
        }
        int released=0;
        return ()=>
        {
            if(Interlocked.Exchange(ref released,1)!=0)return;
            lock(_accessGate)
            {
                _activeConnections--;
                if(_disposed&&_activeConnections==0){_lifetimeLease?.Dispose();_lifetimeLease=null;}
            }
        };
    }

    private FileStream EnterMaintenance()
    {
        lock(_accessGate)
        {
            if(_disposed||_requiresRestart)throw new InvalidOperationException("أعد تشغيل البرنامج قبل الاستعادة.");
            if(_maintenance||_activeConnections!=0)
                throw new IOException("توجد عملية حفظ أو إرسال أو قراءة جارية؛ انتظر اكتمالها قبل الاستعادة.");
            _maintenance=true;
            _lifetimeLease?.Dispose();_lifetimeLease=null;
            try{return new FileStream(LeasePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch
            {
                _maintenance=false;
                try{_lifetimeLease=SharedLease();}catch{_requiresRestart=true;}
                throw;
            }
        }
    }
    private void LeaveMaintenance(FileStream exclusive,bool restored)
    {
        lock(_accessGate)
        {
            _requiresRestart|=restored;
            exclusive.Dispose();
            if(!_requiresRestart)
                try{_lifetimeLease=SharedLease();}catch{_requiresRestart=true;}
            _maintenance=false;
        }
    }
    public void Dispose()
    {
        lock(_accessGate)
        {
            _disposed=true;
            if(_activeConnections==0){_lifetimeLease?.Dispose();_lifetimeLease=null;}
        }
    }
}
