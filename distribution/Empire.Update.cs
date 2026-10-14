// SPDX-License-Identifier: GPL-3.0-or-later
// .NET Framework 4.8, available on supported Windows 10/11 installations.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Empire
{
    public sealed class FileRecord
    {
        public string path { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
    }

    public sealed class Asset
    {
        public string url { get; set; }
        public long size { get; set; }
        public string sha256 { get; set; }
    }

    public sealed class Manifest
    {
        public int schema { get; set; }
        public string repository { get; set; }
        public string version { get; set; }
        public string previous { get; set; }
        public Asset full { get; set; }
        public Asset delta { get; set; }
        public List<FileRecord> files { get; set; }
    }

    public sealed class State
    {
        public string version { get; set; }
        public string previous { get; set; }
    }

    public sealed class Updater
    {
        public const string Repository = "colonelchaboumm/OpenRA-Empire";
        public const string DownloadRoot = "https://github.com/" + Repository + "/releases/download/";
        public const string LatestUrl = "https://github.com/" + Repository + "/releases/latest/download/empire-manifest.json";
        public readonly string Root;
        readonly Action<string, string, long> download;
        readonly Action<string> progress;

        public Updater(string root, Action<string, string, long> transport = null, Action<string> report = null)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            download = transport ?? Download;
            progress = report ?? delegate { };
        }

        static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16777216, RecursionLimit = 32 };
        }

        public static T ReadJson<T>(string path)
        {
            return Serializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void WriteJson(string path, object value)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Serializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static Version ParseVersion(string version)
        {
            Version result;
            if (version == null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") || !Version.TryParse(version, out result))
                throw new InvalidDataException("Numero de version invalide.");
            return result;
        }

        public static string SafePath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith("/"))
                throw new InvalidDataException("Chemin de fichier refuse.");
            foreach (var part in relative.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || Regex.IsMatch(part, "[<>\"|?*\\x00-\\x1f]") ||
                    part.EndsWith(".") || part.EndsWith(" ") || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Chemin de fichier refuse : " + relative);
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chemin hors installation.");
            for (var ancestor = path; ancestor != null && ancestor.Length >= prefix.Length - 1; ancestor = Path.GetDirectoryName(ancestor))
                if ((File.Exists(ancestor) || Directory.Exists(ancestor)) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Lien de fichiers refuse : " + ancestor);
            return path;
        }

        public State ReadState()
        {
            var state = ReadJson<State>(SafePath(Root, "current.json"));
            ParseVersion(state.version);
            if (state.previous != null) ParseVersion(state.previous);
            return state;
        }

        public static void ValidateManifest(Manifest manifest)
        {
            if (manifest == null || manifest.schema != 1 || manifest.repository != Repository)
                throw new InvalidDataException("Format de mise a jour inconnu.");
            var version = ParseVersion(manifest.version);
            if (manifest.previous != null && ParseVersion(manifest.previous) >= version)
                throw new InvalidDataException("Historique de versions invalide.");
            if (manifest.files == null || manifest.files.Count == 0 || manifest.files.Count > 50000)
                throw new InvalidDataException("Liste de fichiers invalide.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.files)
            {
                if (file == null) throw new InvalidDataException("Fichier absent.");
                SafePath(Path.GetTempPath(), file.path);
                if (!seen.Add(file.path) || file.path.Equals("release-manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(file.path, @"^(Support|UserData|versions)(/|$)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Fichier reserve ou duplique.");
                if (!HashValid(file.sha256) || file.size < 0 || file.size > 2147483648L)
                    throw new InvalidDataException("Empreinte de fichier invalide.");
            }
            foreach (var required in new[] { "OpenRA.exe", "mods/medieval/mod.yaml", "updater/Empire.Launcher.exe" })
                if (!seen.Contains(required)) throw new InvalidDataException("Fichier indispensable absent : " + required);
            foreach (var path in seen)
                for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                    if (seen.Contains(path.Substring(0, slash)))
                        throw new InvalidDataException("Un chemin ne peut pas etre un fichier et un dossier : " + path);
            foreach (var asset in new[] { manifest.full, manifest.delta })
            {
                if (asset == null) continue;
                var prefix = DownloadRoot + "v" + manifest.version + "/";
                if (asset.url == null || !asset.url.StartsWith(prefix, StringComparison.Ordinal) ||
                    !Regex.IsMatch(asset.url.Substring(prefix.Length), @"^[a-zA-Z0-9._-]+\.zip$") ||
                    !HashValid(asset.sha256) || asset.size <= 0 || asset.size > 2147483648L)
                    throw new InvalidDataException("Archive de mise a jour invalide.");
            }
            if (manifest.full == null) throw new InvalidDataException("Archive complete absente.");
        }

        static bool HashValid(string hash) { return hash != null && Regex.IsMatch(hash, "^[a-f0-9]{64}$"); }

        public static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static bool VerifyFile(string path, long size, string hash)
        {
            return File.Exists(path) && new FileInfo(path).Length == size && Hash(path) == hash;
        }

        public static void Download(string url, string destination, long limit)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "OpenRA-Empire-Updater/1.0";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.ContentLength > limit) throw new InvalidDataException("Telechargement trop volumineux.");
                using (var input = response.GetResponseStream())
                using (var output = File.Create(destination))
                {
                    var buffer = new byte[131072];
                    long total = 0;
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > limit) throw new InvalidDataException("Telechargement trop volumineux.");
                        output.Write(buffer, 0, count);
                    }
                }
            }
        }

        public Manifest FetchManifest(string version = null)
        {
            if (version != null) ParseVersion(version);
            var url = version == null ? LatestUrl : DownloadRoot + "v" + version + "/empire-manifest.json";
            var temp = Path.GetTempFileName();
            try
            {
                download(url, temp, 16777216);
                var manifest = ReadJson<Manifest>(temp);
                ValidateManifest(manifest);
                if (version != null && manifest.version != version) throw new InvalidDataException("Version telechargee inattendue.");
                return manifest;
            }
            finally { File.Delete(temp); }
        }

        public static void VerifyInstallation(string directory, Manifest manifest)
        {
            foreach (var file in manifest.files)
                if (!VerifyFile(SafePath(directory, file.path), file.size, file.sha256))
                    throw new InvalidDataException("Fichier endommage : " + file.path);
        }

        public static void Extract(string archive, string directory, Manifest manifest)
        {
            var allowed = manifest.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(archive))
                foreach (var entry in zip.Entries)
                {
                    var destination = SafePath(directory, entry.FullName);
                    FileRecord file;
                    if (!allowed.TryGetValue(entry.FullName, out file) || !seen.Add(entry.FullName) || entry.Length != file.size)
                        throw new InvalidDataException("Entree ZIP inattendue : " + entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    // Bound the uncompressed bytes too, even when ZIP metadata is malformed.
                    using (var input = entry.Open())
                    using (var output = File.Create(destination))
                    {
                        var buffer = new byte[131072];
                        long total = 0;
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += count;
                            if (total > file.size) throw new InvalidDataException("Entree ZIP trop volumineuse.");
                            output.Write(buffer, 0, count);
                        }
                    }
                    if (!VerifyFile(destination, file.size, file.sha256)) throw new InvalidDataException("Fichier telecharge endommage.");
                }
        }

        public State Install(Manifest latest)
        {
            ValidateManifest(latest);
            var state = ReadState();
            if (ParseVersion(latest.version) <= ParseVersion(state.version)) return state;
            var chain = new List<Manifest>();
            var cursor = latest;
            bool useDelta = false;
            for (var i = 0; i < 30; i++)
            {
                chain.Insert(0, cursor);
                if (cursor.delta == null || cursor.previous == null) break;
                if (cursor.previous == state.version) { useDelta = true; break; }
                if (ParseVersion(cursor.previous) < ParseVersion(state.version)) break;
                try { cursor = FetchManifest(cursor.previous); }
                catch (Exception e)
                {
                    if (!(e is WebException || e is IOException || e is InvalidDataException || e is ArgumentException)) throw;
                    break;
                }
            }
            var current = SafePath(Root, "versions/" + state.version);
            Manifest old = null;
            if (useDelta)
            {
                try
                {
                    progress("Verification de la version installee...");
                    old = ReadJson<Manifest>(SafePath(current, "release-manifest.json"));
                    ValidateManifest(old);
                    if (old.version != state.version) throw new InvalidDataException("Version locale incoherente.");
                    VerifyInstallation(current, old);
                }
                catch (Exception e)
                {
                    if (!(e is IOException || e is InvalidDataException || e is ArgumentException || e is InvalidOperationException)) throw;
                    useDelta = false;
                }
            }
            if (!useDelta || chain.Sum(m => m.delta == null ? 0 : m.delta.size) >= latest.full.size)
            {
                useDelta = false;
                chain = new List<Manifest> { latest };
            }
            var id = Guid.NewGuid().ToString("N");
            var stageRelative = "versions/.staging-" + id;
            var stage = SafePath(Root, stageRelative);
            var archive = SafePath(Root, "versions/.download-" + id + ".zip");
            Directory.CreateDirectory(stage);
            try
            {
                if (useDelta)
                    foreach (var file in old.files)
                    {
                        var target = SafePath(stage, file.path);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(SafePath(current, file.path), target);
                    }
                foreach (var step in chain)
                {
                    var asset = useDelta ? step.delta : step.full;
                    progress("Telechargement de la version " + step.version + " (" + Math.Ceiling(asset.size / 1048576d) + " Mo)...");
                    download(asset.url, archive, asset.size);
                    if (!VerifyFile(archive, asset.size, asset.sha256)) throw new InvalidDataException("Le telechargement est incomplet ou endommage.");
                    progress("Installation de la version " + step.version + "...");
                    // Prune only the staged copy before extraction: a removed file may now
                    // be a directory, and an obsolete directory may become a file.
                    var allowed = new HashSet<string>(step.files.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
                    foreach (var path in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                    {
                        var relative = path.Substring(stage.Length + 1).Replace('\\', '/');
                        if (!allowed.Contains(relative)) File.Delete(SafePath(stage, relative));
                    }
                    foreach (var path in Directory.GetDirectories(stage, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                    {
                        var relative = path.Substring(stage.Length + 1).Replace('\\', '/');
                        var directory = SafePath(stage, relative);
                        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                    }
                    Extract(archive, stage, step);
                }
                progress("Verification finale des fichiers...");
                VerifyInstallation(stage, latest);
                WriteJson(Path.Combine(stage, "release-manifest.json"), latest);
                var final = SafePath(Root, "versions/" + latest.version);
                if (Directory.Exists(final)) VerifyInstallation(final, latest);
                else Directory.Move(stage, final);
                var updated = new State { version = latest.version, previous = state.version };
                WriteJson(SafePath(Root, "current.json"), updated);
                return updated;
            }
            finally
            {
                if (File.Exists(archive)) File.Delete(archive);
                // Only this call's freshly created staging directory can be removed.
                if (Directory.Exists(stage)) Directory.Delete(SafePath(Root, stageRelative), true);
            }
        }

        public State Rollback()
        {
            var state = ReadState();
            if (state.previous == null) throw new InvalidOperationException("Aucune version precedente disponible.");
            var directory = SafePath(Root, "versions/" + state.previous);
            var manifest = ReadJson<Manifest>(SafePath(directory, "release-manifest.json"));
            ValidateManifest(manifest);
            if (manifest.version != state.previous) throw new InvalidDataException("Version precedente incoherente.");
            VerifyInstallation(directory, manifest);
            var restored = new State { version = state.previous, previous = state.version };
            WriteJson(SafePath(Root, "current.json"), restored);
            return restored;
        }

        public void Play()
        {
            var state = ReadState();
            var directory = SafePath(Root, "versions/" + state.version);
            Process.Start(new ProcessStartInfo
            {
                FileName = SafePath(directory, "OpenRA.exe"), WorkingDirectory = directory,
                Arguments = "-- Game.Mod=medieval Graphics.Mode=Windowed Graphics.WindowedSize=1280,800",
                UseShellExecute = false, CreateNoWindow = true
            });
        }
    }
}
