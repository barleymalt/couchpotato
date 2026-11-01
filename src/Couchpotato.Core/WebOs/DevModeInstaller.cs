using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Couchpotato.WebOs
{
    public sealed class InstallException : Exception
    {
        public InstallException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Installs the TV app through webOS Developer Mode, doing what LG's ares-install does:
    /// fetch the key from the Key Server, SSH in as "prisoner", stream the .ipk, ask the
    /// install service to install it, then launch it with the pairing parameters.
    /// </summary>
    public static class DevModeInstaller
    {
        const int SshPort = 9922;
        const int KeyServerPort = 9991;
        const string User = "prisoner";
        const string TempDir = "/media/developer/temp";
        const string LunaSend = "/usr/bin/luna-send-pub";
        static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);

        /// <summary>Downloads the passphrase-protected SSH key served by the Developer Mode app's Key Server.</summary>
        public static async Task<string> FetchKeyAsync(IPAddress tv)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                {
                    var pem = await http.GetStringAsync("http://" + tv + ":" + KeyServerPort + "/webos_rsa").ConfigureAwait(false);
                    if (pem.IndexOf("PRIVATE KEY", StringComparison.Ordinal) < 0) throw new InstallException("The TV's Key Server returned something that is not a key.");
                    return pem;
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                throw new InstallException("Can't reach the TV's Key Server. In the Developer Mode app on the TV, turn on \"Key Server\".", e);
            }
        }

        /// <returns>The ids of old MagicRemoteService apps that were removed from the TV.</returns>
        public static IList<string> Install(IPAddress tv, string pemKey, string passphrase, byte[] ipk, string appId, string launchParamsJson, Action<string> progress)
        {
            if (launchParamsJson.IndexOf('\'') >= 0) throw new ArgumentException("Launch parameters must not contain single quotes");

            PrivateKeyFile key;
            try
            {
                key = new PrivateKeyFile(new MemoryStream(Encoding.ASCII.GetBytes(pemKey)), (passphrase ?? "").Trim());
            }
            catch (Exception e) when (e is SshException || e is InvalidOperationException || e is ArgumentException ||
                                      e is System.Security.Cryptography.CryptographicException)
            {
                throw new InstallException("Wrong passphrase. Type the passphrase shown in the Developer Mode app on the TV.", e);
            }

            var connection = new ConnectionInfo(tv.ToString(), SshPort, User, new PrivateKeyAuthenticationMethod(User, key))
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            using (var ssh = new SshClient(connection))
            {
                progress("Connecting to the TV…");
                try { ssh.Connect(); }
                catch (Exception e) when (e is SshException || e is System.Net.Sockets.SocketException)
                {
                    throw new InstallException("Can't open a Developer Mode connection to the TV. Check that Developer Mode is on and the TV was restarted after enabling it.", e);
                }

                var remotePath = TempDir + "/" + appId + ".ipk";
                progress("Uploading the TV app…");
                Run(ssh, "/bin/mkdir -p " + TempDir);
                Upload(ssh, remotePath, ipk);

                progress("Installing the TV app…");
                try
                {
                    var error = WaitForInstallService(ssh, "install", "{\"id\":\"com.ares.defaultName\",\"ipkUrl\":\"" + remotePath + "\",\"subscribe\":true}", "installed");
                    if (error != null) throw new InstallException("The TV could not install the app: " + error);
                }
                finally { TryRun(ssh, "/bin/rm -f \"" + remotePath + "\""); }

                var removed = RemoveOldApps(ssh, progress);

                progress("Starting the TV app…");
                var output = Run(ssh, LunaSend + " -n 1 luna://com.webos.applicationManager/launch '{\"id\":\"" + appId +
                                      "\",\"subscribe\":false,\"params\":" + launchParamsJson + "}'");
                if (!Regex.IsMatch(output, "\"returnValue\"\\s*:\\s*true"))
                    throw new InstallException("The app was installed but the TV refused to start it: " + output.Trim());

                ssh.Disconnect();
                return removed;
            }
        }

        /// <summary>
        /// Uninstalls the TV apps of the original MagicRemoteService (one per HDMI input), so they stop
        /// grabbing the remote. Best effort: a failure here never fails the setup.
        /// </summary>
        static IList<string> RemoveOldApps(SshClient ssh, Action<string> progress)
        {
            var removed = new List<string>();
            try
            {
                var apps = Run(ssh, LunaSend + " -n 1 luna://com.webos.applicationManager/dev/listApps '{}'");
                var ids = Regex.Matches(apps, "\"id\"\\s*:\\s*\"(com\\.cathwyler\\.magicremoteservice(\\.[a-z0-9]+)?)\"")
                               .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
                foreach (var id in ids)
                {
                    progress("Removing the old MagicRemoteService app (" + id + ")…");
                    // In "input" mode the old app also registered itself as a TV input.
                    TryRun(ssh, LunaSend + " -n 1 luna://com.webos.service.eim/deleteDevice '{\"appId\":\"" + id + "\"}'");
                    var error = WaitForInstallService(ssh, "remove", "{\"id\":\"" + id + "\",\"subscribe\":true}", "removed");
                    if (error == null) removed.Add(id);
                    else Log.Warn("Could not remove " + id + ": " + error);
                }
            }
            catch (Exception e)
            {
                Log.Warn("Looking for the old MagicRemoteService app failed: " + e.Message);
            }
            return removed;
        }

        static void Upload(SshClient ssh, string remotePath, byte[] data)
        {
            using (var cmd = ssh.CreateCommand("/bin/cat > \"" + remotePath + "\""))
            {
                var run = cmd.BeginExecute();
                using (var input = cmd.CreateInputStream()) input.Write(data, 0, data.Length);
                cmd.EndExecute(run);
                if (cmd.ExitStatus != 0) throw new InstallException("Uploading the app failed: " + cmd.Error);
            }
            var size = Run(ssh, "/bin/ls -l \"" + remotePath + "\"");
            if (!Regex.IsMatch(size, "\\s" + data.Length + "\\s")) throw new InstallException("Uploading the app failed: size mismatch.");
        }

        /// <summary>
        /// Calls com.webos.appInstallService/dev/{method} with "luna-send-pub -i" and waits for the
        /// done state or a failure, like ares-install. Returns null on success, else the reason.
        /// </summary>
        static string WaitForInstallService(SshClient ssh, string method, string paramsJson, string doneState)
        {
            var command = LunaSend + " -i luna://com.webos.appInstallService/dev/" + method + " '" + paramsJson + "'";
            using (var cmd = ssh.CreateCommand(command))
            {
                cmd.BeginExecute();
                var reader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
                var outcome = Task.Run(() =>
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var state = Regex.Match(line, "\"state\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                        if (Regex.IsMatch(state, "FAILED", RegexOptions.IgnoreCase))
                        {
                            var reason = Regex.Match(line, "\"reason\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                            return reason.Length > 0 ? reason : state;
                        }
                        if (Regex.IsMatch(state, doneState + "|^SUCCESS", RegexOptions.IgnoreCase)) return null;
                        if (Regex.IsMatch(line, "\"returnValue\"\\s*:\\s*false")) return line.Trim();
                    }
                    return "the TV closed the request without an answer";
                });

                bool finished = outcome.Wait(InstallTimeout);
                try { cmd.CancelAsync(); } catch (Exception) { }
                return finished ? outcome.Result : "no answer within two minutes";
            }
        }

        static string Run(SshClient ssh, string command)
        {
            using (var cmd = ssh.RunCommand(command))
            {
                if (cmd.ExitStatus != 0) throw new InstallException("Command failed on the TV (" + command.Split(' ')[0] + "): " + cmd.Error.Trim());
                return cmd.Result;
            }
        }

        static void TryRun(SshClient ssh, string command)
        {
            try { ssh.RunCommand(command).Dispose(); } catch (Exception) { }
        }
    }
}
