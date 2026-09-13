using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nuventra.NuvexaDB.Native;

/// <summary>C ABI. No exceptions cross this boundary. Strings are UTF-8; free them with <c>nuvexa_free</c>.</summary>
public static unsafe class NuvexaExports
{
    [UnmanagedCallersOnly(EntryPoint = "nuvexa_create", CallConvs = [typeof(CallConvCdecl)])]
    public static int Create(byte* path, byte* key, nint* handle)
    {
        if (handle == null)
        {
            return NuvexaAbi.Error;
        }

        *handle = 0;
        var status = NuvexaAbi.Create(Read(path), Read(key), out var created);
        *handle = created;
        return status;
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_open", CallConvs = [typeof(CallConvCdecl)])]
    public static int Open(byte* path, byte* key, nint* handle)
    {
        if (handle == null)
        {
            return NuvexaAbi.Error;
        }

        *handle = 0;
        var status = NuvexaAbi.Open(Read(path), Read(key), out var opened);
        *handle = opened;
        return status;
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_close", CallConvs = [typeof(CallConvCdecl)])]
    public static int Close(nint handle) => NuvexaAbi.Close(handle);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_is_encrypted", CallConvs = [typeof(CallConvCdecl)])]
    public static int IsEncrypted(byte* path, int* encrypted)
    {
        if (encrypted == null)
        {
            return NuvexaAbi.Error;
        }

        *encrypted = 0;
        var status = NuvexaAbi.IsEncrypted(Read(path), out var flag);
        *encrypted = flag;
        return status;
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_insert", CallConvs = [typeof(CallConvCdecl)])]
    public static int Insert(nint handle, byte* collection, byte* json, byte** idOut)
    {
        var status = NuvexaAbi.Insert(handle, Read(collection), Read(json), out var id);
        return Write(status, id, idOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_replace", CallConvs = [typeof(CallConvCdecl)])]
    public static int Replace(nint handle, byte* collection, byte* json) =>
        NuvexaAbi.Replace(handle, Read(collection), Read(json));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_delete_by_id", CallConvs = [typeof(CallConvCdecl)])]
    public static int DeleteById(nint handle, byte* collection, byte* id, int* deleted)
    {
        if (deleted == null)
        {
            return NuvexaAbi.Error;
        }

        *deleted = 0;
        var status = NuvexaAbi.DeleteById(handle, Read(collection), Read(id), out var flag);
        *deleted = flag;
        return status;
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_find_by_id", CallConvs = [typeof(CallConvCdecl)])]
    public static int FindById(nint handle, byte* collection, byte* id, byte** jsonOut)
    {
        var status = NuvexaAbi.FindById(handle, Read(collection), Read(id), out var json);
        return Write(status, json, jsonOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_execute", CallConvs = [typeof(CallConvCdecl)])]
    public static int Execute(nint handle, byte* nql, byte** jsonOut)
    {
        var status = NuvexaAbi.Execute(handle, Read(nql), out var json);
        return Write(status, json, jsonOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_ensure_index", CallConvs = [typeof(CallConvCdecl)])]
    public static int EnsureIndex(nint handle, byte* collection, byte* fieldsJson) =>
        NuvexaAbi.EnsureIndex(handle, Read(collection), Read(fieldsJson));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_abi_version", CallConvs = [typeof(CallConvCdecl)])]
    public static int AbiVersion() => NuvexaAbi.AbiVersion;

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_insert_many", CallConvs = [typeof(CallConvCdecl)])]
    public static int InsertMany(nint handle, byte* collection, byte* jsonArray, byte** idsOut)
    {
        var status = NuvexaAbi.InsertMany(handle, Read(collection), Read(jsonArray), out var ids);
        return Write(status, ids, idsOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_list_collections", CallConvs = [typeof(CallConvCdecl)])]
    public static int ListCollections(nint handle, byte** jsonOut)
    {
        var status = NuvexaAbi.ListCollections(handle, out var json);
        return Write(status, json, jsonOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_drop_collection", CallConvs = [typeof(CallConvCdecl)])]
    public static int DropCollection(nint handle, byte* collection) =>
        NuvexaAbi.DropCollection(handle, Read(collection));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_rename_collection", CallConvs = [typeof(CallConvCdecl)])]
    public static int RenameCollection(nint handle, byte* from, byte* to) =>
        NuvexaAbi.RenameCollection(handle, Read(from), Read(to));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_list_indexes", CallConvs = [typeof(CallConvCdecl)])]
    public static int ListIndexes(nint handle, byte* collection, byte** jsonOut)
    {
        var status = NuvexaAbi.ListIndexes(handle, Read(collection), out var json);
        return Write(status, json, jsonOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_drop_index", CallConvs = [typeof(CallConvCdecl)])]
    public static int DropIndex(nint handle, byte* collection, byte* name) =>
        NuvexaAbi.DropIndex(handle, Read(collection), Read(name));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_count", CallConvs = [typeof(CallConvCdecl)])]
    public static int Count(nint handle, byte* collection, byte* filterJson, long* count)
    {
        if (count == null)
        {
            return NuvexaAbi.Error;
        }

        *count = 0;
        var status = NuvexaAbi.Count(handle, Read(collection), Read(filterJson), out var n);
        *count = n;
        return status;
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_checkpoint", CallConvs = [typeof(CallConvCdecl)])]
    public static int Checkpoint(nint handle) => NuvexaAbi.Checkpoint(handle);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_backup", CallConvs = [typeof(CallConvCdecl)])]
    public static int Backup(nint handle, byte* destPath) =>
        NuvexaAbi.Backup(handle, Read(destPath));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_compact", CallConvs = [typeof(CallConvCdecl)])]
    public static int Compact(nint handle) => NuvexaAbi.Compact(handle);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_restore", CallConvs = [typeof(CallConvCdecl)])]
    public static int Restore(byte* backupPath, byte* destPath, int overwrite) =>
        NuvexaAbi.Restore(Read(backupPath), Read(destPath), overwrite);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_stats", CallConvs = [typeof(CallConvCdecl)])]
    public static int Stats(nint handle, byte** jsonOut)
    {
        var status = NuvexaAbi.Stats(handle, out var json);
        return Write(status, json, jsonOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_change_encryption_key", CallConvs = [typeof(CallConvCdecl)])]
    public static int ChangeEncryptionKey(nint handle, byte* currentKey, byte* nextKey) =>
        NuvexaAbi.ChangeEncryptionKey(handle, Read(currentKey), Read(nextKey));

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_begin_transaction", CallConvs = [typeof(CallConvCdecl)])]
    public static int BeginTransaction(nint handle) => NuvexaAbi.BeginTransaction(handle);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_commit", CallConvs = [typeof(CallConvCdecl)])]
    public static int Commit(nint handle) => NuvexaAbi.Commit(handle);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_rollback", CallConvs = [typeof(CallConvCdecl)])]
    public static int Rollback(nint handle) => NuvexaAbi.Rollback(handle);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_fs_upload", CallConvs = [typeof(CallConvCdecl)])]
    public static int FsUpload(nint handle, byte* fileName, byte* sourcePath, int chunkSize, byte** idOut)
    {
        var status = NuvexaAbi.FsUpload(handle, Read(fileName), Read(sourcePath), chunkSize, out var id);
        return Write(status, id, idOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_fs_download", CallConvs = [typeof(CallConvCdecl)])]
    public static int FsDownload(nint handle, byte* fileId, byte* destPath, int* found)
    {
        if (found == null)
        {
            return NuvexaAbi.Error;
        }

        *found = 0;
        var status = NuvexaAbi.FsDownload(handle, Read(fileId), Read(destPath), out var flag);
        *found = flag;
        return status;
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_fs_metadata", CallConvs = [typeof(CallConvCdecl)])]
    public static int FsMetadata(nint handle, byte* fileId, byte** jsonOut)
    {
        var status = NuvexaAbi.FsMetadata(handle, Read(fileId), out var json);
        return Write(status, json, jsonOut);
    }

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static int LastError(byte** message) => Write(NuvexaAbi.Ok, NuvexaAbi.LastError, message);

    [UnmanagedCallersOnly(EntryPoint = "nuvexa_free", CallConvs = [typeof(CallConvCdecl)])]
    public static void Free(byte* pointer)
    {
        if (pointer != null)
        {
            Marshal.FreeCoTaskMem((nint)pointer);
        }
    }

    private static string? Read(byte* pointer) =>
        pointer == null ? null : Marshal.PtrToStringUTF8((nint)pointer);

    private static int Write(int status, string? value, byte** destination)
    {
        if (destination == null)
        {
            return status == NuvexaAbi.Ok && value is not null ? NuvexaAbi.Error : status;
        }

        *destination = value is null ? null : (byte*)Marshal.StringToCoTaskMemUTF8(value);
        return status;
    }
}
