// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Empire
{
    public sealed class Launcher : Form
    {
        readonly Updater updater;
        readonly Label status;
        readonly Label version;
        readonly Button update;
        readonly Button play;
        readonly Button rollback;
        readonly ProgressBar progress;
        Manifest latest;
        bool busy;

        public Launcher(string root)
        {
            Text = "OpenRA Empire — Les Terres du Bourg";
            ClientSize = new Size(660, 360);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(24, 39, 34);
            ForeColor = Color.FromArgb(239, 231, 209);
            Font = new Font("Segoe UI", 10);
            AddLabel("LES TERRES DU BOURG", 28, 25, 602, 40, 23);
            AddLabel("OpenRA Empire", 30, 70, 595, 27, 11);
            version = AddLabel("", 30, 113, 595, 26, 10);
            status = AddLabel("Recherche des mises à jour…", 30, 151, 595, 66, 11);
            progress = new ProgressBar { Location = new Point(30, 219), Size = new Size(600, 5), Style = ProgressBarStyle.Marquee };
            Controls.Add(progress);
            update = AddButton("Vérification…", 30, 243, 290);
            play = AddButton("Jouer avec la version installée", 335, 243, 295);
            rollback = AddButton("Revenir à la version précédente", 30, 303, 358);
            rollback.Height = 31;
            var link = new LinkLabel
            {
                Text = "Notes des mises à jour", Location = new Point(437, 308), Size = new Size(193, 26),
                LinkColor = Color.FromArgb(223, 194, 118)
            };
            link.LinkClicked += delegate { System.Diagnostics.Process.Start("https://github.com/" + Updater.Repository + "/releases"); };
            Controls.Add(link);
            updater = new Updater(root, null, message =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => status.Text = message));
            });
            play.Click += delegate { LaunchGame(); };
            update.Click += async delegate
            {
                if (latest != null && Updater.ParseVersion(latest.version) > Updater.ParseVersion(updater.ReadState().version))
                    await Operate("update");
                else await Operate("check");
            };
            rollback.Click += async delegate { await Operate("rollback"); };
            Shown += async delegate { await Operate("check"); };
            FormClosing += (sender, args) => { if (busy) args.Cancel = true; };
        }

        Label AddLabel(string text, int x, int y, int width, int height, int size)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Segoe UI", size) };
            Controls.Add(label);
            return label;
        }

        Button AddButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(width, 44), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(49, 70, 53), ForeColor = ForeColor
            };
            Controls.Add(button);
            return button;
        }

        void RefreshState()
        {
            var state = updater.ReadState();
            version.Text = "Version installée : " + state.version;
            update.Enabled = play.Enabled = !busy;
            rollback.Enabled = !busy && state.previous != null;
            progress.Visible = busy;
            ControlBox = !busy;
            update.Text = latest != null && Updater.ParseVersion(latest.version) > Updater.ParseVersion(state.version)
                ? "Mettre à jour et jouer" : "Vérifier les mises à jour";
        }

        async Task Operate(string action)
        {
            busy = true;
            RefreshState();
            try
            {
                if (action == "check")
                {
                    status.Text = "Recherche des mises à jour…";
                    latest = await Task.Run(() => updater.FetchManifest());
                    status.Text = Updater.ParseVersion(latest.version) > Updater.ParseVersion(updater.ReadState().version)
                        ? "La version " + latest.version + " est disponible. Vos données seront conservées."
                        : "Votre jeu est à jour. Bon retour au bourg !";
                }
                else
                {
                    status.Text = "Vérification des fichiers…";
                    await Task.Run(() => action == "rollback" ? updater.Rollback() : updater.Install(latest));
                    busy = false;
                    LaunchGame();
                }
            }
            catch (Exception error)
            {
                status.Text = "Opération impossible. Vous pouvez jouer avec la version installée.";
                try { File.AppendAllText(Path.Combine(updater.Root, "updater.log"), DateTime.UtcNow.ToString("o") + " " + action + ": " + error.Message + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (action != "check") MessageBox.Show(this, error.Message, "Mise à jour interrompue", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { busy = false; if (!IsDisposed) RefreshState(); }
        }

        void LaunchGame()
        {
            try { updater.Play(); Close(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Lancement impossible"); }
        }

        [STAThread]
        static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                // root/versions/1.0.0/updater/Empire.Launcher.exe
                var root = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent.Parent.FullName;
                using (var installationLock = File.Open(Updater.SafePath(root, "launcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                using (var form = new Launcher(root)) Application.Run(form);
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("Le lanceur est déjà ouvert, ou les fichiers sont indisponibles. Extrayez tout le ZIP dans Documents ou sur le Bureau.\n\n" + error.Message,
                    "OpenRA Empire", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
