using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.Network;
using PostApo.Core;
using PostApo.District;
using UnityEngine;

namespace PostApo.Farm
{
    /// <summary>
    /// Gisements : le seul moyen d'obtenir les matieres premieres qui alimentent l'etabli et les
    /// ateliers de district. Recolte lente, rendement faible, stock limite, risque de blessure.
    /// </summary>
    public sealed class FarmSystem
    {
        private readonly PostApoPlugin _plugin;
        private readonly JsonStore<FarmData> _store;
        private FarmData _data;

        /// <summary>Recoltes en cours, indexees par SteamID (une seule a la fois).</summary>
        private readonly HashSet<string> _harvesting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FarmSystem(PostApoPlugin plugin, string root)
        {
            _plugin = plugin;
            _store = new JsonStore<FarmData>(root, "farm_data.json");
            Reload();
        }

        public IEnumerable<FarmNode> Nodes { get { return _data.nodes ?? new List<FarmNode>(); } }
        public int ValidCount { get { return Nodes.Count(n => n.Valid); } }

        public void Reload()
        {
            _data = _store.Load();
            if (_data.nodes == null) { _data.nodes = new List<FarmNode>(); }
            if (_data.cooldowns == null) { _data.cooldowns = new Dictionary<string, long>(); }

            foreach (var node in _data.nodes)
            {
                if (node == null) { continue; }
                Validate(node);
            }

            var invalid = Utils.ItemsReady()
                ? _data.nodes.Where(n => n != null && !n.Valid).ToArray()
                : new FarmNode[0];

            if (invalid.Length > 0)
            {
                Utils.Warn(invalid.Length + " gisement(s) desactive(s) :");
                foreach (var node in invalid.Take(10))
                {
                    Utils.Warn("  - #" + node.id + " " + node.name + " : " + node.InvalidReason);
                }
            }

            Save();
        }

        private void Validate(FarmNode node)
        {
            node.Valid = true;
            node.InvalidReason = string.Empty;

            if (node.drops == null) { node.drops = new List<FarmDrop>(); }
            if (string.IsNullOrWhiteSpace(node.kind)) { node.kind = NodeKind.Gisement; }

            node.ResolvedResourceId = Utils.ResolveItemId(node.resourceSlug, node.resourceItemId);

            foreach (var drop in node.drops)
            {
                drop.ResolvedId = Utils.ResolveItemId(drop.slug, drop.itemId);
                if (drop.minQty < 1) { drop.minQty = 1; }
                if (drop.maxQty < drop.minQty) { drop.maxQty = drop.minQty; }
            }

            var hasMain = node.ResolvedResourceId > 0 && Utils.ItemExists(node.ResolvedResourceId);
            var hasDrops = node.drops.Any(d => d.ResolvedId > 0 && Utils.ItemExists(d.ResolvedId));

            // Un point est exploitable s'il produit au moins quelque chose : ressource ou butin.
            if (!hasMain && !hasDrops)
            {
                node.Valid = false;
                node.InvalidReason = "aucune ressource ni butin exploitable";
            }

            if (!hasMain) { node.ResolvedResourceId = 0; }

            node.ResolvedToolId = Utils.ResolveItemId(node.requiredToolSlug, node.requiredToolItemId);

            if (node.position == null) { node.position = new Position(); }
            if (node.minYield < 1) { node.minYield = 1; }
            if (node.maxYield < node.minYield) { node.maxYield = node.minYield; }
            if (node.harvestTimeSeconds < 1f) { node.harvestTimeSeconds = 1f; }
            if (node.maxCharges < 0) { node.maxCharges = 0; }
            if (node.maxCharges > 0 && node.charges > node.maxCharges) { node.charges = node.maxCharges; }
            if (node.lastRegenUnix <= 0) { node.lastRegenUnix = Utils.NowUnix(); }
        }

        public bool Save() { return _store.Save(_data); }

