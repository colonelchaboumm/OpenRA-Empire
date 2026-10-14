// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Empire;

static class EmpireTests
{
    static int checks;
    static string root;
    static readonly Dictionary<string, string> assets = new Dictionary<string, string>();
    static readonly List<string> requests = new List<string>();
    static string interrupted;
    static string corrupted;
    static string stable;

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("ECHEC : " + message);
        checks++;
        Console.WriteLine("OK : " + message);
    }

    static void Fails(Action action, string message)
    {
        var failed = false;
        try { action(); } catch { failed = true; }
        Assert(failed, message);
    }

    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content);
    }

    static void Zip(string source, string destination, IEnumerable<string> paths)
    {
        using (var zip = ZipFile.Open(destination, ZipArchiveMode.Create))
            foreach (var path in paths) zip.CreateEntryFromFile(Path.Combine(source, path), path);
    }

    static Asset Asset(string path, string version)
    {
        var url = Updater.DownloadRoot + "v" + version + "/" + Path.GetFileName(path);
        assets[url] = path;
        return new Asset { url = url, size = new FileInfo(path).Length, sha256 = Updater.Hash(path) };
    }

    static Manifest Fixture(string version, Manifest previous, Dictionary<string, string> extra = null)
    {
        var directory = Path.Combine(root, "fixture-" + version);
        Write(Path.Combine(directory, "OpenRA.exe"), "fixture executable");
        Write(Path.Combine(directory, "mods/medieval/mod.yaml"), "Version: " + version);
        Write(Path.Combine(directory, "updater/Empire.Launcher.exe"), "fixture launcher");
        Write(Path.Combine(directory, "stable.dat"), stable);
        Write(Path.Combine(directory, version == "1.0.0" ? "obsolete.dat" : "new.dat"), version);
        if (version == "1.0.0") Write(Path.Combine(directory, "legacy/payload.dat"), "old directory");
        if (extra != null)
            foreach (var file in extra) Write(Path.Combine(directory, file.Key), file.Value);
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(path => new FileRecord
        {
            path = path.Substring(directory.Length + 1).Replace('\\', '/'), size = new FileInfo(path).Length, sha256 = Updater.Hash(path)
        }).ToList();
        var full = Path.Combine(root, "full-" + version + ".zip");
        Zip(directory, full, files.Select(f => f.path));
        Asset delta = null;
        if (previous != null)
        {
            var old = previous.files.ToDictionary(f => f.path, f => f.sha256);
            var changed = files.Where(f => !old.ContainsKey(f.path) || old[f.path] != f.sha256).Select(f => f.path);
            var patch = Path.Combine(root, "patch-" + version + ".zip");
            Zip(directory, patch, changed);
            delta = Asset(patch, version);
        }
        var manifest = new Manifest { schema = 1, repository = Updater.Repository, version = version,
            previous = previous == null ? null : previous.version, full = Asset(full, version), delta = delta, files = files };
        var manifestPath = Path.Combine(root, "manifest-" + version + ".json");
        Updater.WriteJson(manifestPath, manifest);
        assets[Updater.DownloadRoot + "v" + version + "/empire-manifest.json"] = manifestPath;
        Updater.WriteJson(Path.Combine(directory, "release-manifest.json"), manifest);
        return manifest;
    }

    static Manifest Copy(Manifest manifest)
    {
        return Updater.ReadJson<Manifest>(assets[Updater.DownloadRoot + "v" + manifest.version + "/empire-manifest.json"]);
    }

    static void Transport(string url, string destination, long limit)
    {
        requests.Add(url);
        if (url == interrupted)
        {
            File.WriteAllText(destination, "partial download");
            throw new IOException("Simulated interruption");
        }
        if (!assets.ContainsKey(url)) throw new IOException("Unknown fixture " + url);
        File.Copy(assets[url], destination, true);
        if (url == corrupted)
        {
            var data = File.ReadAllBytes(destination);
            data[10] ^= 1;
            File.WriteAllBytes(destination, data);
        }
    }

    static Updater Installation(string name)
    {
        var installation = Path.Combine(root, name);
        var source = Path.Combine(root, "fixture-1.0.0");
        var target = Path.Combine(installation, "versions/1.0.0");
        foreach (var path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, path.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(path, dest);
        }
        Updater.WriteJson(Path.Combine(installation, "current.json"), new State { version = "1.0.0" });
        Write(Path.Combine(installation, "UserData/save.sav"), "personal save");
        return new Updater(installation, Transport);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--render")
            {
                Application.EnableVisualStyles();
                var install = Path.GetFullPath(args[1]);
                var state = new Updater(install).ReadState();
                var assembly = Assembly.LoadFrom(Path.Combine(install, "versions/" + state.version + "/updater/Empire.Launcher.exe"));
                using (var form = (Form)Activator.CreateInstance(assembly.GetType("Empire.Launcher"), new object[] { install }))
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.Show();
                    Application.DoEvents();
                    foreach (Control control in form.Controls)
                        if (control.Right > form.ClientSize.Width || control.Bottom > form.ClientSize.Height)
                            throw new Exception("Element hors fenetre : " + control.Text);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(args[2]);
                    }
                    form.Hide();
                }
                Console.WriteLine("Lanceur rendu et limites de fenetre verifiees.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--verify")
            {
                var updater = new Updater(args[1]);
                var state = updater.ReadState();
                var directory = Updater.SafePath(updater.Root, "versions/" + state.version);
                var manifest = Updater.ReadJson<Manifest>(Path.Combine(directory, "release-manifest.json"));
                Updater.ValidateManifest(manifest);
                Updater.VerifyInstallation(directory, manifest);
                Console.WriteLine("Installation verifiee : " + state.version + ", " + manifest.files.Count + " fichiers.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--remote-install")
            {
                var destination = Path.GetFullPath(args[1]);
                if (Directory.Exists(destination)) throw new Exception("Le dossier de test doit etre nouveau.");
                Directory.CreateDirectory(destination);
                Updater.WriteJson(Path.Combine(destination, "current.json"), new State { version = "0.0.0" });
                var updater = new Updater(destination, null, Console.WriteLine);
                var manifest = updater.FetchManifest();
                var state = updater.Install(manifest);
                Console.WriteLine("Installation depuis GitHub verifiee : " + state.version);
                return 0;
            }
            root = Path.Combine(Path.GetFullPath(args.Length == 1 ? args[0] : ".distribution"), "updater-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { RunTests(); }
            finally
            {
                // Only the unique fixture folder created above, never a caller-supplied tree.
                if (Path.GetFileName(root).StartsWith("updater-tests-")) Directory.Delete(root, true);
            }
            Console.WriteLine("\n" + checks + " controles reussis.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void RunTests()
    {
        var bytes = new byte[131072];
        new Random(123).NextBytes(bytes);
        stable = Convert.ToBase64String(bytes);
        var one = Fixture("1.0.0", null);
        var two = Fixture("1.0.1", one);
        var three = Fixture("1.0.2", two);
        var updater = Installation("direct");
        var state = updater.Install(two);
        Assert(state.version == "1.0.1", "activation du patch");
        Assert(requests.Contains(two.delta.url) && !requests.Contains(two.full.url), "seul le patch est telecharge");
        Assert(!File.Exists(Path.Combine(updater.Root, "versions/1.0.1/obsolete.dat")), "fichiers obsoletes retires");
        Assert(File.Exists(Path.Combine(updater.Root, "versions/1.0.0/obsolete.dat")), "ancienne version intacte");
        Assert(File.ReadAllText(Path.Combine(updater.Root, "UserData/save.sav")) == "personal save", "donnees personnelles conservees");
        state = updater.Rollback();
        Assert(state.version == "1.0.0" && state.previous == "1.0.1", "retour a la version precedente");
        updater.Install(two);
        Assert(updater.ReadState().version == "1.0.1", "reinstallation apres retour arriere");

        updater = Installation("skipped");
        requests.Clear();
        updater.Install(three);
        Assert(updater.ReadState().version == "1.0.2", "mise a jour apres version sautee");
        Assert(requests.Contains(two.delta.url) && requests.Contains(three.delta.url) && !requests.Contains(three.full.url), "chaine de patches sans archive complete");
        var count = requests.Count;
        updater.Install(two);
        Assert(requests.Count == count && updater.ReadState().version == "1.0.2", "aucune retrogradation automatique");

        updater = Installation("interrupted");
        interrupted = two.delta.url;
        Fails(() => updater.Install(two), "telechargement interrompu refuse");
        Assert(updater.ReadState().version == "1.0.0", "interruption sans changement de version active");
        Assert(!Directory.GetFileSystemEntries(Path.Combine(updater.Root, "versions")).Any(p => Path.GetFileName(p).StartsWith(".")), "fichiers temporaires nettoyes apres erreur");
        interrupted = null;
        corrupted = two.delta.url;
        Fails(() => updater.Install(two), "archive corrompue refusee");
        Assert(updater.ReadState().version == "1.0.0", "empreinte incorrecte sans modification du jeu");
        corrupted = null;

        updater = Installation("damaged");
        File.WriteAllText(Path.Combine(updater.Root, "versions/1.0.0/stable.dat"), "damaged");
        requests.Clear();
        updater.Install(two);
        Assert(requests.Contains(two.full.url), "installation endommagee reparee avec archive complete");
        Updater.VerifyInstallation(Path.Combine(updater.Root, "versions/1.0.1"), two);

        updater = Installation("missing-history");
        interrupted = Updater.DownloadRoot + "v1.0.1/empire-manifest.json";
        requests.Clear();
        updater.Install(three);
        Assert(requests.Contains(three.full.url), "historique indisponible : archive complete");
        interrupted = null;

        updater = Installation("activation-failure");
        using (var locked = File.Open(Path.Combine(updater.Root, "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Fails(() => updater.Install(two), "activation impossible sans ecraser le pointeur actif");
        Assert(updater.ReadState().version == "1.0.0", "ancienne version active apres echec de remplacement atomique");
        updater.Install(two);
        Assert(updater.ReadState().version == "1.0.1", "reprise apres echec de l'activation");

        foreach (var path in new[] { "../outside", "/absolute", "C:/outside", "folder\\file", "foo/../bar", "file:stream", "CON", "foo./bar", "nul.txt" })
            Fails(() => Updater.SafePath(root, path), "chemin dangereux refuse : " + path);
        var bad = Copy(two);
        bad.full.url = "https://untrusted.invalid/game.zip";
        Fails(() => Updater.ValidateManifest(bad), "hebergement non autorise refuse");
        bad = Copy(two);
        bad.files.Add(bad.files[0]);
        Fails(() => Updater.ValidateManifest(bad), "chemins dupliques refuses");
        bad = Copy(two);
        bad.previous = bad.version;
        Fails(() => Updater.ValidateManifest(bad), "boucle de versions refusee");
        bad = Copy(two);
        bad.files.Add(new FileRecord { path = "NEW.DAT/child", size = 0, sha256 = bad.files[0].sha256 });
        Fails(() => Updater.ValidateManifest(bad), "chemin fichier/dossier contradictoire refuse avant installation");

        var evil = Path.Combine(root, "evil.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open())) writer.Write("attack");
        var target = Path.Combine(root, "evil-target");
        Directory.CreateDirectory(target);
        Fails(() => Updater.Extract(evil, target, two), "ZIP avec traversee de dossier refuse");
        Assert(!File.Exists(Path.Combine(root, "outside.txt")), "aucune ecriture hors destination");

        var missing = Path.Combine(root, "missing.zip");
        Zip(Path.Combine(root, "fixture-1.0.1"), missing, new[] { "mods/medieval/mod.yaml" });
        bad = Copy(two);
        bad.delta = null;
        bad.full = Asset(missing, "1.0.1");
        updater = Installation("incomplete");
        Fails(() => updater.Install(bad), "archive incomplete refusee a la verification finale");
        Assert(updater.ReadState().version == "1.0.0", "verification finale avant activation");

        var layout = Fixture("1.0.3", one, new Dictionary<string, string>
        {
            { "obsolete.dat/content.dat", "file replaced by directory" },
            { "legacy", "directory replaced by file" }
        });
        updater = Installation("layout-change");
        requests.Clear();
        updater.Install(layout);
        Assert(requests.Contains(layout.delta.url) && !requests.Contains(layout.full.url), "changement de structure installe avec le patch");
        var layoutDirectory = Path.Combine(updater.Root, "versions/1.0.3");
        Updater.VerifyInstallation(layoutDirectory, layout);
        Assert(File.Exists(Path.Combine(layoutDirectory, "obsolete.dat/content.dat")), "un fichier peut devenir un dossier");
        Assert(File.Exists(Path.Combine(layoutDirectory, "legacy")), "un dossier peut devenir un fichier");
        Assert(updater.Rollback().version == "1.0.0", "retour arriere apres changement de structure");
        Updater.VerifyInstallation(Path.Combine(updater.Root, "versions/1.0.0"), one);

        var reversed = Fixture("1.0.4", layout, new Dictionary<string, string>
        {
            { "obsolete.dat", "file again" },
            { "legacy/payload.dat", "directory again" }
        });
        updater = Installation("layout-chain");
        requests.Clear();
        updater.Install(reversed);
        Assert(requests.Contains(layout.delta.url) && requests.Contains(reversed.delta.url) && !requests.Contains(reversed.full.url),
            "versions sautees : deux changements de structure successifs par patches");
        Updater.VerifyInstallation(Path.Combine(updater.Root, "versions/1.0.4"), reversed);
        Assert(File.ReadAllText(Path.Combine(updater.Root, "UserData/save.sav")) == "personal save", "changements de structure sans modifier les sauvegardes");

        updater = Installation("layout-interrupted");
        corrupted = reversed.delta.url;
        Fails(() => updater.Install(reversed), "second patch corrompu refuse apres le premier changement de structure");
        corrupted = null;
        Assert(updater.ReadState().version == "1.0.0", "echec du patch de structure sans changement de version active");
        Updater.VerifyInstallation(Path.Combine(updater.Root, "versions/1.0.0"), one);
    }
}
