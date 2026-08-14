using System;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.CheckpointSystem;
using Life.Network;
using UnityEngine;

namespace PostApo.Core
{
    /// <summary>
    /// Point d'interaction au sol, materialise par un checkpoint natif Nova-Life.
    /// </summary>
    public sealed class InteractionPoint
    {
        /// <summary>Cle stable, utilisee pour comparer les jeux de points d'un rafraichissement a l'autre.</summary>
        public string Key;
        public Vector3 Position;

        /// <summary>Appele quand un joueur entre dans le checkpoint.</summary>
        public Action<Player> OnEnter;

        /// <summary>Filtre optionnel : si false, le point n'est pas affiche a ce joueur.</summary>
        public Func<Player, bool> VisibleTo;
    }

    /// <summary>
    /// Gere les checkpoints de tous les systemes du plugin.
    ///
    /// Nova-Life cree les checkpoints par joueur (<c>Player.CreateCheckpoint</c>), il faut donc les
    /// (re)poser a chaque apparition et les detruire a la deconnexion. Ce service centralise ce
    /// cycle de vie pour eviter de le dupliquer dans chaque feature.
    /// </summary>
    public sealed class CheckpointService
    {
        /// <summary>Au-dela de cette distance, le point n'est pas envoye au client (limite le nombre de checkpoints).</summary>
        private const float StreamRadius = 400f;

        /// <summary>Nombre maximum de checkpoints simultanes par joueur.</summary>
        private const int MaxPerPlayer = 60;

        private readonly List<Func<IEnumerable<InteractionPoint>>> _providers =
            new List<Func<IEnumerable<InteractionPoint>>>();

        private readonly Dictionary<string, List<NCheckpoint>> _spawned =
            new Dictionary<string, List<NCheckpoint>>();

        /// <summary>Empreinte du dernier jeu de points pose pour chaque joueur.</summary>
        private readonly Dictionary<string, string> _lastSignature = new Dictionary<string, string>();

        public void AddProvider(Func<IEnumerable<InteractionPoint>> provider)
        {
            if (provider != null) { _providers.Add(provider); }
        }

        /// <summary>
        /// Repose les checkpoints visibles pour ce joueur, <b>uniquement si necessaire</b>.
        ///
        /// Detruire puis recreer les checkpoints a chaque passage les ferait clignoter cote client.
        /// On compare donc une empreinte (points visibles + zone du joueur) et on ne reconstruit que
        /// si elle a change. <paramref name="force"/> ignore cette optimisation.
        /// </summary>
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

            // Clear purge aussi l'empreinte : on l'enregistre donc apres, jamais avant.
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

                        // Le joueur et le point sont captures dans la closure.
                        //
                        // On ne peut PAS retrouver le joueur via LifeServer.GetPlayer(checkpoint.playerId) :
                        // cette surcharge n'interprete pas l'argument comme un netId et renvoie null,
                        // ce qui rendait les checkpoints totalement inertes. Les plugins natifs qui
                        // fonctionnent capturent eux aussi le joueur a la creation.
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

        /// <summary>
        /// Empreinte des points visibles pour ce joueur. La position est arrondie a 50 m :
        /// se deplacer un peu ne declenche pas de reconstruction, traverser la carte si.
        /// </summary>
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

        /// <summary>Declenche l'action du point, avec repli si le joueur capture n'est plus valide.</summary>
        private void Handle(Player player, InteractionPoint point, NCheckpoint checkpoint)
        {
            try
            {
                if (point == null || point.OnEnter == null) { return; }

                var target = player;
                if (target == null || target.setup == null)
                {
                    // Le personnage a pu etre recree depuis : on retrouve le joueur par son netId.
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

        /// <summary>Force la reconstruction chez tous les joueurs : a appeler quand un point est ajoute ou retire.</summary>
        public void RefreshAll()
        {
            foreach (var player in Utils.OnlinePlayers())
            {
                Refresh(player, true);
            }
        }

        /// <summary>Passage periodique : ne reconstruit que chez les joueurs dont l'environnement a change.</summary>
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

            // Sans cela, un joueur qui se reconnecte dans un decor inchange verrait son
            // rafraichissement saute et n'aurait plus aucun checkpoint.
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