        public FarmNode Get(int id)
        {
            return _data.nodes.FirstOrDefault(n => n != null && n.id == id);
        }

        // ------------------------------------------------------------------ points d'interaction

        public IEnumerable<InteractionPoint> Points()
        {
            if (!_plugin.Config.farm.enabled) { yield break; }

            foreach (var node in _data.nodes.ToArray())
            {
                if (node == null || !node.Valid || node.position == null) { continue; }
                var captured = node;

                yield return new InteractionPoint
                {
                    Key = "farm-" + captured.id,
                    Position = captured.position.ToVector3(),
                    VisibleTo = p => IsAllowed(p, captured)
                                     || Utils.IsStaff(p, _plugin.Config.staffLevelMin),
                    OnEnter = p => Open(p, captured),
                };
            }
        }

        /// <summary>
        /// Boussole des points connus : distance et direction depuis le joueur.
        ///
        /// Nova-Life n'expose pas de marqueur de carte au plugin ; on donne donc un cap et une
        /// distance, ce qui laisse l'exploration reelle tout en indiquant par ou commencer.
        /// </summary>
        public void OpenCompass(Player player, string kind)
        {
            if (player == null) { return; }

            var origin = Utils.Position(player);
            // « epave » regroupe epaves et caches : du point de vue du joueur, ce sont les deux
            // endroits ou l'on fouille plutot que l'on recolte.
            Func<FarmNode, bool> matches;
            if (string.Equals(kind, NodeKind.Gisement, StringComparison.OrdinalIgnoreCase))
            {
                matches = n => string.Equals(n.kind, NodeKind.Gisement, StringComparison.OrdinalIgnoreCase);
            }
            else if (string.IsNullOrEmpty(kind))
            {
                matches = n => true;
            }
            else
            {
                matches = n => !string.Equals(n.kind, NodeKind.Gisement, StringComparison.OrdinalIgnoreCase);
            }

            var wanted = _data.nodes
                .Where(n => n != null && n.Valid && n.position != null)
                .Where(n => matches(n))
                .Select(n => new { Node = n, Distance = Vector3.Distance(origin, n.position.ToVector3()) })
                .OrderBy(x => x.Distance)
                .Take(12)
                .ToList();

            var title = kind == NodeKind.Gisement ? "Gisements connus" : "Epaves et caches connues";

            if (wanted.Count == 0)
            {
                Ui.Info(player, title, Ui.Bad("Aucun point connu.")
                    + "\n\n" + Ui.Dim("Le staff n'en a pas encore place, ou aucun n'est exploitable."));
                return;
            }

            var body = Ui.Dim("Les points les plus proches de vous. Suivez le cap indique.") + "\n";
            var entries = new List<Ui.MenuEntry>();

            foreach (var item in wanted)
            {
                var node = item.Node;
                var icon = node.HasMainResource ? node.ResolvedResourceId
                    : node.drops.Count > 0 ? node.drops[0].ResolvedId : 0;

                var state = node.maxCharges > 0 && node.charges <= 0
                    ? Ui.Bad("epuise")
                    : Bearing(origin, node.position.ToVector3()) + " " + Mathf.RoundToInt(item.Distance) + " m";

                entries.Add(new Ui.MenuEntry(node.name, icon, state, null));
            }

            Ui.Menu(player, title, body, entries, "Fermer", null);
        }

        /// <summary>Cap cardinal du joueur vers un point (x = est, z = nord).</summary>
        private static string Bearing(Vector3 from, Vector3 to)
        {
            var dx = to.x - from.x;
            var dz = to.z - from.z;

            var angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (angle < 0f) { angle += 360f; }

            string[] points = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
            return points[Mathf.RoundToInt(angle / 45f) % 8];
        }

