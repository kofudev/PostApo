using System;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.Network;
using UnityEngine;

namespace PostApo.Core
{
    public sealed class CommandRegistry
    {
        private const string Description = "PostApo";

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<string, float> _lastCall = new Dictionary<string, float>();

        public bool Registered { get; private set; }

        public void Add(string name, string[] aliases, string usage, Action<Player, string[]> handler)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null) { return; }

            _entries.Add(new Entry
            {
                Name = Normalize(name),
                Aliases = (aliases ?? new string[0]).Select(Normalize).Where(a => !string.IsNullOrEmpty(a)).ToArray(),
                Usage = usage ?? string.Empty,
                Handler = handler,
            });
        }

        public bool TryRegister()
        {
            if (Registered) { return true; }

            try
            {
                if (Nova.server == null || Nova.server.chat == null || Nova.server.chat.commands == null)
                {
                    return false;
                }
            }
            catch { return false; }

            var count = 0;
            foreach (var entry in _entries)
            {
                try
                {
                    var captured = entry;
                    Action<Player, string[]> action = (player, args) => Dispatch(player, captured, args);

                    var existing = Nova.server.chat.commands.FirstOrDefault(
                        c => c != null && string.Equals(c.fullCommandName, captured.Name, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        if (string.Equals(existing.description, Description, StringComparison.Ordinal))
                        {
                            existing.aliases = captured.Aliases;
                            existing.usage = captured.Usage;
                            existing.action = action;
                        }
                        else
                        {
                            Utils.Warn("commande " + captured.Name + " deja utilisee par « " + existing.description + " » : ignoree.");
                            continue;
                        }
                    }
                    else
                    {
                        new SChatCommand(captured.Name, captured.Aliases, Description, captured.Usage, action).Register();
                    }

                    count++;
                }
                catch (Exception ex)
                {
                    Utils.Warn("enregistrement de " + entry.Name + " : " + ex.Message);
                }
            }

            if (count == 0) { return false; }

            Registered = true;
            Utils.Log(count + " commande(s) enregistree(s) dans le chat.");
            return true;
        }

        private void Dispatch(Player player, Entry entry, string[] args)
        {
            if (player == null || entry == null) { return; }

            try
            {
                var key = Utils.SteamId(player) + "|" + entry.Name + "|" + string.Join(" ", args ?? new string[0]);
                var now = Time.realtimeSinceStartup;

                float previous;
                if (_lastCall.TryGetValue(key, out previous) && now - previous < 0.4f) { return; }
                _lastCall[key] = now;

                if (_lastCall.Count > 512)
                {
                    foreach (var stale in _lastCall.Where(kv => now - kv.Value > 30f).Select(kv => kv.Key).ToArray())
                    {
                        _lastCall.Remove(stale);
                    }
                }
            }
            catch { }

            try
            {
                entry.Handler(player, args ?? new string[0]);
            }
            catch (Exception ex)
            {
                Utils.Error("commande " + entry.Name + " : " + ex);
                Utils.Send(player, Ui.Bad("✕ Erreur interne, l'action a ete annulee."));
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return string.Empty; }
            var trimmed = value.Trim();
            return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
        }

        private sealed class Entry
        {
            public string Name;
            public string[] Aliases;
            public string Usage;
            public Action<Player, string[]> Handler;
        }
    }
}
