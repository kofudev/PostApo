using System;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.Network;
using PostApo.Core;
using PostApo.District;
using UnityEngine;

namespace PostApo.Etabli
{
    /// <summary>
    /// Etabli : point de fabrication staff, fabrication de l'etabli personnel, pose unique,
    /// et menu de craft sur les etablis poses.
    /// </summary>
    public sealed class EtabliSystem
    {
        private readonly PostApoPlugin _plugin;
        private readonly JsonStore<EtabliData> _store;
        private EtabliData _data;

        /// <summary>Id de l'item « etabli », resolu au chargement. 0 = non configure.</summary>
        private int _etabliItemId;

        public EtabliSystem(PostApoPlugin plugin, string root)
        {
            _plugin = plugin;
            _store = new JsonStore<EtabliData>(root, "etabli_data.json");
            Reload();
        }

        public EtabliData Data { get { return _data; } }
        public int EtabliItemId { get { return _etabliItemId; } }
        public bool Configured { get { return _etabliItemId > 0; } }

        public void Reload()
        {
            _data = _store.Load();
            if (_data.staffPoints == null) { _data.staffPoints = new List<StaffCraftPoint>(); }
            if (_data.placed == null) { _data.placed = new List<PlacedEtabli>(); }
            if (_data.playersWhoPlaced == null) { _data.playersWhoPlaced = new List<string>(); }

            var config = _plugin.Config.etabli;
            _etabliItemId = Utils.ResolveItemId(config.etabliItemSlug, config.etabliItemId);

            if (_etabliItemId <= 0 && Utils.ItemsReady())
            {
                Utils.Warn("aucun item d'etabli valide (config.etabli.etabliItemSlug / etabliItemId) : "
                           + "la fabrication et la pose d'etabli sont desactivees. "
                           + "Utilisez /postapo finditem <texte> pour trouver le bon slug.");
            }

            Save();
        }

        public bool Save() { return _store.Save(_data); }

        // ------------------------------------------------------------------ points d'interaction

        public IEnumerable<InteractionPoint> Points()
        {
            if (!_plugin.Config.etabli.enabled) { yield break; }

            foreach (var point in _data.staffPoints.ToArray())
            {
                if (point == null || point.position == null) { continue; }
                var captured = point;

                yield return new InteractionPoint
                {
                    Key = "etabli-staff-" + captured.id,
                    Position = captured.position.ToVector3(),
                    OnEnter = p => OpenStaffPoint(p, captured),
                };
            }

            foreach (var etabli in _data.placed.ToArray())
            {
                if (etabli == null || etabli.position == null) { continue; }
                var captured = etabli;

                yield return new InteractionPoint
                {
                    Key = "etabli-" + captured.ownerSteamId,
                    Position = captured.position.ToVector3(),
                    VisibleTo = p => CanUse(p, captured)
                                     || Utils.IsStaff(p, _plugin.Config.staffLevelMin),
                    OnEnter = p => OpenPlacedEtabli(p, captured),
                };
            }
        }

        /// <summary>Un etabli est utilisable par son proprietaire et, s'il est partage, par son district.</summary>
        private bool CanUse(Player player, PlacedEtabli etabli)
        {
            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId) || etabli == null) { return false; }

