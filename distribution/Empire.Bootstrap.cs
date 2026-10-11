// SPDX-License-Identifier: GPL-3.0-or-later
// Small .NET Framework bootstrap: Windows already supplies its runtime.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;

static class EmpireBootstrap
{
    [STAThread]
    static void Main()
    {
        try
        {
            var root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var state = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(root, "current.json")));
            var version = (string)state["version"];
            if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
                throw new InvalidDataException("Version installee invalide.");
            var launcher = Path.Combine(root, "versions", version, "updater", "Empire.Launcher.exe");
            if (!File.Exists(launcher))
                throw new FileNotFoundException("Le lanceur est introuvable. Extrayez tout le ZIP avant de jouer.", launcher);
            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "OpenRA Empire", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

}
