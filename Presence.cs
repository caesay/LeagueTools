using Swan.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace LeagueTools
{
    class Presence
    {
        public Task Observer => observer;
        public int Id { get; private set; }
        public string Log => _builder.ToString();

        public static List<Presence> ListOfPresence = new List<Presence>();
        private static int _nextId = 0;

        private readonly SslStream _incoming;
        private readonly SslStream _outgoing;
        private bool _appearOffline;
        private string _lastPresence;
        private bool _connectToMuc = true;
        private int _id;
        private StringBuilder _builder;

        Task incoming = null;
        Task outgoing = null;
        Task observer = null;

        public Presence(SslStream incoming, SslStream outgoing, bool appearOffline)
        {
            this._incoming = incoming;
            this._outgoing = outgoing;
            this._appearOffline = appearOffline;
            this._builder = new StringBuilder();
            this.Id = Interlocked.Increment(ref _nextId);
        }

        public void Start()
        {
            ListOfPresence.Add(this);
            if (ListOfPresence.Count > 6)
                ListOfPresence.RemoveAt(0);

            incoming = Task.Factory.StartNew(IncomingLoop, TaskCreationOptions.LongRunning);
            outgoing = Task.Factory.StartNew(OutgoingLoop, TaskCreationOptions.LongRunning);
            observer = Task.Factory.StartNew(ObserverLoop, TaskCreationOptions.LongRunning);
        }

        public static void UpdateAllPresence(bool appearOffline)
        {
            foreach (var p in ListOfPresence)
            {
                p.UpdatePresence(appearOffline);
            }
        }

        private void UpdatePresence(bool appearOffline)
        {
            if (observer.IsCompleted)
                return;

            _appearOffline = appearOffline;
            if (string.IsNullOrEmpty(_lastPresence)) return;
            PossiblyRewriteAndResendPresence(_lastPresence, _appearOffline ? "offline" : "chat");
        }

        [DebuggerHidden]
        [DebuggerNonUserCode]
        private void ObserverLoop()
        {
            while (true)
            {
                Thread.Sleep(1000);
                if (incoming.Status != TaskStatus.Running || outgoing.Status != TaskStatus.Running)
                {
                    var exception = incoming.Exception ?? outgoing.Exception;
                    if (exception != null)
                    {
                        _builder.AppendLine("(closed)");
                        _builder.AppendLine();
                        _builder.AppendLine("An exception was thrown:");
                        _builder.AppendLine(exception.ToString());
                        throw exception;
                    }
                }
            }
        }

        [DebuggerHidden]
        [DebuggerNonUserCode]
        private void IncomingLoop()
        {
            int byteCount;
            var bytes = new byte[4096];

            do
            {
                if (outgoing != null && outgoing.IsCompleted)
                    break;

                byteCount = _incoming.Read(bytes, 0, bytes.Length);
                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);


                //Logger.Debug("\nFROM RC: " + content);

                // If this is possibly a presence stanza, rewrite it.
                if (content.Contains("<presence"))
                {
                    PossiblyRewriteAndResendPresence(content, _appearOffline ? "offline" : "chat");
                }
                else
                {
                    _builder.AppendLine(content);
                    _outgoing.Write(bytes, 0, byteCount);
                }
            } while (byteCount != 0);

            //Logger.Warn(@"Incoming closed.");
        }

        [DebuggerHidden]
        [DebuggerNonUserCode]
        private void OutgoingLoop()
        {
            int byteCount;
            var bytes = new byte[4096];

            do
            {
                if (incoming != null && incoming.IsCompleted)
                    break;

                byteCount = _outgoing.Read(bytes, 0, bytes.Length);
                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);
                _builder.AppendLine(content);
                //Logger.Debug("\nTO RC: " + Encoding.UTF8.GetString(bytes, 0, byteCount));
                _incoming.Write(bytes, 0, byteCount);
            } while (byteCount != 0);

            //Logger.Warn(@"Outgoing closed.");
        }

        private void PossiblyRewriteAndResendPresence(string content, string targetStatus)
        {
            Logger.Warn("Rewriting presence!");
            try
            {
                _lastPresence = content;
                var wrappedContent = "<xml>" + content + "</xml>";
                var xml = XDocument.Load(new StringReader(wrappedContent));

                if (xml.Root == null) return;
                if (xml.Root.HasElements == false) return;

                foreach (var presence in xml.Root.Elements())
                {
                    if (presence.Name != "presence") continue;
                    if (presence.Attribute("to") != null)
                    {
                        if (_connectToMuc) continue;
                        presence.Remove();
                    }

                    if (targetStatus != "chat" || presence.Element("games")?.Element("league_of_legends")?.Element("st")?.Value != "dnd")
                    {
                        presence.Element("show")?.ReplaceNodes(targetStatus);
                        presence.Element("games")?.Element("league_of_legends")?.Element("st")?.ReplaceNodes(targetStatus);
                    }

                    if (targetStatus == "chat") continue;
                    presence.Element("status")?.Remove();

                    if (targetStatus == "mobile")
                    {
                        presence.Element("games")?.Element("league_of_legends")?.Element("p")?.Remove();
                        presence.Element("games")?.Element("league_of_legends")?.Element("m")?.Remove();
                    }
                    else
                    {
                        presence.Element("games")?.Element("league_of_legends")?.Remove();
                    }

                    //Remove Legends of Runeterra presence
                    presence.Element("games")?.Element("bacon")?.Remove();

                    //Remove VALORANT presence
                    presence.Element("games")?.Element("valorant")?.Remove();
                }

                var sb = new StringBuilder();
                var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment };
                using (var xw = XmlWriter.Create(sb, xws))
                {
                    foreach (var xElement in xml.Root.Elements())
                    {
                        xElement.WriteTo(xw);
                    }
                }

                _builder.AppendLine(sb.ToString());
                File.WriteAllText("gmp.txt", content + Environment.NewLine + "==============================" + Environment.NewLine + sb.ToString());

                _outgoing.Write(Encoding.UTF8.GetBytes(sb.ToString()));
                Logger.Info("DECEIVE: " + sb);
            }
            catch (Exception e)
            {
                Logger.Error("Error rewriting presence.");
                Logger.Error(e.Message);
            }
        }
    }
}
