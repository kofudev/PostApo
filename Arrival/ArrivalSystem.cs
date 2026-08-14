using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.Network;
using PostApo.Core;
using PostApo.District;
using UnityEngine;

namespace PostApo.Arrival
{
    public sealed class ArrivalSystem
    {
        private readonly PostApoPlugin _plugin;
        private readonly JsonStore<ArrivalData> _arrivalStore;
        private readonly JsonStore<WelcomeData> _welcomeStore;

        private ArrivalData _arrival;
        private WelcomeData _welcome;

        private readonly HashSet<string> _inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ArrivalSystem(PostApoPlugin plugin, string root)
        {
            _plugin = plugin;
            _arrivalStore = new JsonStore<ArrivalData>(root, "arrival_data.json");
            _welcomeStore = new JsonStore<WelcomeData>(root, "welcome_data.json");
            Reload();
        }

        public IEnumerable<ArrivalPoint> Points { get { return _arrival.arrivalPoints ?? new List<ArrivalPoint>(); } }

        public void Reload()
        {
            _arrival = _arrivalStore.Load();
            if (_arrival.arrivalPoints == null) { _arrival.arrivalPoints = new List<ArrivalPoint>(); }
            if (_arrival.playersInitialized == null) { _arrival.playersInitialized = new List<string>(); }

            _welcome = _welcomeStore.Load();
            if (_welcome.playersWelcomeRewarded == null) { _welcome.playersWelcomeRewarded = new List<string>(); }

            SaveArrival();
            SaveWelcome();
        }

        private bool SaveArrival() { return _arrivalStore.Save(_arrival); }
        private bool SaveWelcome() { return _welcomeStore.Save(_welcome); }

        public bool IsInitialized(string steamId)
        {
            return !string.IsNullOrEmpty(steamId)
                   && _arrival.playersInitialized.Any(s => string.Equals(s, steamId, StringComparison.OrdinalIgnoreCase));
        }

        public bool WasRewarded(string steamId)
        {
            return !string.IsNullOrEmpty(steamId)
                   && _welcome.playersWelcomeRewarded.Any(s => string.Equals(s, steamId, StringComparison.OrdinalIgnoreCase));
        }

        private void MarkInitialized(string steamId)
        {
            if (string.IsNullOrEmpty(steamId) || IsInitialized(steamId)) { return; }
            _arrival.playersInitialized.Add(steamId);
            SaveArrival();
        }

        public bool ResetPlayer(string steamId, bool alsoReward)
        {
            if (string.IsNullOrEmpty(steamId)) { return false; }

            var removed = _arrival.playersInitialized
                .RemoveAll(s => string.Equals(s, steamId, StringComparison.OrdinalIgnoreCase)) > 0;

            if (alsoReward)
            {
                removed |= _welcome.playersWelcomeRewarded
                    .RemoveAll(s => string.Equals(s, steamId, StringComparison.OrdinalIgnoreCase)) > 0;
                SaveWelcome();
            }

            SaveArrival();
            return removed;
        }

        public void OnSpawn(Player player)
        {
            if (player == null || !_plugin.Config.arrival.enabled) { return; }

            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId)) { return; }

            if (IsInitialized(steamId) || _inProgress.Contains(steamId)) { return; }

            _inProgress.Add(steamId);

            var host = LifeManager.instance as MonoBehaviour;
            if (host == null)
            {
                Begin(player, steamId);
                return;
            }

