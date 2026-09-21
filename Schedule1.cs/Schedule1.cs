// WindowsGSM.Schedule1
// Copyright (c) 2026 Hotwire90
// Licensed under the MIT License - see LICENSE for full terms.
// https://github.com/Hotwire90/WindowsGSM.Schedule1

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.Plugins
{
    // WindowsGSM plugin for Schedule I dedicated servers.
    //
    // Schedule I has no official dedicated server. This plugin installs the base game via SteamCMD
    // (via the SteamCMDAgent base class WindowsGSM ships for exactly this purpose), then deploys
    // MelonLoader (the mod-loading framework) followed by the community "DedicatedServerMod" (S1DS, by
    // ifBars: https://github.com/ifBars/S1DedicatedServers), a MelonLoader mod that replaces Steam P2P
    // lobbies with a real headless/authoritative server.
    public class Schedule1 : SteamCMDAgent
    {
        // - Plugin Info
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Schedule1",
            author = "Hotwire90",
            description = "🧩 WindowsGSM plugin for Schedule I Dedicated Server, powered by the community DedicatedServerMod (S1DS)",
            version = "1.0",
            url = "https://github.com/ifBars/S1DedicatedServers",
            color = "#4CAF50"
        };

        // - Standard Constructor and properties
        public Schedule1(ServerConfig serverData) : base(serverData) => base.serverData = _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;

        // - Settings properties for SteamCMD installer
        public override bool loginAnonymous => false; // Paid EA title - the SteamCMD account must own the game
        public override string AppId => "3164500"; // Schedule I

        // - Game server Fixed variables
        public override string StartPath => "Schedule I.exe"; // Game server start path
        public string FullName = "Schedule I Dedicated Server (S1DS)";
        public bool AllowsEmbedConsole = true; // MelonLoader's --stdio-console lets us pipe stdin/stdout
        public int PortIncrements = 1;
        public object QueryMethod = null; // S1DS ships its own lightweight TCP status query, not A2S

        // - Game server default values
        public string ServerName = "Schedule I Server";
        public string Defaultmap = "Hyland Point"; // cosmetic only - the plugin never reads this field, Schedule I has one continuous world, not selectable maps
        public string Maxplayers = "8";
        public string Port = "38465";       // serverPort: UDP for gameplay, TCP for S1DS status query
        public string QueryPort = "27016";  // steamGameServerQueryPort (only used with SteamGameServer auth)
        public string Additional = "";
        // NOTE: this build's ServerConfig has no ServerPassword field. If you want a server password,
        // add --server-password "yourpassword" to Additional Parameters in the WindowsGSM Edit Server dialog.
        // Auth provider and mod verification are controlled entirely by server_config.toml now
        // (authProvider / modVerificationEnabled under [authentication]), not by default CLI flags here -
        // command-line args would otherwise silently override whatever the config file says.

        private const string S1DSRepo = "ifBars/S1DedicatedServers";
        private const string MelonLoaderRepo = "LavaGang/MelonLoader";
        private const string MelonLoaderAssetName = "MelonLoader.x64.zip";
        private const string SteamAppIdFileName = "steam_appid.txt";

        // - Create a default cfg for the game server after installation
        // WindowsGSM calls this right after a successful install, so this is where we deploy
        // MelonLoader (the mod-loading framework S1DS depends on) and then DedicatedServerMod itself
        // on top of the freshly-installed Schedule I files.
        //
        // MelonLoader MUST be installed first. Without it, Mods\ is just an inert folder - the game
        // never loads anything out of it, --dedicated-server/--stdio-console are unrecognized args that
        // Unity silently ignores, and the game boots as a normal graphical client straight into the
        // multiplayer lobby UI.
        public async void CreateServerCFG()
        {
            await DeployMelonLoader();
            await DeployDedicatedServerMod();
        }

        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            string exePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(exePath))
            {
                Error = $"{Path.GetFileName(exePath)} not found ({exePath})";
                return null;
            }

            string serverFilesDir = ServerPath.GetServersServerFiles(_serverData.ServerID);

            // Steamworks app id lookup depends on this file existing beside the exe
            string appIdFile = Path.Combine(serverFilesDir, SteamAppIdFileName);
            if (!File.Exists(appIdFile))
            {
                File.WriteAllText(appIdFile, AppId);
            }

            // Keep server_config.toml in sync with whatever's set in WindowsGSM's Edit Server dialog,
            // so changing name/port/query port/max players/GSLT there doesn't silently go stale against
            // a config file someone edited by hand once and never touched again.
            SyncServerConfig(serverFilesDir);

            string param = string.Join(" ", new[]
            {
                // Matches S1DS's own official launcher (start_server.bat, shipped in the release zip)
                // exactly: --batchmode --nographics --dedicated-server --stdio-console
                "--batchmode",
                "--nographics",
                "--dedicated-server",
                $"--server-name \"{_serverData.ServerName}\"",
                $"--max-players {_serverData.ServerMaxPlayer}",
                $"--server-port {_serverData.ServerPort}",
                "--stdio-console",
                "-logFile -",
                _serverData.ServerParam
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = serverFilesDir, // must launch from the game folder (Steamworks app-id lookup depends on it)
                    FileName = exePath,
                    Arguments = param,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            // Set up Redirect Input and Output to WindowsGSM Console if EmbedConsole is on
            if (AllowsEmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                p.Start();
                if (AllowsEmbedConsole)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        // - Stop server function
        // MelonLoader's stdio console accepts admin commands directly over stdin. DedicatedServerMod's
        // actual command for a clean shutdown is "shutdown" (see docs.s1servers.com/docs/commands/server-commands.html)
        // - "stop" is not a recognized command and is silently ignored by the console.
        public async Task Stop(Process p)
        {
            if (p == null || p.HasExited)
            {
                return;
            }

            try
            {
                if (p.StartInfo.RedirectStandardInput)
                {
                    await p.StandardInput.WriteLineAsync("shutdown");
                    await p.StandardInput.FlushAsync();
                }

                // Give S1DS a chance to autosave and shut down cleanly before force-killing
                for (int i = 0; i < 20 && !p.HasExited; i++)
                {
                    await Task.Delay(500);
                }
            }
            catch
            {
                // fall through to force-kill below
            }

            if (!p.HasExited)
            {
                p.Kill();
            }
        }

        // - Update server function
        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, error) = await Installer.SteamCMD.UpdateEx(serverData.ServerID, AppId, validate, custom: custom, loginAnonymous: loginAnonymous);
            Error = error;

            if (p == null)
            {
                // SteamCMD never started a process - most commonly a failed login (bad password,
                // Steam Guard code needed, etc). Error already holds SteamCMD's own message, so
                // just bail out here instead of crashing on the null below.
                return null;
            }

            await Task.Run(() => { p.WaitForExit(); });

            if (p.ExitCode == 0)
            {
                // Re-check/re-deploy in case a SteamCMD update touched or reset files MelonLoader/S1DS
                // overlay (version.dll, dobby.dll, Mods/, start_server.bat)
                await DeployMelonLoader();
                await DeployDedicatedServerMod();
            }

            return p;
        }

        public bool IsInstallValid()
        {
            return File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            string exePath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {Path.GetFileName(exePath)}";
            return File.Exists(exePath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }

        // Patches WindowsGSM's own server settings into server_config.toml before every Start(), so the
        // two never drift apart. Only touches the specific keys below - every comment, every other
        // setting, and the file's overall structure are left exactly as the operator has them. Does
        // nothing if the file doesn't exist yet (it's generated by the game itself on its first run, per
        // the README - nothing to sync into on a server that's never been started once).
        //
        // GSLT: WindowsGSM's built-in "Server GSLT" field (_serverData.ServerGSLT) is left blank by
        // default for every new install - it's never set here, only read from whatever the operator
        // typed into that field themselves. That field is the two-way source of truth for these two
        // config keys: a token in the field gets written into the config, and clearing the field back
        // to blank resets the config back to anonymous login, so the field always reflects what's
        // actually running - it never silently leaves a stale token behind.
        private void SyncServerConfig(string serverFilesDir)
        {
            string configPath = Path.Combine(serverFilesDir, "UserData", "server_config.toml");
            if (!File.Exists(configPath))
            {
                return;
            }

            try
            {
                string gslt = _serverData.ServerGSLT != null ? _serverData.ServerGSLT.Trim() : "";
                bool hasGslt = !string.IsNullOrEmpty(gslt);

                // "section|key" -> the new value to write, already TOML-formatted (quoted strings,
                // bare numbers/booleans). A single pass below walks the file once, tracking which
                // [section] each line is under, and rewrites any line whose "section|key" is in here.
                var targets = new Dictionary<string, string>
                {
                    ["server|serverName"] = TomlString(_serverData.ServerName),
                    ["server|serverPort"] = _serverData.ServerPort.ToString(),
                    ["server|maxPlayers"] = _serverData.ServerMaxPlayer.ToString(),
                    ["authentication|steamGameServerQueryPort"] = _serverData.ServerQueryPort.ToString(),
                    ["authentication|steamGameServerLogOnAnonymous"] = hasGslt ? "false" : "true",
                    ["authentication|steamGameServerToken"] = TomlString(hasGslt ? gslt : ""),
                };

                var lines = File.ReadAllLines(configPath).ToList();
                var sectionPattern = new Regex(@"^\s*\[([^\]]+)\]\s*$");
                var keyPattern = new Regex(@"^\s*([A-Za-z0-9_]+)\s*=");
                string currentSection = "";

                for (int i = 0; i < lines.Count; i++)
                {
                    var sectionMatch = sectionPattern.Match(lines[i]);
                    if (sectionMatch.Success)
                    {
                        currentSection = sectionMatch.Groups[1].Value.Trim();
                        continue;
                    }

                    var keyMatch = keyPattern.Match(lines[i]);
                    if (!keyMatch.Success)
                    {
                        continue;
                    }

                    string lookupKey = currentSection + "|" + keyMatch.Groups[1].Value;
                    if (targets.TryGetValue(lookupKey, out string newValue))
                    {
                        lines[i] = $"{keyMatch.Groups[1].Value} = {newValue}";
                        targets.Remove(lookupKey); // applied - and don't insert a duplicate below
                    }
                }

                // Anything left here means that key wasn't found in the file at all (shouldn't normally
                // happen for a file S1DS generated itself) - insert it right after its section header
                // rather than silently dropping the setting.
                foreach (var pending in targets)
                {
                    string[] parts = pending.Key.Split(new[] { '|' }, 2);
                    int sectionIdx = lines.FindIndex(l =>
                    {
                        var m = sectionPattern.Match(l);
                        return m.Success && m.Groups[1].Value.Trim().Equals(parts[0], StringComparison.OrdinalIgnoreCase);
                    });

                    if (sectionIdx != -1)
                    {
                        lines.Insert(sectionIdx + 1, $"{parts[1]} = {pending.Value}");
                    }
                }

                File.WriteAllLines(configPath, lines);
            }
            catch (Exception e)
            {
                Notice = $"Couldn't sync WindowsGSM's settings into server_config.toml ({e.Message}). " +
                         $"Check server_config.toml's [server]/[authentication] sections manually.";
            }
        }

        // Formats a value as a TOML basic (double-quoted) string, escaping backslashes and quotes so
        // server names/tokens with special characters can't break the file.
        private static string TomlString(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        // Schedule I ships as an IL2CPP build (confirmed via the console log: MelonLoader's Il2CppInterop
        // support module registers itself on startup). GameAssembly.dll only exists in IL2CPP Unity builds
        // - a true Mono build has no such file, just Assembly-CSharp.dll under a Managed/ folder. Deploying
        // the wrong DedicatedServerMod flavor (Mono-Server.zip) onto an IL2CPP game produces a wall of
        // Harmony TypeLoadExceptions and missing-assembly errors (e.g. FishNet.Runtime not found), because
        // the mod's assembly can't resolve types through the wrong interop layer.
        private bool IsIl2Cpp(string serverFilesDir)
        {
            return File.Exists(Path.Combine(serverFilesDir, "GameAssembly.dll"));
        }

        // Downloads the latest MelonLoader Windows x64 build from GitHub and extracts it into the game's
        // root folder (version.dll and the MelonLoader/ folder land directly beside Schedule I.exe -
        // newer MelonLoader releases don't always ship a dobby.dll, that's fine, whatever's in the zip
        // gets extracted). This is the mod-loading framework itself - a separate, required prerequisite
        // from DedicatedServerMod, which is just a set of DLLs that MelonLoader loads once it's present.
        // Skips the download entirely if version.dll is already there, so repeated installs/updates don't
        // re-fetch and re-extract it every single time.
        private async Task DeployMelonLoader()
        {
            string serverFilesDir = ServerPath.GetServersServerFiles(_serverData.ServerID);
            string versionDllPath = Path.Combine(serverFilesDir, "version.dll");

            if (File.Exists(versionDllPath))
            {
                return; // already installed
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "WindowsGSM.Schedule1");

                    string releaseJson = await Task.Run(() => wc.DownloadString($"https://api.github.com/repos/{MelonLoaderRepo}/releases/latest"));
                    var release = JObject.Parse(releaseJson);
                    string downloadUrl = release["assets"]?
                        .FirstOrDefault(a => string.Equals((string)a["name"], MelonLoaderAssetName, StringComparison.OrdinalIgnoreCase))?["browser_download_url"]?
                        .ToString();

                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        Notice = $"Schedule I installed, but couldn't find {MelonLoaderAssetName} on the latest MelonLoader release. " +
                                 $"Without MelonLoader, DedicatedServerMod can never load and the game will boot as a normal client. " +
                                 $"Download it manually from https://github.com/{MelonLoaderRepo}/releases and extract everything in the " +
                                 $"zip directly into the server folder.";
                        return;
                    }

                    string zipPath = Path.Combine(serverFilesDir, MelonLoaderAssetName);
                    await Task.Run(() => wc.DownloadFile(downloadUrl, zipPath));

                    string extractDir = Path.Combine(serverFilesDir, "_melonloader_temp");
                    if (Directory.Exists(extractDir))
                    {
                        Directory.Delete(extractDir, true);
                    }

                    ExtractZip(zipPath, extractDir);
                    MergeDirectory(extractDir, serverFilesDir);

                    Directory.Delete(extractDir, true);
                    File.Delete(zipPath);
                }

                Notice = "MelonLoader installed.";
            }
            catch (Exception e)
            {
                Notice = $"Schedule I installed, but auto-deploying MelonLoader failed ({e.Message}). " +
                         $"Without it, DedicatedServerMod can never load. Download {MelonLoaderAssetName} from " +
                         $"https://github.com/{MelonLoaderRepo}/releases and extract everything in the zip " +
                         $"directly into the server folder yourself.";
            }
        }

        // Downloads the latest S1DS release asset (Mono-Server.zip / Il2cpp_Server.zip) from GitHub and
        // merges its Mods/ folder + start_server.bat + DLLs into the freshly installed Schedule I files.
        // Deliberately never deletes anything already in Mods/ - a server operator may have other,
        // unrelated mods installed there, and this plugin has no business removing files it didn't put
        // there itself. It only overwrites files that share the same name as ones in the S1DS package.
        private async Task DeployDedicatedServerMod()
        {
            string serverFilesDir = ServerPath.GetServersServerFiles(_serverData.ServerID);
            bool il2cpp = IsIl2Cpp(serverFilesDir);
            string assetName = il2cpp ? "Il2cpp_Server.zip" : "Mono-Server.zip";

            try
            {
                // GitHub's API requires TLS 1.2; older .NET Framework defaults don't always negotiate it automatically
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "WindowsGSM.Schedule1");

                    string releaseJson = await Task.Run(() => wc.DownloadString($"https://api.github.com/repos/{S1DSRepo}/releases/latest"));
                    var release = JObject.Parse(releaseJson);
                    string downloadUrl = release["assets"]?
                        .FirstOrDefault(a => string.Equals((string)a["name"], assetName, StringComparison.OrdinalIgnoreCase))?["browser_download_url"]?
                        .ToString();

                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        Notice = $"Schedule I installed, but couldn't find {assetName} on the latest S1DedicatedServers release. " +
                                 $"Download it manually from https://github.com/{S1DSRepo}/releases and extract it into the server folder.";
                        return;
                    }

                    string zipPath = Path.Combine(serverFilesDir, assetName);
                    await Task.Run(() => wc.DownloadFile(downloadUrl, zipPath));

                    string extractDir = Path.Combine(serverFilesDir, "_s1ds_temp");
                    if (Directory.Exists(extractDir))
                    {
                        Directory.Delete(extractDir, true);
                    }

                    ExtractZip(zipPath, extractDir);
                    MergeDirectory(extractDir, serverFilesDir);

                    Directory.Delete(extractDir, true);
                    File.Delete(zipPath);
                }

                string appIdFile = Path.Combine(serverFilesDir, SteamAppIdFileName);
                if (!File.Exists(appIdFile))
                {
                    File.WriteAllText(appIdFile, AppId);
                }

                Notice = "DedicatedServerMod deployed. Start the server once, stop it, then edit server_config.toml " +
                         "(at minimum set [storage].saveGamePath) before opening the server to players.";
            }
            catch (Exception e)
            {
                Notice = $"Schedule I installed, but auto-deploying DedicatedServerMod failed ({e.Message}). " +
                         $"Download {assetName} from https://github.com/{S1DSRepo}/releases and extract it into the server folder yourself.";
            }
        }

        // Manual zip extraction via PowerShell's Expand-Archive. System.IO.Compression isn't in this
        // WindowsGSM build's plugin compiler reference set, so rather than guess at more compression
        // assemblies, just shell out to a tool every Windows box already has.
        private static void ExtractZip(string zipPath, string extractDir)
        {
            Directory.CreateDirectory(extractDir);

            string escapedZipPath = zipPath.Replace("'", "''");
            string escapedExtractDir = extractDir.Replace("'", "''");
            string command = $"Expand-Archive -LiteralPath '{escapedZipPath}' -DestinationPath '{escapedExtractDir}' -Force";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var proc = Process.Start(psi))
            {
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    throw new Exception($"Expand-Archive failed (exit {proc.ExitCode}): {stderr}");
                }
            }
        }

        private static void MergeDirectory(string sourceDir, string targetDir)
        {
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, targetDir));
            }

            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                File.Copy(filePath, filePath.Replace(sourceDir, targetDir), true);
            }
        }
    }
}
