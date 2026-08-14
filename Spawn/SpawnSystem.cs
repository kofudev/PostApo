using System;
using Life.Network;
using PostApo.Core;

namespace PostApo.Spawn
{
    /// <summary>
    /// Pied de biche permanent.
    ///
    /// Nova-Life n'expose aucun moyen de rendre un item indestructible ou incessible depuis un
    /// plugin (option A du cahier des charges : il faudrait patcher <c>Inventory</c>, ce qui
    /// imposerait une dependance externe). Le plugin applique donc l'option B, entierement native :
    /// l'item est verifie a chaque apparition du personnage, apres chaque mort, et periodiquement.
    /// Des qu'il manque, il est rendu.
    /// </summary>
    public sealed class SpawnSystem
    {
        private readonly PostApoPlugin _plugin;
        private int _itemId;

        public SpawnSystem(PostApoPlugin plugin)
        {
            _plugin = plugin;
            Reload();
        }

        public bool Configured { get { return _itemId > 0; } }
        public int ItemId { get { return _itemId; } }

        public void Reload()
        {
            var config = _plugin.Config.crowbar;
            _itemId = config.enabled ? Utils.ResolveItemId(config.slug, config.itemId) : 0;

            if (config.enabled && _itemId <= 0 && Utils.ItemsReady())
            {
                Utils.Warn("pied de biche introuvable (config.crowbar.slug / itemId) : la remise automatique est desactivee. "
                           + "Utilisez /postapo finditem pied pour trouver le bon slug.");
            }
        }

        /// <summary>Verifie et redonne l'item si necessaire. Retourne true si un item a ete remis.</summary>
        public bool Ensure(Player player)
        {
            if (!Configured || player == null) { return false; }

            try
            {
                var config = _plugin.Config.crowbar;
                var wanted = Math.Max(1, config.quantity);
                var owned = Utils.CountItem(player, _itemId);
                if (owned >= wanted) { return false; }

                var missing = wanted - owned;
                if (!Utils.CanGiveItem(player, _itemId, missing)) { return false; }
                if (!Utils.GiveItem(player, _itemId, missing)) { return false; }

                Utils.Send(player, _plugin.Prefix + Ui.Dim("Votre pied de biche vous a ete rendu."));
                return true;
            }
            catch (Exception ex)
            {
                Utils.Warn("remise du pied de biche : " + ex.Message);
                return false;
            }
        }

        /// <summary>Passe en revue tous les joueurs connectes.</summary>
        public void EnsureAll()
        {
            if (!Configured) { return; }

            foreach (var player in Utils.OnlinePlayers())
            {
                Ensure(player);
            }
        }
    }
}