            host.StartCoroutine(BeginDelayed(player, steamId));
        }

        public void OnDisconnect(string steamId)
        {
            if (!string.IsNullOrEmpty(steamId)) { _inProgress.Remove(steamId); }
        }

        private IEnumerator BeginDelayed(Player player, string steamId)
        {
            var delay = Mathf.Max(0.5f, _plugin.Config.arrival.delayBeforeIntroSeconds);
            yield return new WaitForSeconds(delay);

            if (player == null || player.setup == null)
            {
                _inProgress.Remove(steamId);
                yield break;
            }

            Begin(player, steamId);
        }

        private void Begin(Player player, string steamId)
        {
            try
            {
                _plugin.Webhook.LogPlayerArrival(Utils.Name(player), steamId);

                if (_plugin.Config.arrival.randomArrivalEnabled)
                {
                    TeleportToRandomPoint(player);
                }

                if (_plugin.Config.arrival.introductionEnabled)
                {
                    ShowIntroduction(player, steamId);
                }
                else
                {
                    AskDistrict(player, steamId);
                }
            }
            catch (Exception ex)
            {
                Utils.Error("parcours d'arrivee : " + ex);
                _plugin.Webhook.LogError("ArrivalSystem.Begin", ex);
                _inProgress.Remove(steamId);
            }
        }

        private void TeleportToRandomPoint(Player player)
        {
            var points = _arrival.arrivalPoints;
            if (points == null || points.Count == 0)
            {
                Utils.Warn("aucun point d'arrivee configure : le joueur apparait a l'endroit par defaut du serveur. "
                           + "Utilisez /spawn_arrivee set.");
                return;
            }

            var point = Utils.PickRandom(points);
            if (point == null) { return; }

            var destination = new Vector3(point.x, point.y, point.z) + Vector3.up * 0.5f;
            if (Utils.Teleport(player, destination))
            {
                _plugin.Webhook.LogTeleport(Utils.Name(player), Utils.SteamId(player),
                    "point d'arrivee #" + point.id + (string.IsNullOrWhiteSpace(point.name) ? "" : " (" + point.name + ")"));
            }
        }

        private void ShowIntroduction(Player player, string steamId)
        {
            var lines = _plugin.Config.arrival.introductionText ?? new List<string>();
            if (lines.Count == 0)
            {
                AskDistrict(player, steamId);
                return;
            }

            Ui.LongText(player, _plugin.Config.arrival.introTitle,
                string.Join("\n", lines.ToArray()),
                "J'ai compris",
                () => AskDistrict(player, steamId));
        }

        private void AskDistrict(Player player, string steamId)
        {
            var districts = _plugin.Districts.All.OrderBy(d => d.id).ToList();
            if (districts.Count == 0)
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Aucun district n'est configure. Prevenez le staff."));
                Finish(player, steamId, null, false, new List<string>(), "aucune");
                return;
            }

            var entries = new List<Ui.MenuEntry>();
            foreach (var district in districts)
            {
                var captured = district;
                var specialites = captured.specialites != null && captured.specialites.Count > 0
                    ? "  " + Ui.Dim("[" + string.Join(", ", captured.specialites.ToArray()) + "]")
                    : string.Empty;

                entries.Add(new Ui.MenuEntry(captured.name + specialites,
                    () => ShowDistrictDetail(player, steamId, captured)));
            }

            var mandatory = _plugin.Config.arrival.districtChoiceMandatory;
            var body = Ui.Dim("Choisissez avec soin : votre district determine vos ressources, "
                              + "vos ateliers et vos alliances.");

            Ui.Menu(player, _plugin.Config.arrival.districtChoiceTitle, body, entries,
                mandatory ? null : "Decider plus tard",
                mandatory ? (Action)null : () => Finish(player, steamId, null, false, new List<string>(), "aucune"));
        }

        private void ShowDistrictDetail(Player player, string steamId, PostApo.District.District district)
        {
            var body = "<b>" + district.name + "</b>\n";

            if (!string.IsNullOrWhiteSpace(district.description))
            {
                body += "\n" + district.description + "\n";
            }

            if (district.specialites != null && district.specialites.Count > 0)
            {
                body += "\nSpecialites : " + Ui.Accent(string.Join(", ", district.specialites.ToArray()));
            }

            body += "\nMembres : " + (district.members != null ? district.members.Count : 0);
            body += "\nBase : " + (district.HasBase ? Ui.Ok("configuree") : Ui.Dim("aucune"));

            var entries = new List<Ui.MenuEntry>
            {
                new Ui.MenuEntry(Ui.Ok("Rejoindre ce district"), () => JoinDistrict(player, steamId, district)),
                new Ui.MenuEntry("Retour", () => AskDistrict(player, steamId)),
            };

            Ui.Menu(player, district.name, body, entries, null, null);
        }

        private void JoinDistrict(Player player, string steamId, PostApo.District.District district)
        {
            string error;
            if (!_plugin.Districts.Join(player, district, out error))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ " + (error ?? "Impossible de rejoindre ce district.")));
                AskDistrict(player, steamId);
                return;
            }

            _plugin.Webhook.LogDistrictSelection(Utils.Name(player), steamId, district.name);
            Utils.Send(player, _plugin.Prefix + Ui.Ok("✓ Vous avez rejoint le " + district.name + "."));

            MarkInitialized(steamId);

            var rewards = GiveWelcomeRewards(player, steamId);
            AskBaseTeleport(player, steamId, district, rewards);
        }

        public List<string> GiveWelcomeRewards(Player player, string steamId)
        {
            var given = new List<string>();

            if (string.IsNullOrEmpty(steamId)) { return given; }

            if (WasRewarded(steamId))
            {
                _plugin.Webhook.LogAbuse(Utils.Name(player), steamId, "recompense de bienvenue deja percue");
                return given;
            }

            var rewards = _plugin.Config.welcomeRewards ?? new List<ItemStack>();
            if (rewards.Count == 0) { return given; }

            _welcome.playersWelcomeRewarded.Add(steamId);
            SaveWelcome();

            foreach (var reward in rewards)
            {
                if (reward == null || reward.quantity <= 0) { continue; }

                var itemId = Utils.ResolveItemId(reward.slug, reward.itemId);
                if (itemId <= 0 || !Utils.ItemExists(itemId))
                {
                    Utils.Warn("recompense de bienvenue ignoree : item introuvable ("
                               + (string.IsNullOrWhiteSpace(reward.slug) ? "id " + reward.itemId : reward.slug) + ").");
                    continue;
                }

                if (Utils.GiveItem(player, itemId, reward.quantity))
                {
                    given.Add(reward.quantity + " × " + Utils.ItemName(itemId));
                }
                else
                {
                    Utils.Warn("remise de " + reward.quantity + " × " + Utils.ItemName(itemId) + " impossible (inventaire plein ?).");
                }
            }

            if (given.Count > 0)
            {
                Utils.Send(player, _plugin.Prefix + Ui.Ok("✓ Vos ressources de bienvenue ont ete ajoutees.")
                                   + "\n" + Ui.Dim(string.Join(", ", given.ToArray())));
                _plugin.Webhook.LogWelcomeReward(Utils.Name(player), steamId, given);
            }

            return given;
        }

        private void AskBaseTeleport(Player player, string steamId, PostApo.District.District district, List<string> rewards)
        {
            var config = _plugin.Config.arrival;

            if (!district.HasBase)
            {
                Ui.Text(player, district.name,
                    Ui.Bad("Ce district ne possede actuellement aucune base configuree.")
                    + "\n\n" + Ui.Dim("Vous restez la ou vous etes. Debrouillez-vous."),
                    "Continuer",
                    () => Finish(player, steamId, district, false, rewards, "aucune (pas de base configuree)"));
                return;
            }

            Ui.Confirm(player, district.name, config.baseChoiceTitle,
                config.baseChoiceYes, config.baseChoiceNo,
                () =>
                {
                    _plugin.Districts.TeleportToBase(player, district, false);
                    Finish(player, steamId, district, true, rewards, "Base du " + district.name);
                },
                () =>
                {
                    Utils.Send(player, _plugin.Prefix + Ui.Dim("Vous restez sur place."));
                    _plugin.Webhook.LogTeleport(Utils.Name(player), steamId, "refus de la teleportation vers la base");
                    Finish(player, steamId, district, true, rewards, "aucune (refus du joueur)");
                });
        }

        private void Finish(Player player, string steamId, PostApo.District.District district,
                            bool hasBase, List<string> rewards, string teleport)
        {
            _inProgress.Remove(steamId);
            MarkInitialized(steamId);

            _plugin.Webhook.LogArrivalSummary(
                Utils.Name(player), steamId,
                district != null ? district.name : "aucun",
                hasBase, rewards, teleport);

            _plugin.Checkpoints.Refresh(player);
        }

        public ArrivalPoint AddPoint(Vector3 position, string name)
        {
            var point = new ArrivalPoint
            {
                id = _arrival.arrivalPoints.Count == 0 ? 1 : _arrival.arrivalPoints.Max(p => p.id) + 1,
                name = Utils.Sanitize(name, 32),
                x = position.x,
                y = position.y,
                z = position.z,
            };

            _arrival.arrivalPoints.Add(point);
            SaveArrival();
            return point;
        }

        public bool RemovePoint(int id)
        {
            var point = _arrival.arrivalPoints.FirstOrDefault(p => p != null && p.id == id);
            if (point == null) { return false; }

            _arrival.arrivalPoints.Remove(point);
            return SaveArrival();
        }

        public ArrivalPoint GetPoint(int id)
        {
            return _arrival.arrivalPoints.FirstOrDefault(p => p != null && p.id == id);
        }
    }
}
