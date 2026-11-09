using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OmUpdate {
    // The active pointer is switched only after a complete, verified extraction.
    // A pending pointer survives process death and rolls back before any DLL loads.
    public sealed class ContentStore {
        public readonly string Root;
        public ContentStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
        string Pointer(string name) => Path.Combine(Root, name);
        public string Current => Read("current");
        public string Previous => Read("previous");
        public bool Pending => File.Exists(Pointer("pending"));

        string Read(string name) {
            string path = Pointer(name);
            if (!File.Exists(path)) return null;
            string id = File.ReadAllText(path).Trim();
            return ContentManifest.IsHash(id) && Directory.Exists(Path.Combine(Root, id)) ? id : null;
        }
        public string DirectoryFor(string id) {
            if (!ContentManifest.IsHash(id)) throw new InvalidDataException("Invalid content identifier");
            return Path.Combine(Root, id);
        }
        public static void AtomicWrite(string path, string text) {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        public void Recover() {
            if (!Pending) return;
            var previous = Previous;
            if (previous != null) AtomicWrite(Pointer("current"), previous);
            else if (File.Exists(Pointer("current"))) File.Delete(Pointer("current"));
            File.Delete(Pointer("pending"));
        }
        public void Activate(string id) {
            if (!Directory.Exists(DirectoryFor(id))) throw new DirectoryNotFoundException(id);
            string current = Current;
            if (id == current) return;
            AtomicWrite(Pointer("previous"), current ?? "");
            // Write intent BEFORE current. A crash at either write is recoverable.
            AtomicWrite(Pointer("pending"), id);
            AtomicWrite(Pointer("current"), id);
        }
        public void Confirm() { if (Pending) File.Delete(Pointer("pending")); }
        public void Prune() {
            var keep = new[] { Current, Previous };
            foreach (var path in Directory.GetDirectories(Root)) {
                string id = Path.GetFileName(path);
                if (ContentManifest.IsHash(id) && !keep.Contains(id)) Directory.Delete(path, true);
            }
        }
        public void UseEmbedded() {
            if (File.Exists(Pointer("current"))) File.Delete(Pointer("current"));
            if (File.Exists(Pointer("pending"))) File.Delete(Pointer("pending"));
        }
        public void Extract(string zip, ContentManifest manifest, Action<float> progress = null) {
            if (new FileInfo(zip).Length != manifest.sizeBytes || ContentManifest.HashFile(zip,
                progress == null ? (Action<long, long>)null : (read, size) => progress(.2f * read / Math.Max(1, size))) != manifest.sha256)
                throw new InvalidDataException("内容包大小或校验值不符。");
            string destination = DirectoryFor(manifest.sha256);
            string stage = destination + ".extracting";
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);
            try {
                long total = 0, written = 0;
                var buffer = new byte[131072];
                using (var archive = ZipFile.OpenRead(zip)) {
                    if (archive.Entries.Count > 50000) throw new InvalidDataException("内容文件数量异常。");
                    foreach (var entry in archive.Entries) {
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                        string output = SafePath(stage, entry.FullName);
                        // Reject Unix symlinks rather than creating ambiguous entries.
                        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                            throw new InvalidDataException("内容包包含链接。");
                        total = checked(total + entry.Length);
                        if (total > manifest.unpackedBytes) throw new InvalidDataException("解压大小超出清单。");
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        using (var source = entry.Open())
                        using (var target = new FileStream(output, FileMode.CreateNew)) {
                            int count;
                            while ((count = source.Read(buffer, 0, buffer.Length)) != 0) {
                                target.Write(buffer, 0, count);
                                written += count;
                                progress?.Invoke(.2f + .8f * written / Math.Max(1, manifest.unpackedBytes));
                            }
                        }
                    }
                }
                if (total != manifest.unpackedBytes) throw new InvalidDataException("解压内容不完整。");
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                Directory.Move(stage, destination);
            } catch {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                throw;
            }
        }
        public static string SafePath(string root, string relative) {
            if (string.IsNullOrEmpty(relative) || relative.Contains("\\") || relative.Contains(":")
                || relative.Split('/').Any(x => x == ".." || x == ".") || Path.IsPathRooted(relative))
                throw new InvalidDataException("非法内容路径。");
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(fullRoot, StringComparison.Ordinal)) throw new InvalidDataException("非法内容路径。");
            return full;
        }
        public static void VerifyIndex(string directory, ContentIndex index, bool hashFiles = true, Action<float> progress = null) {
            if (index == null || index.files == null || index.files.Length > 50000) throw new InvalidDataException("内容索引无效。");
            long total = index.files.Sum(f => f.size), verified = 0;
            foreach (var file in index.files) {
                string path = SafePath(directory, file.path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.size || (hashFiles && ContentManifest.HashFile(path,
                    progress == null ? (Action<long, long>)null : (read, size) => progress((float)(verified + read) / Math.Max(1, total))) != file.sha256))
                    throw new InvalidDataException("内容文件损坏：" + file.path);
                verified += file.size;
            }
            foreach (var required in (index.assemblies ?? Array.Empty<string>()).Select(n => "code/" + n)
                .Concat((index.metadata ?? Array.Empty<string>()).Select(n => "metadata/" + n))
                .Concat(index.bundles ?? Array.Empty<string>())) {
                if (!index.files.Any(f => f.path == required)) throw new InvalidDataException("内容索引缺少文件：" + required);
            }
        }
    }
}
