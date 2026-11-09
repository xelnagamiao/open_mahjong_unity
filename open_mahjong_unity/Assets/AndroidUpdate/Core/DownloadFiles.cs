using System;
using System.IO;

namespace OmUpdate {
    // A received segment is retained across process death. An explicit offset
    // makes merging idempotent even if the process stops during the copy itself.
    public sealed class DownloadFiles {
        public readonly string FilePath;
        public string Piece => FilePath + ".receiving";
        string OffsetFile => Piece + ".offset";
        readonly long size;
        public DownloadFiles(string file, string identity, long expectedSize) {
            FilePath = file; size = expectedSize;
            string marker = file + ".identity";
            if (!System.IO.File.Exists(marker) || System.IO.File.ReadAllText(marker) != identity) {
                Delete(file); Delete(Piece); Delete(OffsetFile);
                ContentStore.AtomicWrite(marker, identity);
            }
            Merge();
            if (Length > size) Delete(file);
        }
        public long Length => System.IO.File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
        public bool RecordResponse(long responseCode, string contentRange, long requestedOffset) {
            long offset;
            if (responseCode == 200) offset = 0;
            else if (responseCode == 206 && (contentRange ?? "").StartsWith("bytes " + requestedOffset + "-", StringComparison.Ordinal)
                && contentRange.EndsWith("/" + size, StringComparison.Ordinal)) offset = requestedOffset;
            else return false;
            ContentStore.AtomicWrite(OffsetFile, offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        public void Merge() {
            if (!System.IO.File.Exists(Piece)) { Delete(OffsetFile); return; }
            long offset;
            if (!System.IO.File.Exists(OffsetFile) || !long.TryParse(System.IO.File.ReadAllText(OffsetFile), out offset)
                || offset < 0 || offset > size || new FileInfo(Piece).Length > size - offset
                || (offset > 0 && Length < offset)) {
                Delete(Piece); Delete(OffsetFile); return;
            }
            using (var source = System.IO.File.OpenRead(Piece))
            using (var target = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Write)) {
                target.SetLength(offset); target.Position = offset; source.CopyTo(target); target.Flush(true);
            }
            Delete(Piece); Delete(OffsetFile);
        }
        static void Delete(string path) { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