        private bool IsAllowed(Player player, FarmNode node)
        {
            if (string.IsNullOrWhiteSpace(node.requiredSpecialite)) { return true; }

            var district = _plugin.Districts.DistrictOf(player);
            return district != null && district.HasSpecialite(node.requiredSpecialite);
        }

        // ------------------------------------------------------------------ regeneration

        /// <summary>Reconstitue le stock des gisements en fonction du temps ecoule. Appele par la boucle du plugin.</summary>
        public void Tick()
        {
            var now = Utils.NowUnix();
            var multiplier = Mathf.Max(0.1f, _plugin.Config.difficulty.farmCooldownMultiplier);
            var dirty = false;

            foreach (var node in _data.nodes)
            {
                if (node == null || node.maxCharges <= 0) { continue; }
                if (node.charges >= node.maxCharges) { node.lastRegenUnix = now; continue; }

                var perCharge = (long)Mathf.Max(1f, node.rechargeSeconds * multiplier);
                var elapsed = now - node.lastRegenUnix;
                if (elapsed < perCharge) { continue; }

                var regained = (int)(elapsed / perCharge);
                if (regained <= 0) { continue; }

                node.charges = Math.Min(node.maxCharges, node.charges + regained);
                node.lastRegenUnix += regained * perCharge;
                dirty = true;
            }

            // Purge des latences expirees pour ne pas laisser grossir le fichier indefiniment.
            var expired = _data.cooldowns.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToArray();
            if (expired.Length > 0)
            {
                foreach (var key in expired) { _data.cooldowns.Remove(key); }
                dirty = true;
            }

            if (dirty) { Save(); }
        }

        // ------------------------------------------------------------------ interface

        private void Open(Player player, FarmNode node)
        {
            if (player == null || node == null) { return; }

            var steamId = Utils.SteamId(player);
            var body = Ui.Dim(DescribeNode(node)) + "\n\n";

            if (node.HasMainResource)
            {
                body += "Ressource : " + Ui.Accent(Utils.ItemName(node.ResolvedResourceId)) + "\n";
                body += "Rendement : " + EffectiveMin(node) + " a " + EffectiveMax(node) + " par passage\n";
            }

            var visibleDrops = node.drops.Where(d => d.ResolvedId > 0).ToList();
            if (visibleDrops.Count > 0)
            {
                body += "Trouvailles possibles :\n";
                foreach (var drop in visibleDrops)
                {
                    var label = string.IsNullOrWhiteSpace(drop.label) ? Utils.ItemName(drop.ResolvedId) : drop.label;
                    var odds = drop.chancePercent >= 100f ? "garanti"
                        : drop.chancePercent <= 5f ? "tres rare"
                        : drop.chancePercent <= 20f ? "rare"
                        : "occasionnel";

                    body += "  • " + label + Ui.Dim("  (" + odds + ")") + "\n";
                }
            }

            body += "Duree : " + CraftEngineFormat(EffectiveHarvestTime(node)) + "\n";

            if (node.maxCharges > 0)
            {
                body += "Stock du gisement : " + node.charges + "/" + node.maxCharges + "\n";
            }

            if (node.ResolvedToolId > 0)
            {
                var hasTool = Utils.CountItem(player, node.ResolvedToolId) > 0;
                body += (hasTool ? Ui.Ok("✓ ") : Ui.Bad("✕ ")) + "Outil : " + Utils.ItemName(node.ResolvedToolId) + "\n";
            }

            var remaining = CooldownRemaining(node, steamId);
            if (remaining > 0)
            {
                body += "\n" + Ui.Bad("Vous devez souffler encore " + remaining + " s.");
            }

            var entries = new List<Ui.MenuEntry>();
            if (remaining <= 0)
            {
                entries.Add(new Ui.MenuEntry(Ui.Ok("Recolter"), () => Harvest(player, node)));
            }

            Ui.Menu(player, node.name, body, entries, "Partir", null);
        }

