using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace PostApo.Core
{
    /// <summary>
    /// Couche unique d'envoi vers Discord. Aucun appel HTTP n'est fait ailleurs dans le plugin.
    ///
    /// Les messages sont empiles puis envoyes par un thread de fond : le gameplay n'est jamais
    /// bloque, et une panne de Discord (URL invalide, coupure reseau, rate-limit) n'a aucun effet
    /// visible en jeu.
    /// </summary>
    public sealed class WebhookLogger : IDisposable
    {
        private const int MaxQueued = 200;

        private readonly Queue<string> _queue = new Queue<string>();
        private readonly object _gate = new object();
        private readonly ManualResetEvent _signal = new ManualResetEvent(false);

        private Thread _worker;
        private volatile bool _running;
        private volatile string _url = string.Empty;

        public WebhookLogger(string url)
        {
            SetUrl(url);

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }

            _running = true;
            _worker = new Thread(Pump) { IsBackground = true, Name = "PostApo-Webhook" };
            _worker.Start();
        }

        public bool Enabled { get { return !string.IsNullOrWhiteSpace(_url); } }

        public void SetUrl(string url)
        {
            _url = string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim();
        }

        // ------------------------------------------------------------- evenements metier

        public void LogPlayerArrival(string playerName, string steamId)
        {
            Post("\U0001F7E2 **Nouveau joueur**\n"
                 + "Joueur : " + Safe(playerName) + "\n"
                 + "SteamID : " + Safe(steamId));
        }

        public void LogDistrictSelection(string playerName, string steamId, string districtName)
        {
            Post("\U0001F3D8️ **Choix de district**\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + "District choisi : " + Safe(districtName));
        }

        public void LogDistrictJoin(string playerName, string steamId, string districtName, string gradeName)
        {
            Post("➕ **Ajout au district**\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + "District : " + Safe(districtName) + "\n"
                 + "Grade : " + Safe(gradeName));
        }

        public void LogWelcomeReward(string playerName, string steamId, IEnumerable<string> lines)
        {
            var body = lines == null ? "(aucune)" : string.Join("\n", new List<string>(lines).ToArray());
            Post("\U0001F381 **Recompenses de bienvenue**\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + body);
        }

        public void LogTeleport(string playerName, string steamId, string destination)
        {
            Post("\U0001F9ED **Teleportation**\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + "Destination : " + Safe(destination));
        }

        public void LogStaffAction(string playerName, string steamId, string action)
        {
            Post("\U0001F6E0️ **Action staff**\n"
                 + "Staff : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + Safe(action));
        }

        public void LogCraft(string playerName, string steamId, string what, bool success)
        {
            Post((success ? "⚙️ **Craft reussi**" : "\U0001F4A5 **Craft rate**") + "\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + Safe(what));
        }

        public void LogFarm(string playerName, string steamId, string what)
        {
            Post("⛏️ **Recolte**\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + Safe(what));
        }

        public void LogAbuse(string playerName, string steamId, string what)
        {
            Post("⚠️ **Tentative refusee**\n"
                 + "Joueur : " + Safe(playerName) + " (" + Safe(steamId) + ")\n"
                 + Safe(what));
        }

        public void LogError(string context, Exception ex)
        {
            var detail = ex == null ? "(sans exception)" : ex.GetType().Name + " : " + ex.Message;
            Post("❌ **Erreur plugin**\n" + Safe(context) + "\n```" + Safe(detail) + "```");
        }

        public void LogInfo(string message)
        {
            Post("ℹ️ " + Safe(message));
        }

        /// <summary>Recapitulatif complet du parcours d'arrivee d'un joueur.</summary>
        public void LogArrivalSummary(string playerName, string steamId, string districtName,
                                      bool hasBase, IEnumerable<string> rewards, string teleport)
        {
            var rewardBody = rewards == null ? "(aucune)" : string.Join("\n", new List<string>(rewards).ToArray());
            Post("\U0001F7E2 **Nouveau joueur**\n\n"
                 + "Joueur : " + Safe(playerName) + "\n"
                 + "SteamID : " + Safe(steamId) + "\n\n"
                 + "District choisi : " + Safe(districtName) + "\n\n"
                 + "Base :\n" + (hasBase ? "Oui" : "Non") + "\n\n"
                 + "Recompenses :\n" + rewardBody + "\n\n"
                 + "Teleportation :\n" + Safe(teleport));
        }

        // ------------------------------------------------------------- transport

        private void Post(string content)
        {
            if (!Enabled || string.IsNullOrEmpty(content)) { return; }

            lock (_gate)
            {
                if (_queue.Count >= MaxQueued)
                {
                    // Discord est injoignable ou sature : on jette le plus ancien plutot que de gonfler la memoire.
                    _queue.Dequeue();
                }

                _queue.Enqueue(content.Length > 1900 ? content.Substring(0, 1900) + "..." : content);
            }

            try { _signal.Set(); } catch { }
        }

        private void Pump()
        {
            while (_running)
            {
                try
                {
                    string message = null;
                    lock (_gate)
                    {
                        if (_queue.Count > 0) { message = _queue.Dequeue(); }
                    }

                    if (message == null)
                    {
                        _signal.Reset();
                        _signal.WaitOne(2000);
                        continue;
                    }

                    Send(message);

                    // Cadence volontairement basse : reste tres loin des limites Discord.
                    Thread.Sleep(1200);
                }
                catch (ThreadInterruptedException) { return; }
                catch
                {
                    // Un echec d'envoi ne doit jamais tuer le thread ni remonter en jeu.
                    try { Thread.Sleep(2000); } catch { return; }
                }
            }
        }

        private void Send(string content)
        {
            var url = _url;
            if (string.IsNullOrWhiteSpace(url)) { return; }

            try
            {
                var payload = JsonConvert.SerializeObject(new WebhookPayload { content = content });
                var bytes = Encoding.UTF8.GetBytes(payload);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.UserAgent = "PostApo-NovaLife/1.0";

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null))
                {
                    reader.ReadToEnd();
                }
            }
            catch
            {
                // Silencieux par conception : le webhook ne doit jamais perturber le serveur.
            }
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) { return "-"; }
            return value.Replace("@everyone", "@​everyone").Replace("@here", "@​here");
        }

        public void Dispose()
        {
            _running = false;
            try { _signal.Set(); } catch { }
            try
            {
                if (_worker != null && _worker.IsAlive && !_worker.Join(1500))
                {
                    _worker.Interrupt();
                }
            }
            catch { }

            _worker = null;
            try { _signal.Close(); } catch { }
        }

        private sealed class WebhookPayload
        {
            public string content { get; set; }
        }
    }
}
