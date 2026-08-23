using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MirrorStudio.Database
{
    /// <summary>
    /// Minimal SQLite access via the Windows-bundled winsqlite3.dll (no NuGet dependency).
    /// </summary>
    public sealed class LiteDb : IDisposable
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int OPEN_READWRITE = 0x2;
        private const int OPEN_CREATE = 0x4;

        private IntPtr _db;

        public LiteDb(string path)
        {
            var flags = OPEN_READWRITE | OPEN_CREATE;
            var rc = sqlite3_open_v2(Utf8Z(path), out _db, flags, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                string msg = _db != IntPtr.Zero ? ErrMsg() : $"sqlite error {rc}";
                if (_db != IntPtr.Zero) sqlite3_close_v2(_db);
                _db = IntPtr.Zero;
                throw new InvalidOperationException($"Cannot open database '{path}': {msg}");
            }
            Exec("PRAGMA foreign_keys=ON;");
        }

        public bool IsOpen => _db != IntPtr.Zero;

        // ---------- low level ----------

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
        private static byte[] Utf8Z(string s)
        {
            var b = new byte[Encoding.UTF8.GetByteCount(s) + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, b, 0);
            return b;
        }

        private string ErrMsg()
        {
            var p = sqlite3_errmsg(_db);
            return p == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringUTF8(p) ?? "unknown error";
        }

        public void Exec(string sql)
        {
            var rc = sqlite3_exec(_db, Utf8Z(sql), IntPtr.Zero, IntPtr.Zero, out var errPtr);
            if (rc != SQLITE_OK)
            {
                string msg = errPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(errPtr) ?? "" : ErrMsg();
                if (errPtr != IntPtr.Zero) sqlite3_free(errPtr);
                throw new InvalidOperationException($"SQL error: {msg}\n{sql}");
            }
        }

        /// <summary>Runs a parameterized statement; returns affected row count.</summary>
        public int Execute(string sql, params object?[] args)
        {
            using var stmt = Prepare(sql, args);
            StepOrFail(stmt);
            return sqlite3_changes(_db);
        }

        /// <summary>Returns all result rows keyed by column name.</summary>
        public List<Dictionary<string, object?>> Query(string sql, params object?[] args)
        {
            var rows = new List<Dictionary<string, object?>>();
            using var stmt = Prepare(sql, args);
            var h = stmt.Handle;

            int n = sqlite3_column_count(h);
            var names = new string[n];
            for (int i = 0; i < n; i++)
                names[i] = Marshal.PtrToStringUTF8(sqlite3_column_name(h, i)) ?? $"col{i}";

            while (sqlite3_step(h) == SQLITE_ROW)
            {
                var row = new Dictionary<string, object?>(n);
                for (int i = 0; i < n; i++)
                {
                    object? val = null;
                    switch (sqlite3_column_type(h, i))
                    {
                        case 1: val = sqlite3_column_int64(h, i); break;   // INTEGER
                        case 2: val = sqlite3_column_double(h, i); break;  // FLOAT
                        case 3: val = sqlite3_column_int64(h, i); break;   // stored numeric text
                        case 4:
                            var p = sqlite3_column_text(h, i);
                            val = p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
                            break;                                         // TEXT
                        case 5:                                            // BLOB
                            int len = sqlite3_column_bytes(h, i);
                            var bytes = new byte[len];
                            Marshal.Copy(sqlite3_column_blob(h, i), bytes, 0, len);
                            val = bytes;
                            break;
                    }
                    row[names[i]] = val;
                }
                rows.Add(row);
            }
            return rows;
        }

        public long ScalarLong(string sql, params object?[] args)
        {
            using var stmt = Prepare(sql, args);
            return sqlite3_step(stmt.Handle) == SQLITE_ROW ? sqlite3_column_int64(stmt.Handle, 0) : 0;
        }

        public long LastInsertRowId => sqlite3_last_insert_rowid(_db);

        private SqliteStmt Prepare(string sql, object?[] args)
        {
            var rc = sqlite3_prepare_v2(_db, Utf8Z(sql), -1, out var handle, out _);
            if (rc != SQLITE_OK)
                throw new InvalidOperationException($"Prepare failed ({ErrMsg()}): {sql}");

            var stmt = new SqliteStmt(handle);
            for (int i = 0; i < args.Length; i++)
            {
                int idx = i + 1;
                var a = args[i];
                switch (a)
                {
                    case null: sqlite3_bind_null(handle, idx); break;
                    case int v: sqlite3_bind_int64(handle, idx, v); break;
                    case long v: sqlite3_bind_int64(handle, idx, v); break;
                    case bool v: sqlite3_bind_int64(handle, idx, v ? 1 : 0); break;
                    case double v: sqlite3_bind_double(handle, idx, v); break;
                    case float v: sqlite3_bind_double(handle, idx, v); break;
                    case decimal v: sqlite3_bind_double(handle, idx, (double)v); break;
                    case DateTime v: BindText(handle, idx, v.ToString("o")); break;
                    case Guid v: BindText(handle, idx, v.ToString()); break;
                    default: BindText(handle, idx, a.ToString() ?? ""); break;
                }
            }
            return stmt;
        }

        private static void BindText(IntPtr h, int idx, string s)
        {
            var rc = sqlite3_bind_text(h, idx, Utf8(s), Encoding.UTF8.GetByteCount(s), new IntPtr(-1));
            if (rc != SQLITE_OK) throw new InvalidOperationException($"bind_text failed rc={rc}");
        }

        private void StepOrFail(SqliteStmt stmt)
        {
            int rc = sqlite3_step(stmt.Handle);
            if (rc != SQLITE_DONE && rc != SQLITE_ROW)
                throw new InvalidOperationException($"Execute failed ({ErrMsg()}): {sqlOf(stmt)}");
        }

        private string sqlOf(SqliteStmt s) => "";

        private sealed class SqliteStmt : IDisposable
        {
            public IntPtr Handle { get; }
            public SqliteStmt(IntPtr h) => Handle = h;
            public void Dispose()
            {
                if (Handle != IntPtr.Zero) { sqlite3_finalize(Handle); }
            }
        }

        public void Dispose()
        {
            if (_db != IntPtr.Zero) { sqlite3_close_v2(_db); _db = IntPtr.Zero; }
        }

        // ---------- native imports ----------

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close_v2(IntPtr db);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr ptr);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, out IntPtr tail);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_reset(IntPtr stmt);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int byteLen, IntPtr destructor);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_null(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_name(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_type(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern double sqlite3_column_double(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int index);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_changes(IntPtr db);

        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_last_insert_rowid(IntPtr db);
    }
}
