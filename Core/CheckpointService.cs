using System;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.CheckpointSystem;
using Life.Network;
using UnityEngine;

namespace PostApo.Core
{
    public sealed class InteractionPoint
    {
        public string Key;
        public Vector3 Position;

        public Action<Player> OnEnter;

        public Func<Player, bool> VisibleTo;
    }

    public sealed class CheckpointService
    {
        private const float StreamRadius = 400f;

        private const int MaxPerPlayer = 60;

        private readonly List<Func<IEnumerable<InteractionPoint>>> _providers =
            new List<Func<IEnumerable<InteractionPoint>>>();

        private readonly Dictionary<string, List<NCheckpoint>> _spawned =
            new Dictionary<string, List<NCheckpoint>>();

        private readonly Dictionary<string, string> _lastSignature = new Dictionary<string, string>();

        public void AddProvider(Func<IEnumerable<InteractionPoint>> provider)
        {
            if (provider != null) { _providers.Add(provider); }
        }

        public void Refresh(Player player, bool force = false)
        {
            if (player == null) { return; }

            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId)) { return; }

            var signature = BuildSignature(player);

            string previous;
            if (!force && _lastSignature.TryGetValue(steamId, out previous) && previous == signature)
            {
                return;
            }

            Clear(player);
            _lastSignature[steamId] = signature;

            var origin = Utils.Position(player);
            var created = new List<NCheckpoint>();

            foreach (var provider in _providers)
            {
                IEnumerable<InteractionPoint> points;
                try { points = provider() ?? Enumerable.Empty<InteractionPoint>(); }
                catch (Exception ex) { Utils.Warn("fournisseur de points : " + ex.Message); continue; }

                foreach (var point in points)
                {
                    if (created.Count >= MaxPerPlayer) { break; }
                    if (point == null || point.OnEnter == null) { continue; }

                    try
                    {
                        if (point.VisibleTo != null && !point.VisibleTo(player)) { continue; }
                        if (Vector3.Distance(origin, point.Position) > StreamRadius) { continue; }

                        var capturedPlayer = player;
                        var capturedPoint = point;

                        var checkpoint = new NCheckpoint(player.netId, point.Position,
                            cp => Handle(capturedPlayer, capturedPoint, cp));

                        player.CreateCheckpoint(checkpoint);
                        created.Add(checkpoint);
                    }
                    catch (Exception ex)
                    {
                        Utils.Warn("creation de checkpoint (" + point.Key + ") : " + ex.Message);
                    }
                }
            }

            _spawned[steamId] = created;
        }

        private string BuildSignature(Player player)
        {
            var origin = Utils.Position(player);
            var builder = new System.Text.StringBuilder();

            builder.Append((int)(origin.x / 50f)).Append(':')
                   .Append((int)(origin.z / 50f)).Append('|');

            foreach (var provider in _providers)
            {
                IEnumerable<InteractionPoint> points;
                try { points = provider() ?? Enumerable.Empty<InteractionPoint>(); }
                catch { continue; }

                foreach (var point in points)
                {
                    if (point == null) { continue; }

                    try
                    {
                        if (point.VisibleTo != null && !point.VisibleTo(player)) { continue; }
                        if (Vector3.Distance(origin, point.Position) > StreamRadius) { continue; }
                    }
                    catch { continue; }

                    builder.Append(point.Key).Append(';');
                }
            }

            return builder.ToString();
        }

        private void Handle(Player player, InteractionPoint point, NCheckpoint checkpoint)
        {
            try
            {
                if (point == null || point.OnEnter == null) { return; }

                var target = player;
                if (target == null || target.setup == null)
                {
                    target = checkpoint == null
                        ? null
                        : Utils.OnlinePlayers().FirstOrDefault(p => p.netId == checkpoint.playerId);
                }

                if (target == null) { return; }

                point.OnEnter(target);
            }
            catch (Exception ex)
            {
                Utils.Warn("entree de checkpoint (" + (point != null ? point.Key : "?") + ") : " + ex.Message);
            }
        }

        public void RefreshAll()
        {
            foreach (var player in Utils.OnlinePlayers())
            {
                Refresh(player, true);
            }
        }

        public void Tick()
        {
            foreach (var player in Utils.OnlinePlayers())
            {
                Refresh(player, false);
            }
        }

        public void Clear(Player player)
        {
            if (player == null) { return; }

            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId)) { return; }

            List<NCheckpoint> existing;
            if (!_spawned.TryGetValue(steamId, out existing) || existing == null) { return; }

            foreach (var checkpoint in existing)
            {
                try { player.DestroyCheckpoint(checkpoint); }
                catch { }
            }

            _spawned.Remove(steamId);

            _lastSignature.Remove(steamId);
        }

        public void ClearAll()
        {
            foreach (var player in Utils.OnlinePlayers())
            {
                Clear(player);
            }

            _spawned.Clear();
            _lastSignature.Clear();
        }
    }
}
