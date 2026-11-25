using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ApkShellext2 {
    /// <summary>The two content hashes of one file.</summary>
    public sealed class FileHash {
        public FileHash(string sha256, string xxh3) {
            Sha256 = sha256 ?? "";
            Xxh3 = xxh3 ?? "";
        }

        /// <summary>The SHA-256 digest as 64 lowercase hexadecimal characters.</summary>
        public string Sha256 { get; private set; }

        /// <summary>The XXH3-64 digest as 16 lowercase hexadecimal characters.</summary>
        public string Xxh3 { get; private set; }
    }

    /// <summary>
    /// Hashes a file without loading it into memory. Both digests are computed in one
    /// pass with a fixed buffer, and the file is opened for shared reading so Explorer
    /// can keep its own handle open.
    /// </summary>
    public static class FileHasher {
        private const int BufferSize = 64 * 1024;

        /// <summary>Computes the SHA-256 and XXH3-64 digests of <paramref name="path"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        /// <exception cref="IOException">The file cannot be opened or read.</exception>
        /// <exception cref="UnauthorizedAccessException">The file cannot be opened.</exception>
        public static FileHash Compute(string path) {
            if (path == null) throw new ArgumentNullException("path");
            var buffer = new byte[BufferSize];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.SequentialScan))
            using (var sha256 = SHA256.Create()) {
                Xxh3 xxh3 = Xxh3.Create();
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    xxh3.Append(buffer, 0, read);
                }
                sha256.TransformFinalBlock(buffer, 0, 0);
                return new FileHash(ToHex(sha256.Hash), xxh3.Digest().ToString("x16", CultureInfo.InvariantCulture));
            }
        }

        private static string ToHex(byte[] bytes) {
            var text = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