        private static string DescribeNode(FarmNode node)
        {
            string what;
            switch (node.kind)
            {
                case NodeKind.Epave:
                    what = "Carcasse de l'ancien monde. On y trouve parfois des plans oublies.";
                    break;
                case NodeKind.Cache:
                    what = "Cache scellee. Ce qui dort ici ne se fabrique nulle part.";
                    break;
                default:
                    what = "Gisement exploitable.";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(node.requiredSpecialite))
            {
                what += " Reserve aux districts specialises en « " + node.requiredSpecialite + " ».";
            }

            return what;
        }

        // ------------------------------------------------------------------ recolte

        public float EffectiveHarvestTime(FarmNode node)
        {
            return Mathf.Max(1f, node.harvestTimeSeconds * Mathf.Max(0.1f, _plugin.Config.difficulty.farmTimeMultiplier));
        }

        public int EffectiveMin(FarmNode node)
        {
            return Math.Max(1, Mathf.RoundToInt(node.minYield * Mathf.Max(0.1f, _plugin.Config.difficulty.farmYieldMultiplier)));
        }

        public int EffectiveMax(FarmNode node)
        {
            return Math.Max(EffectiveMin(node),
                Mathf.RoundToInt(node.maxYield * Mathf.Max(0.1f, _plugin.Config.difficulty.farmYieldMultiplier)));
        }

        private long CooldownKeyExpiry(FarmNode node, string steamId)
        {
            long expiry;
            return _data.cooldowns.TryGetValue(CooldownKey(node, steamId), out expiry) ? expiry : 0;
        }

        private static string CooldownKey(FarmNode node, string steamId)
        {
            return node.id + "|" + (steamId ?? string.Empty);
        }

        public long CooldownRemaining(FarmNode node, string steamId)
        {
            if (node == null || string.IsNullOrEmpty(steamId)) { return 0; }
            var remaining = CooldownKeyExpiry(node, steamId) - Utils.NowUnix();
            return remaining > 0 ? remaining : 0;
        }

        private void Harvest(Player player, FarmNode node)
        {
            var prefix = _plugin.Prefix;
            var steamId = Utils.SteamId(player);

            if (string.IsNullOrEmpty(steamId)) { return; }

            if (_harvesting.Contains(steamId))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous etes deja occupe."));
                return;
            }

            if (!IsAllowed(player, node))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Ce gisement est reserve a un autre district."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), steamId, "recolte refusee sur " + node.name);
                return;
            }

