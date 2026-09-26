using System.Globalization;
using System.Runtime.InteropServices;

namespace Mp3Organizer;

// Uses the SQLite engine shipped with Windows; no NuGet restore/native download is required.
public sealed class SqliteDatabase : IDisposable
{
    private IntPtr db;
    public SqliteDatabase(string path)
    {
        var rc=Native.sqlite3_open_v2(path,out db,2|4|0x10000,IntPtr.Zero);
        if(rc!=0) {var error=Error();Dispose();throw new IOException("SQLite open: "+error);}
        Native.sqlite3_busy_timeout(db,5000);
    }
    public List<string?[]> Query(string sql,params object?[] values)
    {
        Check(Native.sqlite3_prepare_v2(db,sql,-1,out var statement,IntPtr.Zero));
        try
        {
            for(var i=0;i<values.Length;i++)
                Check(values[i]==null?Native.sqlite3_bind_null(statement,i+1):Native.sqlite3_bind_text(statement,i+1,Convert.ToString(values[i],CultureInfo.InvariantCulture)!,-1,new IntPtr(-1)));
            var rows=new List<string?[]>();int rc;
            while((rc=Native.sqlite3_step(statement))==100)
            {
                var row=new string?[Native.sqlite3_column_count(statement)];
                for(var i=0;i<row.Length;i++) row[i]=Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement,i));
                rows.Add(row);
            }
            if(rc!=101)Check(rc);
            return rows;
        }
        finally {Native.sqlite3_finalize(statement);}
    }
    private string Error()=>Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(db))??"unknown error";
    private void Check(int result) {if(result!=0)throw new IOException("SQLite: "+Error()+" ("+result+")");}
    public void Dispose() {if(db!=IntPtr.Zero){Native.sqlite3_close_v2(db);db=IntPtr.Zero;}}
    private static class Native
    {
        private const string Library="winsqlite3.dll";
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db,int ms);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int length,out IntPtr statement,IntPtr tail);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)] string text,int length,IntPtr destructor);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_bind_null(IntPtr statement,int index);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_column_count(IntPtr statement);
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
    }
}
