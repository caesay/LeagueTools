using Hardcodet.Wpf.TaskbarNotification;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LeagueTools
{
    class App : Application
    {
        public TaskbarIcon NotifyIcon { get; private set; }
        public int ConfigProxyPort { get; private set; } = 0;
        public string AppName { get; private set; } = "LeagueTools";
        public bool AppearOffline { get; private set; } = true;
        public bool RightAlign { get; private set; } = false;
        public byte[] CertificateBytes { get; private set; }

        HashSet<IntPtr> alreadySet = new HashSet<IntPtr>();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            //var io = Icon.ExtractAssociatedIcon(Riot.GetRiotClientPath());
            //using (FileStream fs = new FileStream("riot.ico", FileMode.Create))
            //    io.Save(fs);
            try
            {
                Assembly executingAssembly = Assembly.GetExecutingAssembly();
                string[] manifestResourceNames = executingAssembly.GetManifestResourceNames();
                string str = "server.pfx";

                using (var sourceStream = executingAssembly.GetManifestResourceStream(manifestResourceNames.Single(m => m.Contains(str))))
                using (var memoryStream = new MemoryStream())
                {
                    sourceStream.CopyTo(memoryStream);
                    CertificateBytes = memoryStream.ToArray();
                }

                NotifyIcon = new TaskbarIcon();
                NotifyIcon.Icon = Icon.ExtractAssociatedIcon(Riot.GetRiotClientPath());

                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                EnsureRiotNotRunning();
                SetupConfigProxy();

                SetupTrayIcon();

                DispatcherTimer timer = new DispatcherTimer();
                timer.Tick += (s, ev) =>
                {
                    SetupTrayIcon();
                    RightAlignGame();
                };
                timer.Interval = TimeSpan.FromSeconds(5);
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                Shutdown();
            }
        }

        void EnsureRiotNotRunning()
        {
            if (Riot.IsAnyRunning())
            {
                using (TaskDialog dialog = new TaskDialog())
                {
                    dialog.WindowTitle = AppName;
                    dialog.MainInstruction = $"Riot processes detected";
                    dialog.Content = AppName + " has detected Riot processes running, but LeagueTools must be started before League. Would you like to automatically kill Riot Processes?";
                    dialog.MainIcon = TaskDialogIcon.Warning;

                    TaskDialogButton kill = new TaskDialogButton("Kill Processes");
                    TaskDialogButton cancelButton = new TaskDialogButton("Exit");
                    dialog.Buttons.Add(kill);
                    dialog.Buttons.Add(cancelButton);

                    var clicked = dialog.Show();
                    if (clicked == kill)
                    {
                        Riot.KillRiotClientProcesses();
                        Thread.Sleep(3000);
                        if (Riot.IsAnyRunning())
                        {
                            using (TaskDialog err = new TaskDialog())
                            {
                                err.WindowTitle = AppName;
                                err.MainInstruction = "Unable to kill riot processes";
                                err.Content = "Unable to kill riot processes. Please kill processes manually and restart " + AppName;
                                err.MainIcon = TaskDialogIcon.Error;
                                TaskDialogButton exitButton = new TaskDialogButton(ButtonType.Ok);
                                err.Buttons.Add(exitButton);
                                err.Show();
                            }
                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }
            }
        }

        void SetupTrayIcon()
        {
            ContextMenu context = new ContextMenu();

            foreach (var e in Enum.GetValues(typeof(RiotClient)).Cast<RiotClient>())
            {
                var name = Enum.GetName(typeof(RiotClient), e);

                if (Riot.ClientIsRunning(e))
                {
                    var game = new MenuItem() { Header = name + " (Running)" };
                    game.Click += (s, ev) => Riot.ClientFocus(e);
                    context.Items.Add(game);

                    var gameRestart = new MenuItem() { Header = "Restart" };
                    gameRestart.Click += (s, ev) => Riot.ClientRestart(e, ConfigProxyPort);
                    game.Items.Add(gameRestart);

                    var gameExit = new MenuItem() { Header = "Exit" };
                    gameExit.Click += (s, ev) => Riot.ClientKill(e);
                    game.Items.Add(gameExit);
                }
                else
                {
                    var game = new MenuItem() { Header = $"Start " + name };
                    game.Click += (s, ev) => Riot.ClientStart(e, ConfigProxyPort);
                    context.Items.Add(game);
                }
            }

            context.Items.Add(new Separator());

            var logs = new MenuItem() { Header = "Presence Logs" };
            logs.IsEnabled = false;
            foreach (var p in Presence.ListOfPresence)
            {
                logs.IsEnabled = true;
                var name = "log." + p.Id;
                if (p.Observer.IsCompleted)
                    name += " (exited)";
                var pres = new MenuItem() { Header = name };
                pres.Click += (s, e) =>
                {
                    Util.ShowMessage(Util.FormatXml(p.Log));
                };
                logs.Items.Add(pres);
            }
            context.Items.Add(logs);

            context.Items.Add(new Separator());

            var align = new MenuItem() { Header = "Right-align game client?" };
            align.IsChecked = RightAlign;
            align.IsCheckable = true;
            align.Click += (s, e) =>
            {
                RightAlign = !RightAlign;
            };
            context.Items.Add(align);

            var offline = new MenuItem() { Header = "Appear offline?" };
            offline.IsChecked = AppearOffline;
            offline.IsCheckable = true;
            offline.Click += (s, e) =>
            {
                AppearOffline = !AppearOffline;
                Presence.UpdateAllPresence(AppearOffline);
            };
            context.Items.Add(offline);

            context.Items.Add(new Separator());

            var exit = new MenuItem() { Header = "Exit" };
            exit.Click += (s, e) =>
            {
                Riot.KillRiotClientProcesses();
                NotifyIcon.Dispose();
                Environment.Exit(0);
            };
            context.Items.Add(exit);

            NotifyIcon.ContextMenu = context;
        }

        void SetupConfigProxy()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var proxyServer = new ConfigProxy("https://clientconfig.rpg.riotgames.com", port);
            ConfigProxyPort = proxyServer.ConfigPort;

            int lastChatPort = 0;
            string lastChatHost = "";

            proxyServer.PatchedChatServer += (sender, args) =>
            {
                lastChatHost = args.ChatHost;
                lastChatPort = args.ChatPort;
                Console.WriteLine("Evt PatchedChatServer");
            };

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    // a client is started, and chat will be trying to connect to our TcpListener
                    var incoming = listener.AcceptTcpClient();
                    Console.WriteLine("Accepting chat TCP client");
                    NotifyIcon.ShowBalloonTip("LeagueTools", "LeagueTools is now intercepting XMPP presence.", BalloonIcon.Info);

                    try
                    {
                        var sslIncoming = new SslStream(incoming.GetStream());
                        var cert = new X509Certificate2(CertificateBytes);
                        sslIncoming.AuthenticateAsServer(cert);

                        var outgoing = new TcpClient(lastChatHost, lastChatPort);
                        var sslOutgoing = new SslStream(outgoing.GetStream());
                        sslOutgoing.AuthenticateAsClient(lastChatHost);

                        var manager = new Presence(sslIncoming, sslOutgoing, AppearOffline);
                        manager.Start();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to accept chat TCP client");
                        Console.WriteLine(e.Message);
                        NotifyIcon.ShowBalloonTip("LeagueTools", "Failed to accept XMPP proxy client: " + e.Message, BalloonIcon.Error);
                        // do nothing.
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }

        void RightAlignGame()
        {
            if (RightAlign)
            {
                var process = Process.GetProcessesByName("League of Legends");
                foreach (var p in process)
                {
                    if (p.MainWindowTitle == "League of Legends (TM) Client" && !alreadySet.Contains(p.MainWindowHandle))
                    {
                        Console.WriteLine("Found! Updating...");
                        SetWindowPos(p.MainWindowHandle, new IntPtr(0), 880, 0, 2560, 1440, 0);
                        alreadySet.Add(p.MainWindowHandle);
                    }
                }
            }
        }
    }
}