            if (string.Equals(steamId, etabli.ownerSteamId, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (!etabli.sharedWithDistrict) { return false; }

            var ownerDistrict = _plugin.Districts.DistrictOf(etabli.ownerSteamId);
            return ownerDistrict != null && ownerDistrict.FindMember(steamId) != null;
        }

        // ------------------------------------------------------------------ point staff

        private void OpenStaffPoint(Player player, StaffCraftPoint point)
        {
            if (player == null || point == null) { return; }

            var config = _plugin.Config.etabli;

            if (!Configured)
            {
                Ui.Info(player, point.name,
                    Ui.Bad("La fabrication d'etabli n'est pas configuree sur ce serveur.")
                    + "\n\n" + Ui.Dim("Le staff doit renseigner etabli.etabliItemId dans config.json."));
                return;
            }

            // Le joueur doit comprendre ou il est et a quoi sert l'endroit sans deviner.
            var body = "Vous êtes à l'" + Ui.Accent("atelier communal") + ".\n"
                       + Ui.Dim("C'est ici que vous fabriquez votre établi personnel. "
                                + "Une fois fabriqué, posez-le où vous voulez avec "
                                + Ui.Accent("/etabli pose") + " — il devient votre atelier de craft.")
                       + "\n\n" + Ui.Dim("Un seul établi par joueur, définitif.")
                       + "\n\n<b>Ressources nécessaires :</b>\n";

            var missing = new List<string>();
            var entries = new List<Ui.MenuEntry>();

            foreach (var cost in config.etabliCost ?? new List<ItemStack>())
            {
                var itemId = Utils.ResolveItemId(cost.slug, cost.itemId);
                if (itemId <= 0) { continue; }

                var have = Utils.CountItem(player, itemId);
                var enough = have >= cost.quantity;
                if (!enough) { missing.Add((cost.quantity - have) + " × " + Utils.ItemName(itemId)); }

                // Chaque ingrédient : icône item, quantité possédée / requise, état couleur.
                entries.Add(new Ui.MenuEntry(
                    (enough ? Ui.Ok("✓ ") : Ui.Bad("✕ "))
                    + cost.quantity + " × " + Utils.ItemName(itemId)
                    + Ui.Dim("  (" + have + " sur vous)"),
                    itemId,
                    have + "/" + cost.quantity,
                    null));
            }

            var alreadyPlaced = HasPlaced(Utils.SteamId(player)) && config.onePlacementPerPlayer;

            if (alreadyPlaced)
            {
                body += "\n" + Ui.Bad("✕ Vous avez déjà posé votre établi. Un seul autorisé par personne.");
            }
            else if (missing.Count > 0)
            {
                body += "\n" + Ui.Bad("✕ Il vous manque : " + string.Join(", ", missing.ToArray()));
                body += "\n" + Ui.Dim("Récoltez ces ressources puis revenez ici.");
            }
            else
            {
                body += "\n" + Ui.Ok("✓ Vous avez tout ce qu'il faut — prêt à fabriquer !");
                entries.Insert(0, new Ui.MenuEntry(
                    Ui.Ok("▶ FABRIQUER MON ÉTABLI"), _etabliItemId, "prêt",
                    () => CraftEtabli(player, point)));
            }

            Ui.Menu(player, point.name, body, entries, "Fermer", null);
        }

        private void CraftEtabli(Player player, StaffCraftPoint point)
        {
            var prefix = _plugin.Prefix;
            var config = _plugin.Config.etabli;

            if (!Configured)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ La fabrication d'etabli n'est pas configuree."));
                return;
            }

            if (Utils.Distance(player, point.position.ToVector3()) > config.interactionRadius + 1f)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Rapprochez-vous du point de fabrication."));
                return;
            }

            var costs = (config.etabliCost ?? new List<ItemStack>())
                .Select(c => new { Id = Utils.ResolveItemId(c.slug, c.itemId), Qty = c.quantity })
                .Where(c => c.Id > 0 && c.Qty > 0)
                .ToList();

            // Message explicite : « ressources insuffisantes » sans dire lesquelles est inutilisable.
            var missing = costs
                .Where(c => Utils.CountItem(player, c.Id) < c.Qty)
                .Select(c => (c.Qty - Utils.CountItem(player, c.Id)) + " × " + Utils.ItemName(c.Id))
                .ToList();

            if (missing.Count > 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Il vous manque : " + string.Join(", ", missing.ToArray()) + "."));
                return;
            }

