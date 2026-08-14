using System;
using Life.Network;
using PostApo.Core;

namespace PostApo.Spawn
{
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
