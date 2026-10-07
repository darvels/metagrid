using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class SteamRemoteStorageGuideService(ILoggingService loggingService) : ISteamRemoteStorageGuideService
{
    private const uint DotaAppId = 570;
    private static readonly object NativeGate = new();
    private int _fileWriteCount;
    public int FileWriteCount => Volatile.Read(ref _fileWriteCount);
    private int _fileDeleteCount;
    public int FileDeleteCount => Volatile.Read(ref _fileDeleteCount);

    public async Task<RemoteGuideDeleteResult> DeleteOwnedFileAsync(SteamAccount account, string remoteFile, string expectedContentHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await Task.Run(() =>
            {
                using var session = new SteamRemoteStorageSession(account);
                cancellationToken.ThrowIfCancellationRequested();
                return session.DeleteOwnedFile(remoteFile, expectedContentHash, () => Interlocked.Increment(ref _fileDeleteCount), cancellationToken);
            }, cancellationToken);
            await loggingService.LogAsync(result.Succeeded ? LogLevelKind.Information : LogLevelKind.Warning,
                "Steam RemoteStorage FileDelete result.", new { account.AccountId, remoteFile, result }, CancellationToken.None);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Steam RemoteStorage deletion failed.", new { account.AccountId, remoteFile, ex.Message }, CancellationToken.None);
            return new(false, true, true, ex.Message);
        }
    }

    public async Task<bool> IsAvailableAsync(SteamAccount account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await Task.Run(() => { using var session = new SteamRemoteStorageSession(account); return session.IsReady; }, cancellationToken);
        }
        catch (Exception ex)
        {
            return await LogAndReturnFalseAsync(account, ex, cancellationToken);
        }
    }

    public Task<IReadOnlyList<RemoteGuideFileEntry>> ListFilesAsync(SteamAccount account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => { using var session = new SteamRemoteStorageSession(account); return session.ListFiles(); }, cancellationToken);
    }

    public Task<byte[]?> ReadFileAsync(SteamAccount account, string remoteFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => { using var session = new SteamRemoteStorageSession(account); return session.ReadFile(remoteFile); }, cancellationToken);
    }

    public async Task<RemoteGuideWriteResult> WriteFileAsync(SteamAccount account, string remoteFile, byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await Task.Run(() =>
            {
                using var session = new SteamRemoteStorageSession(account);
                cancellationToken.ThrowIfCancellationRequested();
                return session.WriteFile(remoteFile, bytes, () => Interlocked.Increment(ref _fileWriteCount));
            }, cancellationToken);
            if (!result.Succeeded)
            {
                await loggingService.LogAsync(LogLevelKind.Warning, "Steam RemoteStorage write failed.", new
                {
                    account.AccountId,
                    remoteFile,
                    result.Error
                }, cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await loggingService.LogAsync(LogLevelKind.Warning, "Steam RemoteStorage write threw an exception.", new
            {
                account.AccountId,
                remoteFile,
                ex.Message
            }, cancellationToken);

            return new RemoteGuideWriteResult
            {
                Succeeded = false,
                RemoteFile = remoteFile,
                Error = ex.Message
            };
        }
    }

    private async Task<bool> LogAndReturnFalseAsync(SteamAccount account, Exception ex, CancellationToken cancellationToken)
    {
        await loggingService.LogAsync(LogLevelKind.Warning, "Steam RemoteStorage was unavailable for guide sync.", new
        {
            account.AccountId,
            ex.Message
        }, cancellationToken);
        return false;
    }

    private sealed class SteamRemoteStorageSession : IDisposable
    {
        private readonly SteamNative _steam;
        private readonly IntPtr _remoteStorage;
        private bool _disposed;
        private bool _initialized;
        private readonly string? _previousAppId;
        private readonly string? _previousGameId;

        public SteamRemoteStorageSession(SteamAccount account)
        {
            Monitor.Enter(NativeGate);
            _previousAppId = Environment.GetEnvironmentVariable("SteamAppId");
            _previousGameId = Environment.GetEnvironmentVariable("SteamGameId");
            try
            {
            // Steam's development appid file lookup uses the process working directory.
            // Supply the explicit session AppID instead of changing the application's cwd.
            Environment.SetEnvironmentVariable("SteamAppId", "570");
            Environment.SetEnvironmentVariable("SteamGameId", "570");
            EnsureSteamAppIdFile();
            _steam = new SteamNative(ResolveLibraryPath(account));
            var init = _steam.SteamAPI_InitFlat(out var errorMessage);
            if (init != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorMessage) ? "SteamAPI_InitFlat failed." : errorMessage);
            }
            _initialized = true;
            var user = _steam.SteamAPI_SteamUser_v023();
            var steamId = _steam.SteamAPI_ISteamUser_GetSteamID(user);
            if (!ulong.TryParse(account.AccountId, out var selected) || steamId != 76561197960265728UL + selected)
                throw new InvalidOperationException($"Active Steam account {steamId & 0xffffffff} does not match selected account {account.AccountId}. No write permitted.");

            _remoteStorage = _steam.SteamAPI_SteamRemoteStorage_v016();
            if (_remoteStorage == IntPtr.Zero)
            {
                throw new InvalidOperationException("Steam RemoteStorage interface was not available.");
            }
            var utils = _steam.SteamAPI_SteamUtils_v010();
            var appId = _steam.SteamAPI_ISteamUtils_GetAppID(utils);
            if (appId != DotaAppId)
            {
                throw new InvalidOperationException($"Steam RemoteStorage initialized with unexpected AppID {appId} instead of 570.");
            }

            if (!_steam.SteamAPI_ISteamRemoteStorage_IsCloudEnabledForAccount(_remoteStorage)
                || !_steam.SteamAPI_ISteamRemoteStorage_IsCloudEnabledForApp(_remoteStorage))
            {
                throw new InvalidOperationException("Steam Cloud for Dota 2 was not enabled for the current account or app.");
            }
            }
            catch
            {
                if (_initialized) _steam?.SteamAPI_Shutdown();
                _steam?.Dispose();
                RestoreAppEnvironment();
                Monitor.Exit(NativeGate);
                throw;
            }
        }

        public bool IsReady => _remoteStorage != IntPtr.Zero;

        public IReadOnlyList<RemoteGuideFileEntry> ListFiles()
        {
            var files = new List<RemoteGuideFileEntry>();
            var count = _steam.SteamAPI_ISteamRemoteStorage_GetFileCount(_remoteStorage);
            for (var index = 0; index < count; index++)
            {
                var pointer = _steam.SteamAPI_ISteamRemoteStorage_GetFileNameAndSize(_remoteStorage, index, out var size);
                var name = Marshal.PtrToStringAnsi(pointer);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                files.Add(new RemoteGuideFileEntry
                {
                    Name = name,
                    Size = size,
                    Exists = _steam.SteamAPI_ISteamRemoteStorage_FileExists(_remoteStorage, name),
                    Persisted = _steam.SteamAPI_ISteamRemoteStorage_FilePersisted(_remoteStorage, name),
                    Timestamp = _steam.SteamAPI_ISteamRemoteStorage_GetFileTimestamp(_remoteStorage, name)
                });
            }

            return files;
        }

        public byte[]? ReadFile(string remoteFile)
        {
            if (!_steam.SteamAPI_ISteamRemoteStorage_FileExists(_remoteStorage, remoteFile))
            {
                return null;
            }

            var size = _steam.SteamAPI_ISteamRemoteStorage_GetFileSize(_remoteStorage, remoteFile);
            if (size <= 0)
            {
                return [];
            }

            var buffer = new byte[size];
            var read = _steam.SteamAPI_ISteamRemoteStorage_FileRead(_remoteStorage, remoteFile, buffer, buffer.Length);
            return read <= 0 ? null : buffer[..read];
        }

        public RemoteGuideWriteResult WriteFile(string remoteFile, byte[] bytes, Action recordWrite)
        {
            if (!remoteFile.StartsWith("guides/", StringComparison.Ordinal) || !remoteFile.EndsWith(".build", StringComparison.Ordinal)
                || remoteFile.Contains("..") || bytes.Length is 0 or > 1024 * 1024)
                throw new InvalidOperationException("Unsafe guide filename or size.");
            var batchStarted = _steam.SteamAPI_ISteamRemoteStorage_BeginFileWriteBatch(_remoteStorage);
            if (!batchStarted) throw new IOException("Steam refused BeginFileWriteBatch; no write performed.");
            recordWrite();
            var writeResult = _steam.SteamAPI_ISteamRemoteStorage_FileWrite(_remoteStorage, remoteFile, bytes, bytes.Length);
            var batchEnded = _steam.SteamAPI_ISteamRemoteStorage_EndFileWriteBatch(_remoteStorage);
            var exists = _steam.SteamAPI_ISteamRemoteStorage_FileExists(_remoteStorage, remoteFile);
            var persisted = _steam.SteamAPI_ISteamRemoteStorage_FilePersisted(_remoteStorage, remoteFile);

            if (!(batchStarted && writeResult && batchEnded && exists && persisted))
            {
                return new RemoteGuideWriteResult
                {
                    Succeeded = false,
                    RemoteFile = remoteFile,
                    BytesWritten = bytes.Length,
                    ExistsAfterWrite = exists,
                    PersistedAfterWrite = persisted,
                    Error = $"BeginBatch={batchStarted}; FileWrite={writeResult}; EndBatch={batchEnded}; Exists={exists}; Persisted={persisted}"
                };
            }

            var readBack = ReadFile(remoteFile);
            return new RemoteGuideWriteResult
            {
                Succeeded = readBack is not null && readBack.AsSpan().SequenceEqual(bytes)
                    && _steam.SteamAPI_ISteamRemoteStorage_GetFileSize(_remoteStorage, remoteFile) == bytes.Length,
                RemoteFile = remoteFile,
                BytesWritten = bytes.Length,
                ExistsAfterWrite = exists,
                PersistedAfterWrite = persisted,
                ReadBackHash = readBack is null ? null : Convert.ToHexString(SHA256.HashData(readBack)),
                Error = readBack is null
                    ? "Read-back after Steam RemoteStorage write returned no bytes."
                    : !readBack.AsSpan().SequenceEqual(bytes)
                        ? "Read-back after Steam RemoteStorage write did not match the bytes that were written."
                        : null
            };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { _steam.SteamAPI_Shutdown(); _steam.Dispose(); }
            finally { RestoreAppEnvironment(); Monitor.Exit(NativeGate); }
        }

        public RemoteGuideDeleteResult DeleteOwnedFile(string remoteFile, string expectedContentHash, Action recordDelete, CancellationToken token)
        {
            if (!GuideDeletionOwnership.IsSafeFile(remoteFile) || expectedContentHash.Length != 64)
                throw new InvalidOperationException("Unsafe owned-guide deletion request.");
            var exists = _steam.SteamAPI_ISteamRemoteStorage_FileExists(_remoteStorage, remoteFile);
            var persisted = _steam.SteamAPI_ISteamRemoteStorage_FilePersisted(_remoteStorage, remoteFile);
            if (!exists) return new(!persisted, false, persisted, persisted ? "Absent file still reports persisted." : null);
            var bytes = ReadFile(remoteFile);
            if (bytes is null || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedContentHash, StringComparison.Ordinal))
                return new(false, exists, persisted, "Guide bytes changed after ownership preflight; no deletion permitted.");
            token.ThrowIfCancellationRequested();
            if (!_steam.SteamAPI_ISteamRemoteStorage_BeginFileWriteBatch(_remoteStorage))
                return new(false, exists, persisted, "Steam refused deletion batch.");
            bool deleted;
            bool ended;
            try
            {
                token.ThrowIfCancellationRequested();
                recordDelete();
                deleted = _steam.SteamAPI_ISteamRemoteStorage_FileDelete(_remoteStorage, remoteFile);
            }
            finally { ended = _steam.SteamAPI_ISteamRemoteStorage_EndFileWriteBatch(_remoteStorage); }
            exists = _steam.SteamAPI_ISteamRemoteStorage_FileExists(_remoteStorage, remoteFile);
            persisted = _steam.SteamAPI_ISteamRemoteStorage_FilePersisted(_remoteStorage, remoteFile);
            return new(deleted && ended && !exists && !persisted, exists, persisted,
                deleted && ended && !exists && !persisted ? null : $"FileDelete={deleted}; EndBatch={ended}; Exists={exists}; Persisted={persisted}");
        }

        private void RestoreAppEnvironment()
        {
            Environment.SetEnvironmentVariable("SteamAppId", _previousAppId);
            Environment.SetEnvironmentVariable("SteamGameId", _previousGameId);
        }

        private static string ResolveLibraryPath(SteamAccount account)
        {
            var candidate = Path.Combine(account.SteamRootPath, "steamapps", "common", "dota 2 beta", "game", "bin", "win64", "steam_api64.dll");
            if (!File.Exists(candidate))
            {
                throw new FileNotFoundException("Could not locate steam_api64.dll for Dota 2.", candidate);
            }

            return candidate;
        }

        private static void EnsureSteamAppIdFile()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, DotaAppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (File.ReadAllText(path).Trim() != "570") throw new InvalidOperationException("Conflicting steam_appid.txt.");
        }
    }

    private sealed class SteamNative : IDisposable
    {
        private readonly IntPtr _moduleHandle;
        private readonly InitFlat _steamApiInitFlat;
        private readonly VoidNoArgs _steamApiShutdown;
        private readonly PtrNoArgs _steamRemoteStorage;
        private readonly PtrNoArgs _steamUtils;
        private readonly PtrNoArgs _steamUser;
        private readonly SteamId _userGetSteamId;
        private readonly GetAppId _utilsGetAppId;
        private readonly BoolInstance _remoteStorageCloudEnabledForAccount;
        private readonly BoolInstance _remoteStorageCloudEnabledForApp;
        private readonly BoolInstance _remoteStorageBeginFileWriteBatch;
        private readonly BoolInstance _remoteStorageEndFileWriteBatch;
        private readonly IntInstance _remoteStorageGetFileCount;
        private readonly GetFileNameAndSize _remoteStorageGetFileNameAndSize;
        private readonly BoolInstanceStringUtf8 _remoteStorageFileExists;
        private readonly BoolInstanceStringUtf8 _remoteStorageFilePersisted;
        private readonly BoolInstanceStringUtf8 _remoteStorageFileDelete;
        private readonly IntInstanceStringUtf8 _remoteStorageGetFileSize;
        private readonly LongInstanceStringUtf8 _remoteStorageGetFileTimestamp;
        private readonly FileReadUtf8 _remoteStorageFileRead;
        private readonly FileWriteUtf8 _remoteStorageFileWrite;

        public SteamNative(string libraryPath)
        {
            _moduleHandle = NativeLibrary.Load(libraryPath);
            _steamApiInitFlat = GetDelegate<InitFlat>("SteamAPI_InitFlat");
            _steamApiShutdown = GetDelegate<VoidNoArgs>("SteamAPI_Shutdown");
            _steamRemoteStorage = GetDelegate<PtrNoArgs>("SteamAPI_SteamRemoteStorage_v016");
            _steamUtils = GetDelegate<PtrNoArgs>("SteamAPI_SteamUtils_v010");
            _steamUser = GetDelegate<PtrNoArgs>("SteamAPI_SteamUser_v023");
            _userGetSteamId = GetDelegate<SteamId>("SteamAPI_ISteamUser_GetSteamID");
            _utilsGetAppId = GetDelegate<GetAppId>("SteamAPI_ISteamUtils_GetAppID");
            _remoteStorageCloudEnabledForAccount = GetDelegate<BoolInstance>("SteamAPI_ISteamRemoteStorage_IsCloudEnabledForAccount");
            _remoteStorageCloudEnabledForApp = GetDelegate<BoolInstance>("SteamAPI_ISteamRemoteStorage_IsCloudEnabledForApp");
            _remoteStorageBeginFileWriteBatch = GetDelegate<BoolInstance>("SteamAPI_ISteamRemoteStorage_BeginFileWriteBatch");
            _remoteStorageEndFileWriteBatch = GetDelegate<BoolInstance>("SteamAPI_ISteamRemoteStorage_EndFileWriteBatch");
            _remoteStorageGetFileCount = GetDelegate<IntInstance>("SteamAPI_ISteamRemoteStorage_GetFileCount");
            _remoteStorageGetFileNameAndSize = GetDelegate<GetFileNameAndSize>("SteamAPI_ISteamRemoteStorage_GetFileNameAndSize");
            _remoteStorageFileExists = GetDelegate<BoolInstanceStringUtf8>("SteamAPI_ISteamRemoteStorage_FileExists");
            _remoteStorageFilePersisted = GetDelegate<BoolInstanceStringUtf8>("SteamAPI_ISteamRemoteStorage_FilePersisted");
            _remoteStorageFileDelete = GetDelegate<BoolInstanceStringUtf8>("SteamAPI_ISteamRemoteStorage_FileDelete");
            _remoteStorageGetFileSize = GetDelegate<IntInstanceStringUtf8>("SteamAPI_ISteamRemoteStorage_GetFileSize");
            _remoteStorageGetFileTimestamp = GetDelegate<LongInstanceStringUtf8>("SteamAPI_ISteamRemoteStorage_GetFileTimestamp");
            _remoteStorageFileRead = GetDelegate<FileReadUtf8>("SteamAPI_ISteamRemoteStorage_FileRead");
            _remoteStorageFileWrite = GetDelegate<FileWriteUtf8>("SteamAPI_ISteamRemoteStorage_FileWrite");
        }

        public int SteamAPI_InitFlat(out string errorMessage)
        {
            var buffer = Marshal.AllocHGlobal(1024);
            try
            {
                Span<byte> zeroes = stackalloc byte[1024];
                zeroes.Clear();
                Marshal.Copy(zeroes.ToArray(), 0, buffer, zeroes.Length);
                var result = _steamApiInitFlat(buffer);
                errorMessage = Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void SteamAPI_Shutdown() => _steamApiShutdown();
        public IntPtr SteamAPI_SteamRemoteStorage_v016() => _steamRemoteStorage();
        public IntPtr SteamAPI_SteamUtils_v010() => _steamUtils();
        public IntPtr SteamAPI_SteamUser_v023() => _steamUser();
        public ulong SteamAPI_ISteamUser_GetSteamID(IntPtr user) => _userGetSteamId(user);
        public uint SteamAPI_ISteamUtils_GetAppID(IntPtr instancePtr) => _utilsGetAppId(instancePtr);
        public bool SteamAPI_ISteamRemoteStorage_IsCloudEnabledForAccount(IntPtr instancePtr) => _remoteStorageCloudEnabledForAccount(instancePtr);
        public bool SteamAPI_ISteamRemoteStorage_IsCloudEnabledForApp(IntPtr instancePtr) => _remoteStorageCloudEnabledForApp(instancePtr);
        public bool SteamAPI_ISteamRemoteStorage_BeginFileWriteBatch(IntPtr instancePtr) => _remoteStorageBeginFileWriteBatch(instancePtr);
        public bool SteamAPI_ISteamRemoteStorage_EndFileWriteBatch(IntPtr instancePtr) => _remoteStorageEndFileWriteBatch(instancePtr);
        public int SteamAPI_ISteamRemoteStorage_GetFileCount(IntPtr instancePtr) => _remoteStorageGetFileCount(instancePtr);
        public IntPtr SteamAPI_ISteamRemoteStorage_GetFileNameAndSize(IntPtr instancePtr, int file, out int sizeInBytes) => _remoteStorageGetFileNameAndSize(instancePtr, file, out sizeInBytes);
        public bool SteamAPI_ISteamRemoteStorage_FileExists(IntPtr instancePtr, string fileName) => _remoteStorageFileExists(instancePtr, fileName);
        public bool SteamAPI_ISteamRemoteStorage_FilePersisted(IntPtr instancePtr, string fileName) => _remoteStorageFilePersisted(instancePtr, fileName);
        public bool SteamAPI_ISteamRemoteStorage_FileDelete(IntPtr instancePtr, string fileName) => _remoteStorageFileDelete(instancePtr, fileName);
        public int SteamAPI_ISteamRemoteStorage_GetFileSize(IntPtr instancePtr, string fileName) => _remoteStorageGetFileSize(instancePtr, fileName);
        public long SteamAPI_ISteamRemoteStorage_GetFileTimestamp(IntPtr instancePtr, string fileName) => _remoteStorageGetFileTimestamp(instancePtr, fileName);
        public int SteamAPI_ISteamRemoteStorage_FileRead(IntPtr instancePtr, string fileName, byte[] buffer, int bytesToRead) => _remoteStorageFileRead(instancePtr, fileName, buffer, bytesToRead);
        public bool SteamAPI_ISteamRemoteStorage_FileWrite(IntPtr instancePtr, string fileName, byte[] buffer, int bytesToWrite) => _remoteStorageFileWrite(instancePtr, fileName, buffer, bytesToWrite);

        public void Dispose()
        {
            if (_moduleHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_moduleHandle);
            }
        }

        private T GetDelegate<T>(string exportName) where T : Delegate
        {
            var export = NativeLibrary.GetExport(_moduleHandle, exportName);
            return Marshal.GetDelegateForFunctionPointer<T>(export);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitFlat(IntPtr errorMessageBuffer);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidNoArgs();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PtrNoArgs();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong SteamId(IntPtr instancePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool BoolInstance(IntPtr instancePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int IntInstance(IntPtr instancePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetAppId(IntPtr instancePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetFileNameAndSize(IntPtr instancePtr, int file, out int sizeInBytes);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool BoolInstanceStringUtf8(IntPtr instancePtr, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int IntInstanceStringUtf8(IntPtr instancePtr, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate long LongInstanceStringUtf8(IntPtr instancePtr, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int FileReadUtf8(IntPtr instancePtr, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, byte[] buffer, int bytesToRead);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool FileWriteUtf8(IntPtr instancePtr, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, byte[] buffer, int bytesToWrite);
    }
}