            if (!Utils.CanGiveItem(player, _etabliItemId, 1))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Votre inventaire est trop plein."));
                return;
            }

            // Consommation puis remise : en cas d'echec de retrait, tout est rendu.
            var taken = new List<KeyValuePair<int, int>>();
            foreach (var cost in costs)
            {
                if (Utils.TakeItem(player, cost.Id, cost.Qty))
                {
                    taken.Add(new KeyValuePair<int, int>(cost.Id, cost.Qty));
                }
                else
                {
                    foreach (var back in taken) { Utils.GiveItem(player, back.Key, back.Value); }
                    Utils.Send(player, prefix + Ui.Bad("✕ Vous n'avez pas les ressources necessaires."));
                    return;
                }
            }

            if (!Utils.GiveItem(player, _etabliItemId, 1))
            {
                foreach (var back in taken) { Utils.GiveItem(player, back.Key, back.Value); }
                Utils.Send(player, prefix + Ui.Bad("✕ Votre inventaire est trop plein."));
                return;
            }

            Utils.Send(player, prefix + Ui.Ok("✓ Etabli fabrique. Utilisez /etabli pose pour l'installer."));
            _plugin.Webhook.LogCraft(Utils.Name(player), Utils.SteamId(player), "Etabli personnel", true);
        }

        // ------------------------------------------------------------------ pose

        public bool HasPlaced(string steamId)
        {
            return !string.IsNullOrEmpty(steamId)
                   && _data.playersWhoPlaced.Any(s => string.Equals(s, steamId, StringComparison.OrdinalIgnoreCase));
        }

        public PlacedEtabli PlacedOf(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) { return null; }
            return _data.placed.FirstOrDefault(
                e => e != null && string.Equals(e.ownerSteamId, steamId, StringComparison.OrdinalIgnoreCase));
        }

        public void Place(Player player)
        {
            var prefix = _plugin.Prefix;
            var steamId = Utils.SteamId(player);
            var config = _plugin.Config.etabli;

            if (string.IsNullOrEmpty(steamId))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Identifiant indisponible, reconnectez-vous."));
                return;
            }

            if (!Configured)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ La pose d'etabli n'est pas configuree sur ce serveur."));
                return;
            }

            if (config.onePlacementPerPlayer && HasPlaced(steamId))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous avez deja pose votre etabli."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), steamId, "seconde pose d'etabli refusee");
                return;
            }

            if (Utils.CountItem(player, _etabliItemId) <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous n'avez pas d'etabli dans votre inventaire."));
                return;
            }

            var position = Utils.Position(player);
            if (position == Vector3.zero)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Position indisponible, reessayez dans un instant."));
                return;
            }

            var tooClose = _data.placed.FirstOrDefault(
                e => e != null && e.position != null
                     && Vector3.Distance(e.position.ToVector3(), position) < config.minDistanceBetweenEtablis);

            if (tooClose != null)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Un autre etabli est deja installe trop pres d'ici."));
                return;
            }

            if (!Utils.TakeItem(player, _etabliItemId, 1))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Impossible de retirer l'etabli de votre inventaire."));
                return;
            }

            var created = new PlacedEtabli
            {
                ownerSteamId = steamId,
                ownerName = Utils.Name(player),
                position = new Position(position),
                placedAt = Utils.NowUnix(),
            };

            _data.placed.Add(created);

            var markedNow = !HasPlaced(steamId);
            if (markedNow) { _data.playersWhoPlaced.Add(steamId); }

            if (!Save())
            {
                // Sauvegarde impossible : on annule exactement ce qui vient d'etre ajoute
                // et on rend l'item, plutot que de le faire disparaitre.
                _data.placed.Remove(created);
                if (markedNow) { _data.playersWhoPlaced.Remove(steamId); }

                Utils.GiveItem(player, _etabliItemId, 1);
                Utils.Send(player, prefix + Ui.Bad("✕ Sauvegarde impossible, l'etabli vous a ete rendu."));
                return;
            }

            SpawnPhysicalObject(player, position);
            _plugin.Checkpoints.RefreshAll();

            Utils.Send(player, prefix + Ui.Ok("✓ Etabli installe. Il restera ici, meme apres un redemarrage."));
            _plugin.Webhook.LogStaffAction(Utils.Name(player), steamId,
                "a pose son etabli en " + Format(position));
        }

        private void OpenPlacedEtabli(Player player, PlacedEtabli etabli)
        {
            if (player == null || etabli == null) { return; }

            if (!CanUse(player, etabli))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Cet établi appartient à " + etabli.ownerName + "."));
                return;
            }

            var mine = string.Equals(Utils.SteamId(player), etabli.ownerSteamId, StringComparison.OrdinalIgnoreCase);
            var ownerDistrict = _plugin.Districts.DistrictOf(etabli.ownerSteamId);

            var title = mine ? "Votre établi" : "Établi de " + Utils.Sanitize(etabli.ownerName, 22);

            var header = (mine
                    ? Ui.Ok("Votre établi personnel.")
                    : Ui.Accent("Établi de " + etabli.ownerName)
                      + (ownerDistrict != null ? Ui.Dim("  (" + ownerDistrict.name + ")") : ""))
                + "\n" + Ui.Dim("Recettes de base accessibles ici. Les pièces avancées "
                                + "se fabriquent à l'atelier spécialisé de votre district.")
                + "\n" + Ui.Dim("Cliquez une recette pour voir les ressources requises.");

            var recipes = _plugin.Craft.GenericRecipes().ToList();

            _plugin.Craft.OpenMenu(player, title, header, recipes, etabli.position.ToVector3());
        }

        /// <summary>Retire l'etabli d'un joueur (commande staff). Ne rend pas la pose : elle reste consommee.</summary>
        public bool RemovePlaced(string steamId, bool alsoResetPlacement)
        {
            var etabli = PlacedOf(steamId);
            if (etabli == null) { return false; }

            _data.placed.Remove(etabli);
            if (alsoResetPlacement)
            {
                _data.playersWhoPlaced.RemoveAll(s => string.Equals(s, steamId, StringComparison.OrdinalIgnoreCase));
            }

            Save();
            _plugin.Checkpoints.RefreshAll();
            return true;
        }

        // ------------------------------------------------------------------ points staff

        public StaffCraftPoint AddStaffPoint(Vector3 position, string name, Player placer)
        {
            var point = new StaffCraftPoint
            {
                id = _data.staffPoints.Count == 0 ? 1 : _data.staffPoints.Max(p => p.id) + 1,
                name = string.IsNullOrWhiteSpace(name) ? "Atelier communal" : Utils.Sanitize(name, 32),
                position = new Position(position),
            };

            _data.staffPoints.Add(point);
            Save();

            SpawnPhysicalObject(placer, position);
            _plugin.Checkpoints.RefreshAll();
            return point;
        }

        public bool RemoveStaffPoint(int id)
        {
            var point = _data.staffPoints.FirstOrDefault(p => p != null && p.id == id);
            if (point == null) { return false; }

            _data.staffPoints.Remove(point);
            Save();
            _plugin.Checkpoints.RefreshAll();
            return true;
        }

        /// <summary>Supprime le point staff le plus proche du joueur (usage : /etabli_point remove).</summary>
        public StaffCraftPoint RemoveNearest(Vector3 position, float maxDistance)
        {
            var nearest = _data.staffPoints
                .Where(p => p != null && p.position != null)
                .OrderBy(p => Vector3.Distance(p.position.ToVector3(), position))
                .FirstOrDefault();

            if (nearest == null) { return null; }
            if (Vector3.Distance(nearest.position.ToVector3(), position) > maxDistance) { return null; }

            _data.staffPoints.Remove(nearest);
            Save();
            _plugin.Checkpoints.RefreshAll();
            return nearest;
        }

        /// <summary>
        /// Pose un vrai objet du jeu a l'emplacement d'un etabli.
        ///
        /// Nova-Life rattache les objets a une zone : on reprend celle ou se trouve le poseur
        /// (<c>CharacterSetup.areaId</c>). <c>AreaManager.CreateObject</c> ne renvoie rien, donc le
        /// plugin ne peut pas retrouver l'objet plus tard : la suppression d'un point d'etabli
        /// retire le checkpoint, pas le meuble (voir README, section Limites).
        ///
        /// L'echec de cette pose est sans consequence : l'etabli reste utilisable via son
        /// checkpoint, seul le decor manque.
        /// </summary>
        public void SpawnPhysicalObject(Player placer, Vector3 position)
        {
            var config = _plugin.Config.etabli;
            if (!config.spawnPhysicalObject || config.physicalObjectItemId <= 0) { return; }

            try
            {
                var manager = Nova.a;
                if (manager == null || placer == null || placer.setup == null) { return; }

                if (!Utils.ItemExists(config.physicalObjectItemId))
                {
                    Utils.Warn("objet d'etabli introuvable (id " + config.physicalObjectItemId
                               + ") : aucun meuble ne sera pose.");
                    return;
                }

                var setup = (Component)placer.setup;
                var forward = setup.transform.forward;

                // Devant le joueur plutot qu'a ses pieds : on evite qu'il apparaisse dans son dos
                // ou a l'interieur de son personnage.
                var target = position + forward.normalized * Mathf.Max(0f, config.physicalObjectForwardOffset);

                // L'objet regarde le joueur.
                var rotation = new Vector3(0f, setup.transform.eulerAngles.y + 180f, 0f);

                manager.CreateObject(
                    config.physicalObjectItemId,
                    (int)placer.setup.areaId,
                    target,
                    rotation,
                    false,
                    Utils.SteamId(placer),
                    string.Empty,
                    string.Empty);

                Utils.Log("objet d'etabli pose (item " + config.physicalObjectItemId
                          + ", zone " + placer.setup.areaId + ").");
            }
            catch (Exception ex)
            {
                Utils.Warn("pose de l'objet d'etabli impossible : " + ex.Message
                           + " — le point reste fonctionnel.");
            }
        }

        public static string Format(Vector3 position)
        {
            return position.x.ToString("0.0") + " / " + position.y.ToString("0.0") + " / " + position.z.ToString("0.0");
        }
    }
}
