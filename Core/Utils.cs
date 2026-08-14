using System;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.InventorySystem;
using Life.Network;
using UnityEngine;

namespace PostApo.Core
{
    public static class Utils
    {
        public const string Tag = "[PostApo]";

        private static readonly System.Random Rng = new System.Random();

        public static string SteamId(Player player)
        {
            try
            {
                if (player == null) { return string.Empty; }
                if (player.account != null && !string.IsNullOrWhiteSpace(player.account.steamId))
                {
                    return player.account.steamId.Trim();
                }

                return player.steamId != 0UL ? player.steamId.ToString() : string.Empty;
            }
            catch { return string.Empty; }
        }

        public static string Name(Player player)
        {
            try
            {
                if (player == null) { return "?"; }
                var full = player.FullName;
                if (!string.IsNullOrWhiteSpace(full)) { return full; }
                if (player.account != null && !string.IsNullOrWhiteSpace(player.account.username)) { return player.account.username; }
                return string.IsNullOrWhiteSpace(player.steamUsername) ? "?" : player.steamUsername;
            }
            catch { return "?"; }
        }

        public static int CharacterId(Player player)
        {
            try { return player != null && player.character != null ? player.character.Id : 0; }
            catch { return 0; }
        }

        public static IEnumerable<Player> OnlinePlayers()
        {
            try
            {
                var list = Nova.server != null ? Nova.server.GetAllPlayers() : null;
                if (list == null) { return Enumerable.Empty<Player>(); }
                return list.Where(p => p != null).ToArray();
            }
            catch { return Enumerable.Empty<Player>(); }
        }

        public static Player FindOnlineBySteamId(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) { return null; }
            var wanted = steamId.Trim();
            return OnlinePlayers().FirstOrDefault(p => string.Equals(SteamId(p), wanted, StringComparison.OrdinalIgnoreCase));
        }

