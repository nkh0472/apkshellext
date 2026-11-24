using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using ApkQuickReader;
using ApkShellext2.Diagnostics;
using ApkShellext2.Interop;
using ApkShellext2.Registration;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace ApkShellext2.Shell.Native {
    /// <summary>
    /// The thumbnail handler: the shell initializes it with the item path (<c>IInitializeWithFile</c>,
    /// loaded in process) or with the item's bytes as an <c>IStream</c> (<c>IInitializeWithStream</c>,
    /// the initialisation an out-of-process load is given), and <c>IThumbnailProvider</c> produces the
    /// bitmap.
    ///
    /// The stream is copied to a private temporary file before rendering, because the renderer opens
    /// the package by path. The copy is bounded by the container limit the readers already apply, the
    /// directory is created on demand, and the file name carries a GUID so two handler instances never
    /// share a file. The copy is deleted when the render returns, and a finalizer deletes it when the
    /// shell initializes the handler and never asks for a bitmap. A stale sweep on Initialize removes
    /// copies older than a day that no live handler owns any more, so an abandoned handler cannot
    /// leave payload-sized files in the temp directory for ever.
    ///
    /// The bitmap is returned as an HBITMAP that the shell owns and frees with DeleteObject, so it
    /// must be a copy the handler never touches again. A request with no thumbnail answers S_FALSE
    /// with a null handle, which is how the shell is told to fall back to the item's icon.
    /// </summary>
    [ComVisible(true)]
    [Guid("D747C5A7-2F66-4B7D-8301-8531838E4ED3")]
    [ProgId(ShellGuids.ThumbnailProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ThumbnailHandlerNative : IThumbnailProviderNative, IInitializeWithFileNative, IInitializeWithStreamNative {
        private const string ModuleName = "ThumbnailHandlerNative";
        private const string TemporaryDirectoryName = "ApkShellext2";
        private const uint WtsatArgb = 2;
        private const int CopyBufferSize = 81920;
        private const int StatFlagNoName = 1;
        private const int StaleCopyHours = 24;

        private string selectedPath;
        private string temporaryPath;

        /// <summary>
        /// Deletes a copy the shell initialized but never rendered. The finalizer is the only caller
        /// that is not on a thread the shell owns, so it swallows everything: a finalizer that throws
        /// ends the process.
        /// </summary>
        ~ThumbnailHandlerNative() {
            try {
                DeleteTemporaryFile();
            } catch {
            }
        }

        public int Initialize(string pszFilePath, uint grfMode) {
            try {
                SweepStaleCopies();
                DeleteTemporaryFile();
                selectedPath = pszFilePath;
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "Initialize failed", ex);
                return HResult.E_FAIL;
            }
        }

        public int Initialize(IStream pstream, uint grfMode) {
            try {
                if (pstream == null) {
                    return HResult.E_INVALIDARG;
                }
                SweepStaleCopies();
                DeleteTemporaryFile();
                selectedPath = null;

                long declared = DeclaredLength(pstream);
                if (declared > SafeArchive.MAX_BASE_APK) {
                    ErrorLog.Warn(ModuleName,
                        "Thumbnail stream declares " + declared + " bytes, more than the " + SafeArchive.MAX_BASE_APK + "-byte container limit; refused without copying",
                        null);
                    return HResult.E_FILE_TOO_LARGE;
                }

                string path = TemporaryFilePath();
                try {
                    int hr = CopyStreamTo(pstream, path);
                    if (hr != HResult.S_OK) {
                        TryDelete(path);
                        return hr;
                    }
                } catch {
                    TryDelete(path);
                    throw;
                }

                temporaryPath = path;
                selectedPath = path;
                return HResult.S_OK;
            } catch (Exception ex) {
                ErrorLog.Error(ModuleName, "Initialize from stream failed", ex);
                return HResult.E_FAIL;
            }
        }

        public int GetThumbnail(uint cx, out IntPtr phbmp, out uint pdwAlpha) {
            phbmp = IntPtr.Zero;
            pdwAlpha = 0;
            try {
                if (string.IsNullOrEmpty(selectedPath) || !File.Exists(selectedPath))
                    return HResult.S_FALSE;
                if (cx == 0)
                    return HResult.S_FALSE;

                // The shell names the size it wants; anything beyond the icon decode budget is
                // refused rather than allocating a bitmap of that size.
                try {
                    IconDecodeLimits.ValidateRenderTarget(new Size((int)cx, (int)cx), "thumbnail");
                } catch (InvalidDataException ex) {
                    ErrorLog.Warn(ModuleName, "Thumbnail size " + cx + " is refused: " + ex.Message, ex);
                    return HResult.S_FALSE;
                }

                Bitmap thumbnail = ThumbnailRenderer.Render(selectedPath, cx, ModuleName);
                if (thumbnail == null)
                    return HResult.S_FALSE;

                phbmp = thumbnail.GetHbitmap();
                pdwAlpha = WtsatArgb;
                return HResult.S_OK;
            } catch (Exception ex) {
                phbmp = IntPtr.Zero;
                pdwAlpha = 0;
                ErrorLog.Error(ModuleName, "GetThumbnail failed for " + Path.GetFileName(selectedPath ?? string.Empty), ex);
                return HResult.S_FALSE;
            } finally {
                // The renderer has opened and closed the copy by the time a bitmap exists or the
                // call has failed; keeping it longer would leave attacker-sized files behind.
                DeleteTemporaryFile();
            }
        }

        /// <summary>
        /// Reads the declared size without taking the name (STATFLAG_NONAME): no string is allocated,
        /// so there is nothing to free. A stream that will not answer is copied anyway and the copy
        /// stops at the limit.
        /// </summary>
        private static long DeclaredLength(IStream pstream) {
            try {
                STATSTG stat;
                pstream.Stat(out stat, StatFlagNoName);
                return stat.cbSize;
            } catch (NotSupportedException) {
                return 0;
            } catch (COMException) {
                return 0;
            }
        }

        private static string TemporaryFilePath() {
            string directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryName);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "thumb-" + Guid.NewGuid().ToString("N") + ".apk");
        }

        /// <summary>
        /// Copies at most the container limit. A stream whose declared size lies is stopped at the
        /// limit as well, so the copy is never unbounded whatever the caller said.
        /// </summary>
        private static int CopyStreamTo(IStream pstream, string path) {
            long total = 0;
            IntPtr read = Marshal.AllocHGlobal(sizeof(int));
            try {
                using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize)) {
                    byte[] buffer = new byte[CopyBufferSize];
                    while (true) {
                        Marshal.WriteInt32(read, 0);
                        pstream.Read(buffer, buffer.Length, read);
                        int count = Marshal.ReadInt32(read);
                        if (count < 0 || count > buffer.Length) {
                            throw new InvalidDataException("IStream.Read reported " + count + " bytes for a " + buffer.Length + "-byte buffer");
                        }
                        if (count == 0) {
                            return HResult.S_OK;
                        }
                        total += count;
                        if (total > SafeArchive.MAX_BASE_APK) {
                            ErrorLog.Warn(ModuleName,
                                "Thumbnail stream is larger than the " + SafeArchive.MAX_BASE_APK + "-byte container limit; copy stopped",
                                null);
                            return HResult.E_FILE_TOO_LARGE;
                        }
                        file.Write(buffer, 0, count);
                    }
                }
            } finally {
                Marshal.FreeHGlobal(read);
            }
        }

        /// <summary>
        /// Deletes the copy and forgets it. Interlocked.Exchange makes the two callers (the render
        /// path and the finalizer) agree on who deletes, so a path is deleted at most once and a
        /// second call on the same instance is a no-op that throws nothing.
        /// </summary>
        private void DeleteTemporaryFile() {
            string path = Interlocked.Exchange(ref temporaryPath, null);
            if (path == null) {
                return;
            }
            try {
                File.Delete(path);
            } catch (IOException ex) {
                ErrorLog.Warn(ModuleName, "Could not delete the temporary thumbnail " + path + ": " + ex.Message, ex);
            } catch (UnauthorizedAccessException ex) {
                ErrorLog.Warn(ModuleName, "Could not delete the temporary thumbnail " + path + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Removes copies left behind by handlers that were initialized and never asked for a bitmap
        /// (the shell may keep such an instance alive, or drop it without a finalizer run). Only files
        /// older than <see cref="StaleCopyHours"/> hours go: a copy another live instance is still
        /// rendering is younger than that. Best effort, like the bucket sweep of any cache.
        /// </summary>
        private static void SweepStaleCopies() {
            try {
                string directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryName);
                if (!Directory.Exists(directory)) {
                    return;
                }
                DateTime limit = DateTime.UtcNow.AddHours(-StaleCopyHours);
                foreach (string file in Directory.GetFiles(directory, "thumb-*.apk")) {
                    try {
                        if (File.GetLastWriteTimeUtc(file) < limit) {
                            File.Delete(file);
                        }
                    } catch (IOException) {
                    } catch (UnauthorizedAccessException) {
                    }
                }
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }

        private static void TryDelete(string path) {
            try {
                File.Delete(path);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }

        /// <summary>
        /// regasm runs this once for every class it registers. The batch entry point writes every
        /// handler in the assembly, so one run registers the whole product, not just this class.
        /// </summary>
        [ComRegisterFunction]
        public static void Register(Type serverType) {
            ShellRegistrar.RegisterFromComRegistration(serverType);
        }

        [ComUnregisterFunction]
        public static void Unregister(Type serverType) {
            ShellRegistrar.UnregisterFromComUnregistration(serverType);
        }
    }
}
