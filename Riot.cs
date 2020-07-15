using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueTools
{
    public enum RiotClient
    {
        LeagueOfLegends = 1,
        Runeterra = 2,
        Valorant = 3,
    }

    class Riot
    {
        public static Process[] GetRiotClientProcesses()
        {
            var candidates = Process.GetProcessesByName("RiotClientServices");
            foreach (var e in Enum.GetValues(typeof(RiotClient)).Cast<RiotClient>())
                candidates = candidates.Concat(Process.GetProcessesByName(GetClientProcessName(e))).ToArray();
            return candidates;
        }

        public static bool IsAnyRunning()
        {
            return GetRiotClientProcesses().Length > 0;
        }

        public static bool ClientIsRunning(RiotClient client)
        {
            return Process.GetProcessesByName(GetClientProcessName(client)).Length > 0;
        }

        public static void ClientKill(RiotClient client)
        {
            KillProcesses(Process.GetProcessesByName(GetClientProcessName(client)));
        }

        public static void ClientStart(RiotClient client, int configPort)
        {
            if (ClientIsRunning(client))
                return;

            var startArgs = new ProcessStartInfo
            {
                FileName = GetRiotClientPath(),
                Arguments = "--client-config-url=\"http://127.0.0.1:" + configPort + "\" --launch-product=" + GetClientProductName(client) + " --launch-patchline=live"
            };

            Process.Start(startArgs);
        }

        public static void ClientFocus(RiotClient client)
        {
            foreach (var p in Process.GetProcessesByName(GetClientProcessName(client)))
            {
                Util.BringMainWindowToFront(p);
            }
        }


        public static void ClientRestart(RiotClient client, int configPort)
        {
            ClientKill(client);
            Thread.Sleep(2000);
            ClientStart(client, configPort);
        }

        public static void KillRiotClientProcesses()
        {
            IEnumerable<Process> candidates = GetRiotClientProcesses();
            KillProcesses(candidates);
        }

        public static string GetRiotClientPath()
        {
            // Find the RiotClientInstalls file.
            var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games/RiotClientInstalls.json");

            if (!File.Exists(installPath))
                return null;

            var data = JObject.Parse(File.ReadAllText(installPath));
            var rcPaths = new List<string>();

            if (data.ContainsKey("rc_default")) rcPaths.Add(data["rc_default"].ToString());
            if (data.ContainsKey("rc_live")) rcPaths.Add(data["rc_live"].ToString());
            if (data.ContainsKey("rc_beta")) rcPaths.Add(data["rc_beta"].ToString());

            return rcPaths.FirstOrDefault(File.Exists);
        }

        private static void KillProcesses(IEnumerable<Process> candidates)
        {
            foreach (var process in candidates)
            {
                Console.WriteLine("Killing process: " + process.ProcessName);
                process.Refresh();
                if (process.HasExited) continue;
                process.Kill();
                process.WaitForExit();
            }
        }

        private static string GetClientProcessName(RiotClient client)
        {
            switch (client)
            {
                case RiotClient.LeagueOfLegends:
                    return "LeagueClient";
                case RiotClient.Runeterra:
                    return "LoR";
                case RiotClient.Valorant:
                    return "VALORANT-Win64-Shipping";
                default:
                    throw new InvalidOperationException();
            }
        }

        private static string GetClientProductName(RiotClient client)
        {
            switch (client)
            {
                case RiotClient.LeagueOfLegends:
                    return "league_of_legends";
                case RiotClient.Runeterra:
                    return "bacon";
                case RiotClient.Valorant:
                    return "valorant";
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