        public static Player FindOnline(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { return null; }
            var q = query.Trim();

            var bySteam = FindOnlineBySteamId(q);
            if (bySteam != null) { return bySteam; }

            int charId;
            if (int.TryParse(q, out charId))
            {
                var byChar = OnlinePlayers().FirstOrDefault(p => CharacterId(p) == charId);
                if (byChar != null) { return byChar; }
            }

            return OnlinePlayers().FirstOrDefault(p => Name(p).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsStaff(Player player, int minLevel)
        {
            try
            {
                if (player == null || player.account == null) { return false; }
                return player.account.AdminLevel >= Math.Max(1, minLevel);
            }
            catch { return false; }
        }

        public static Vector3 Position(Player player)
        {
            try
            {
                if (player != null && player.setup != null)
                {
                    return ((Component)player.setup).transform.position;
                }
            }
            catch { }

            return Vector3.zero;
        }

        public static bool Teleport(Player player, Vector3 destination)
        {
            try
            {
                if (player == null || player.setup == null) { return false; }
                player.setup.TargetSetPosition(destination);
                return true;
            }
            catch (Exception ex)
            {
                Warn("teleport: " + ex.Message);
                return false;
            }
        }

        public static float Distance(Player player, Vector3 point)
        {
            try { return Vector3.Distance(Position(player), point); }
            catch { return float.MaxValue; }
        }

        public static void Send(Player player, string message)
        {
            try
            {
                if (player == null || string.IsNullOrEmpty(message)) { return; }
                player.SendText(message);
            }
            catch { }
        }

        public static void Center(Player player, string title, string subtitle, float seconds)
        {
            try
            {
                if (player == null || player.setup == null) { return; }

                player.setup.TargetShowCenterText(
                    Sanitize(title, 28),
                    Sanitize(subtitle, 36),
                    seconds);
            }
            catch { }
        }

        public static Inventory InventoryOf(Player player)
        {
            try { return player != null && player.setup != null ? player.setup.inventory : null; }
            catch { return null; }
        }

        public static int CountItem(Player player, int itemId)
        {
            try
            {
                var inv = InventoryOf(player);
                if (inv == null || inv.items == null || itemId <= 0) { return 0; }

                var total = 0;
                for (var i = 0; i < inv.items.Count; i++)
                {
                    var slot = inv.items[i];
                    if (slot.itemId == itemId)
                    {
                        total += Math.Max(1, slot.number);
                    }
                }

                return total;
            }
            catch { return 0; }
        }

        public static bool GiveItem(Player player, int itemId, int quantity)
        {
            try
            {
                var inv = InventoryOf(player);
                if (inv == null || itemId <= 0 || quantity <= 0) { return false; }
                return inv.AddItem(itemId, quantity, string.Empty, false);
            }
            catch (Exception ex)
            {
                Warn("AddItem(" + itemId + "x" + quantity + "): " + ex.Message);
                return false;
            }
        }

        public static bool CanGiveItem(Player player, int itemId, int quantity)
        {
            try
            {
                var inv = InventoryOf(player);
                if (inv == null || itemId <= 0 || quantity <= 0) { return false; }
                return inv.CanAddItem(itemId, quantity, string.Empty, false);
            }
            catch { return false; }
        }

        public static bool TakeItem(Player player, int itemId, int quantity)
        {
            try
            {
                var inv = InventoryOf(player);
                if (inv == null || itemId <= 0 || quantity <= 0) { return false; }
                return inv.RemoveItem(itemId, quantity, false);
            }
            catch (Exception ex)
            {
                Warn("RemoveItem(" + itemId + "x" + quantity + "): " + ex.Message);
                return false;
            }
        }

        public static int ResolveItemId(string slug, int fallbackId)
        {
            try
            {
                var manager = Nova.man != null ? Nova.man.item : null;
                if (manager != null && !string.IsNullOrWhiteSpace(slug))
                {
                    var bySlug = manager.GetItem(slug.Trim());
                    if (bySlug != null && bySlug.id > 0) { return bySlug.id; }
                }

                if (fallbackId > 0 && manager != null)
                {
                    var byId = manager.GetItem(fallbackId);
                    if (byId != null) { return fallbackId; }
                    return 0;
                }

                return fallbackId > 0 ? fallbackId : 0;
            }
            catch { return fallbackId > 0 ? fallbackId : 0; }
        }

        public static bool ItemsReady()
        {
            try
            {
                var manager = Nova.man != null ? Nova.man.item : null;
                return manager != null && manager.items != null && manager.items.Length > 0;
            }
            catch { return false; }
        }

        private static readonly Dictionary<int, string> KnownNames = new Dictionary<int, string>
        {
            { 3, "Bougie d'allumage" },   { 5, "Batterie" },            { 9, "Pioche" },
            { 29, "Pierre" },             { 30, "Cuivre" },             { 31, "Diamant" },
            { 32, "Hache" },              { 33, "Buche" },              { 36, "Taser" },
            { 41, "Carte Kisa" },         { 79, "Cuivre raffine" },     { 82, "Sable" },
            { 95, "Ordinateur portable" },{ 136, "Bouteille d'eau" },   { 1065, "Lingot d'or" },
            { 1077, "Carte d'Amboise" },  { 1081, "Planche" },          { 1083, "Machine d'assemblage" },
            { 1088, "Plastique" },        { 1089, "Caoutchouc" },       { 1181, "Livre" },
            { 1202, "Feuille de papier" },{ 1213, "Boite a outils" },   { 1219, "Verre" },
            { 1222, "Structure metallique" }, { 1302, "Pile de documents" }, { 1318, "Etabli" },
            { 1321, "Pile de magazine" }, { 1336, "Radio emetteur" },   { 1373, "Machine a sertir" },
            { 1419, "Magnetite" },        { 1425, "Lingot de magnetite" }, { 1429, "Plaque de metal" },
            { 1430, "Poutre en metal" },  { 1530, "Pneu" },             { 1580, "Pied de biche" },
            { 1590, "Batterie portable" },{ 1722, "Lingot de cuivre" }, { 1724, "Petit lingot d'or" },
            { 1755, "Etabli de maison" },
        };

        private static readonly Dictionary<int, string> NameCache = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> IconCache = new Dictionary<int, int>();

        public static string ItemName(int itemId)
        {
            if (itemId <= 0) { return "objet inconnu"; }

            lock (NameCache)
            {
                string cached;
                if (NameCache.TryGetValue(itemId, out cached)) { return cached; }
            }

            var resolved = ResolveName(itemId);

            lock (NameCache) { NameCache[itemId] = resolved; }
            return resolved;
        }

        private static string ResolveName(int itemId)
        {
            string raw = null;

            try
            {
                var manager = Nova.man != null ? Nova.man.item : null;
                var item = manager != null ? manager.GetItem(itemId) : null;
                if (item != null) { raw = item.itemName; }
            }
            catch { }

            if (IsReadable(raw)) { return raw.Trim(); }

            if (!string.IsNullOrWhiteSpace(raw))
            {
                foreach (var candidate in Translate(raw))
                {
                    if (IsReadable(candidate)) { return candidate.Trim(); }
                }
            }

            string known;
            if (KnownNames.TryGetValue(itemId, out known)) { return known; }

            return "objet #" + itemId;
        }

        private static IEnumerable<string> Translate(string key)
        {
            var results = new List<string>();

            try { results.Add(Nova.NewTranslate("Items", key, (string)null)); } catch { }
            try { results.Add(Nova.Translate(key)); } catch { }

            return results;
        }

        private static bool IsReadable(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { return false; }

            var trimmed = value.Trim();
            if (trimmed.Equals("NAME", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (trimmed.IndexOf('/') >= 0) { return false; }

            int ignored;
            return !int.TryParse(trimmed, out ignored);
        }

        public static int IconOf(int itemId)
        {
            if (itemId <= 0) { return -1; }

            lock (IconCache)
            {
                int cached;
                if (IconCache.TryGetValue(itemId, out cached)) { return cached; }
            }

            var index = -1;
            try
            {
                var manager = Nova.man;
                var item = manager != null && manager.item != null ? manager.item.GetItem(itemId) : null;

                if (item != null && item.models != null && manager.newIcons != null)
                {
                    var model = item.models.FirstOrDefault();
                    var icon = model != null ? model.icon : null;

                    if (icon != null)
                    {
                        var found = manager.newIcons.IndexOf(icon);
                        if (found >= 0) { index = found; }
                    }
                }
            }
            catch { }

            lock (IconCache) { IconCache[itemId] = index; }
            return index;
        }

        public static void ClearItemCaches()
        {
            lock (NameCache) { NameCache.Clear(); }
            lock (IconCache) { IconCache.Clear(); }
        }

        public static bool ItemExists(int itemId)
        {
            try
            {
                var manager = Nova.man != null ? Nova.man.item : null;
                return manager != null && itemId > 0 && manager.GetItem(itemId) != null;
            }
            catch { return false; }
        }

        public static IEnumerable<Item> SearchItems(string query, int max)
        {
            var results = new List<Item>();
            try
            {
                var manager = Nova.man != null ? Nova.man.item : null;
                if (manager == null || manager.items == null || string.IsNullOrWhiteSpace(query)) { return results; }

                foreach (var item in manager.items)
                {
                    if (item == null) { continue; }
                    var name = item.itemName ?? string.Empty;
                    var slug = item.slug ?? string.Empty;
                    if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                        && slug.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    results.Add(item);
                    if (results.Count >= max) { break; }
                }
            }
            catch { }

            return results;
        }

        public static int RandomInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) { maxInclusive = minInclusive; }
            lock (Rng) { return Rng.Next(minInclusive, maxInclusive + 1); }
        }

        public static double RandomDouble()
        {
            lock (Rng) { return Rng.NextDouble(); }
        }

        public static T PickRandom<T>(IList<T> items)
        {
            if (items == null || items.Count == 0) { return default(T); }
            lock (Rng) { return items[Rng.Next(items.Count)]; }
        }

        public static long NowUnix()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) { return string.Empty; }
            var cleaned = value.Replace("<", "(").Replace(">", ")").Replace("\r", " ").Replace("\n", " ").Trim();
            return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
        }

        public static void Log(string message) { try { Debug.Log(Tag + " " + message); } catch { } }
        public static void Warn(string message) { try { Debug.LogWarning(Tag + " " + message); } catch { } }
        public static void Error(string message) { try { Debug.LogError(Tag + " " + message); } catch { } }
    }
}