            if (Utils.Distance(player, node.position.ToVector3()) > _plugin.Config.farm.interactionRadius + 1f)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Rapprochez-vous du gisement."));
                return;
            }

            var remaining = CooldownRemaining(node, steamId);
            if (remaining > 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous etes trop epuise : encore " + remaining + " s."));
                return;
            }

            if (node.maxCharges > 0 && node.charges <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Ce gisement est epuise. Revenez plus tard."));
                return;
            }

            if (node.ResolvedToolId > 0 && Utils.CountItem(player, node.ResolvedToolId) <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Il vous faut : " + Utils.ItemName(node.ResolvedToolId) + "."));
                return;
            }

            if (node.HasMainResource && !Utils.CanGiveItem(player, node.ResolvedResourceId, EffectiveMin(node)))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Votre inventaire est trop plein."));
                return;
            }

            var host = LifeManager.instance as MonoBehaviour;
            if (host == null)
            {
                Complete(player, node, steamId);
                return;
            }

            _harvesting.Add(steamId);
            host.StartCoroutine(HarvestRoutine(player, node, steamId));
        }

        private IEnumerator HarvestRoutine(Player player, FarmNode node, string steamId)
        {
            var origin = Utils.Position(player);
            var total = EffectiveHarvestTime(node);
            var elapsed = 0f;
            var maxDrift = Mathf.Max(2f, _plugin.Config.farm.maxDriftDuringActionMeters);
            var wait = new WaitForSeconds(1f);

            Utils.Center(player, node.name, "Recolte en cours...", 2f);

            while (elapsed < total)
            {
                yield return wait;
                elapsed += 1f;

                if (player == null || player.setup == null)
                {
                    _harvesting.Remove(steamId);
                    yield break;
                }

                if (Vector3.Distance(Utils.Position(player), origin) > maxDrift)
                {
                    _harvesting.Remove(steamId);
                    Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Recolte interrompue : vous avez bouge."));
                    Utils.Center(player, "Interrompu", node.name, 3f);
                    yield break;
                }

                if (elapsed % 4f < 0.01f)
                {
                    var percent = Mathf.Clamp(Mathf.RoundToInt(elapsed / total * 100f), 0, 100);
                    Utils.Center(player, node.name, percent + " %", 1.5f);
                }
            }

            _harvesting.Remove(steamId);
            Complete(player, node, steamId);
        }

        private void Complete(Player player, FarmNode node, string steamId)
        {
            if (player == null) { return; }

            var prefix = _plugin.Prefix;

            // Re-verification apres la duree : l'etat a pu changer entre-temps.
            if (node.maxCharges > 0 && node.charges <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Ce point s'est epuise pendant votre travail."));
                return;
            }

            var obtained = new List<string>();
            var rareFinds = new List<string>();
            var consumed = 0;

            // 1. Ressource principale (toujours, si le point en a une).
            if (node.HasMainResource)
            {
                var amount = Utils.RandomInt(EffectiveMin(node), EffectiveMax(node));
                if (node.maxCharges > 0) { amount = Math.Min(amount, node.charges); }

                if (amount > 0 && Utils.GiveItem(player, node.ResolvedResourceId, amount))
                {
                    obtained.Add(amount + " × " + Utils.ItemName(node.ResolvedResourceId));
                    consumed += amount;
                }
            }

            // 2. Butin : chaque ligne est tiree independamment. C'est ici que se trouvent
            //    les plans de vehicule et les composants rares.
            foreach (var drop in node.drops)
            {
                if (drop.ResolvedId <= 0) { continue; }
                if (Utils.RandomDouble() * 100.0 >= Mathf.Clamp(drop.chancePercent, 0f, 100f)) { continue; }

                var qty = Utils.RandomInt(drop.minQty, drop.maxQty);
                if (qty <= 0) { continue; }

                if (!Utils.GiveItem(player, drop.ResolvedId, qty)) { continue; }

                var label = string.IsNullOrWhiteSpace(drop.label)
                    ? Utils.ItemName(drop.ResolvedId)
                    : drop.label;

                var line = qty + " × " + label;
                obtained.Add(line);

                // Une trouvaille peu probable merite d'etre soulignee.
                if (drop.chancePercent <= 25f) { rareFinds.Add(line); }
            }

            if (obtained.Count == 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous ne trouvez rien d'exploitable."));
                return;
            }

            if (node.maxCharges > 0)
            {
                node.charges = Math.Max(0, node.charges - Math.Max(1, consumed));
            }

            var cooldown = (long)Mathf.Max(1f,
                node.playerCooldownSeconds * Mathf.Max(0.1f, _plugin.Config.difficulty.farmCooldownMultiplier));
            _data.cooldowns[CooldownKey(node, steamId)] = Utils.NowUnix() + cooldown;
            Save();

            var summary = string.Join(", ", obtained.ToArray());
            var verb = node.kind == NodeKind.Gisement ? "Recolte" : "Trouve";

            Utils.Send(player, prefix + Ui.Ok("✓ " + verb + " : " + summary + "."));

            if (rareFinds.Count > 0)
            {
                Utils.Send(player, prefix + Ui.Accent("★ Trouvaille rare : " + string.Join(", ", rareFinds.ToArray()) + " !"));
                Utils.Center(player, "Trouvaille rare", rareFinds[0], 5f);

                // Un plan sans explication ne sert a rien : on dit tout de suite ce qu'il ouvre.
                foreach (var drop in node.drops)
                {
                    if (drop.ResolvedId <= 0) { continue; }

                    var description = _plugin.Vehicles != null
                        ? _plugin.Vehicles.PlanDescription(drop.ResolvedId)
                        : null;

                    if (description == null) { continue; }
                    if (!obtained.Any(o => o.EndsWith(drop.label, StringComparison.OrdinalIgnoreCase))) { continue; }

                    Utils.Send(player, prefix + Ui.Ok("Plan obtenu : ") + description
                        + "\n" + Ui.Dim("Il est a vous. Apportez-le a l'atelier de reconstruction de "
                                        + "votre district pour ouvrir un chantier. Details : /plans"));
                    break;
                }
            }
            else
            {
                Utils.Center(player, verb, summary, 3f);
            }

            _plugin.Webhook.LogFarm(Utils.Name(player), steamId, summary + " sur « " + node.name + " »");

            TryInjure(player, node);
        }

        /// <summary>Le travail est dangereux : une recolte peut blesser.</summary>
        private void TryInjure(Player player, FarmNode node)
        {
            var difficulty = _plugin.Config.difficulty;
            if (difficulty.farmInjuryChance <= 0f || difficulty.farmInjuryDamage <= 0) { return; }
            if (Utils.RandomDouble() >= Mathf.Clamp01(difficulty.farmInjuryChance)) { return; }

            try
            {
                var current = player.Health;
                var floor = Math.Max(1, difficulty.farmInjuryMinHealth);
                if (current <= floor) { return; }

                var target = Math.Max(floor, current - difficulty.farmInjuryDamage);
                player.Health = target;

                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Vous vous blessez en travaillant (-"
                                                          + (current - target) + " PV)."));
            }
            catch (Exception ex)
            {
                Utils.Warn("blessure de recolte : " + ex.Message);
            }
        }

        public void AbortFor(string steamId)
        {
            if (!string.IsNullOrEmpty(steamId)) { _harvesting.Remove(steamId); }
        }

        // ------------------------------------------------------------------ administration

        public FarmNode Add(Vector3 position, string slugOrId, string name)
        {
            var node = new FarmNode
            {
                id = _data.nodes.Count == 0 ? 1 : _data.nodes.Max(n => n.id) + 1,
                name = string.IsNullOrWhiteSpace(name) ? "Gisement" : Utils.Sanitize(name, 32),
                position = new Position(position),
                lastRegenUnix = Utils.NowUnix(),
            };

            int numericId;
            if (int.TryParse(slugOrId, out numericId)) { node.resourceItemId = numericId; }
            else { node.resourceSlug = (slugOrId ?? string.Empty).Trim(); }

            node.charges = node.maxCharges;
            Validate(node);

            _data.nodes.Add(node);
            Save();
            _plugin.Checkpoints.RefreshAll();
            return node;
        }

        /// <summary>
        /// Cree une epave (paliers 1-3) ou une cache (paliers 4-5) prete a l'emploi.
        ///
        /// C'est le pendant « exploration » du farm : la ferraille tombe souvent, les plans et les
        /// composants rares presque jamais. Plus le palier est eleve, plus le point est avare,
        /// long a fouiller et lent a se reconstituer.
        /// </summary>
        public FarmNode AddScavengePoint(Vector3 position, int tier, string name)
        {
            tier = Mathf.Clamp(tier, 1, 5);
            var cache = tier >= 4;

            var node = new FarmNode
            {
                id = _data.nodes.Count == 0 ? 1 : _data.nodes.Max(n => n.id) + 1,
                name = string.IsNullOrWhiteSpace(name)
                    ? (cache ? "Cache scellee (palier " + tier + ")" : "Epave (palier " + tier + ")")
                    : Utils.Sanitize(name, 32),
                kind = cache ? NodeKind.Cache : NodeKind.Epave,
                position = new Position(position),
                lastRegenUnix = Utils.NowUnix(),

                // Ferraille de base : la magnetite reste la monnaie d'echange de la filiere metal.
                resourceItemId = Vehicle.Mat.Magnetite,
                minYield = 1 + tier,
                maxYield = 2 + tier * 2,

                harvestTimeSeconds = 15f + 5f * tier,
                playerCooldownSeconds = 180f + 120f * tier,
                maxCharges = Math.Max(3, 14 - 2 * tier),
                rechargeSeconds = 240f + 180f * tier,

                requiredToolItemId = cache ? Vehicle.Mat.BoiteAOutils : 0,
            };

            // Butin commun : de quoi alimenter les premieres etapes.
            node.drops.Add(new FarmDrop(Vehicle.Mat.Cuivre, 2, 4 + tier, 55f));
            node.drops.Add(new FarmDrop(Vehicle.Mat.Caoutchouc, 1, 2 + tier, 35f));
            node.drops.Add(new FarmDrop(Vehicle.Mat.Plastique, 1, 3, 30f));

            // Le plan du palier : la trouvaille qui debloque reellement un chantier.
            node.drops.Add(new FarmDrop(PlanOf(tier), 1, 1, PlanChance(tier), PlanLabel(tier)));

            // Composants rares, uniquement dans les points avances.
            if (tier >= 3)
            {
                node.drops.Add(new FarmDrop(Vehicle.Mat.FaisceauElec, 1, 1, 12f, "Faisceau electronique"));
            }

            if (tier >= 4)
            {
                node.drops.Add(new FarmDrop(Vehicle.Mat.Calculateur, 1, 1, 8f, "Calculateur moteur"));
                node.drops.Add(new FarmDrop(Vehicle.Mat.OutilPrecision, 1, 1, 6f, "Outillage de precision"));
            }

            if (tier >= 5)
            {
                node.drops.Add(new FarmDrop(Vehicle.Mat.CelluleHD, 1, 1, 4f, "Cellule haute densite"));
                node.drops.Add(new FarmDrop(Vehicle.Mat.Diamant, 1, 2, 10f));
            }

            Validate(node);
            _data.nodes.Add(node);
            Save();
            _plugin.Checkpoints.RefreshAll();
            return node;
        }

        private static int PlanOf(int tier)
        {
            switch (tier)
            {
                case 1: return Vehicle.Mat.PlanT1;
                case 2: return Vehicle.Mat.PlanT2;
                case 3: return Vehicle.Mat.PlanT3;
                case 4: return Vehicle.Mat.PlanT4;
                default: return Vehicle.Mat.PlanT5;
            }
        }

        private static string PlanLabel(int tier)
        {
            switch (tier)
            {
                case 1: return "Plan griffonne";
                case 2: return "Dossier technique";
                case 3: return "Manuel constructeur";
                case 4: return "Revue de preparation";
                default: return "Schema classifie";
            }
        }

        /// <summary>Rarete du plan : 18 % au palier 1, 3 % au palier 5.</summary>
        private static float PlanChance(int tier)
        {
            switch (tier)
            {
                case 1: return 18f;
                case 2: return 12f;
                case 3: return 8f;
                case 4: return 5f;
                default: return 3f;
            }
        }

        public bool Remove(int id)
        {
            var node = Get(id);
            if (node == null) { return false; }

            _data.nodes.Remove(node);

            var keys = _data.cooldowns.Keys.Where(k => k.StartsWith(id + "|", StringComparison.Ordinal)).ToArray();
            foreach (var key in keys) { _data.cooldowns.Remove(key); }

            Save();
            _plugin.Checkpoints.RefreshAll();
            return true;
        }

        private static string CraftEngineFormat(float seconds)
        {
            return Etabli.CraftEngine.FormatDuration(seconds);
        }
    }
}
