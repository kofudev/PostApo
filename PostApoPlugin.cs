using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Life;
using Life.DB;
using Life.Network;
using Mirror;
using PostApo.Arrival;
using PostApo.Core;
using PostApo.District;
using PostApo.Etabli;
using PostApo.Farm;
using PostApo.Spawn;
using PostApo.Vehicle;
using UnityEngine;
using DistrictEntity = PostApo.District.District;
// Mirror expose lui aussi un type « Utils » : on leve l'ambiguite explicitement.
using Utils = PostApo.Core.Utils;

namespace PostApo
{
    /// <summary>
    /// Point d'entree du plugin. Ne contient que l'orchestration : chargement, callbacks natifs,
    /// boucle interne et routage des commandes. Toute la logique metier vit dans les modules.
    /// </summary>
    public sealed class PostApoPlugin : Plugin, IDisposable
    {
        public const string Version = "1.0.0";

        private bool _initialized;
        private bool _disposed;

        /// <summary>Passe a true une fois les items du serveur resolus (voir <see cref="Utils.ItemsReady"/>).</summary>
        private bool _itemsResolved;

        private string _root;
        private JsonStore<Config> _configStore;
        private Coroutine _loop;

        private readonly CommandRegistry _commands = new CommandRegistry();

        public Config Config { get; private set; }
        public WebhookLogger Webhook { get; private set; }
        public CheckpointService Checkpoints { get; private set; }

        public ArrivalSystem Arrival { get; private set; }
        public DistrictSystem Districts { get; private set; }
        public CraftEngine Craft { get; private set; }
        public EtabliSystem Etabli { get; private set; }
        public FarmSystem Farm { get; private set; }
        public SpawnSystem Spawn { get; private set; }
        public VehicleSystem Vehicles { get; private set; }

        public string Prefix
        {
            get { return Config != null ? (Config.chatPrefix ?? string.Empty) : string.Empty; }
        }

        public PostApoPlugin(IGameAPI api) : base(api)
        {
            Utils.Log("assembly chargee (v" + Version + "), en attente d'initialisation...");
        }

        // ------------------------------------------------------------------ cycle de vie

        public override void OnPluginInit()
        {
            base.OnPluginInit();

            if (_initialized) { return; }
            _initialized = true;

            try
            {
                _root = ResolveRoot();
                Directory.CreateDirectory(_root);

                _configStore = new JsonStore<Config>(_root, "config.json");
                Config = _configStore.Load();
                _configStore.Save(Config);
            }
            catch (Exception ex)
            {
                Utils.Error("chargement de la configuration impossible : " + ex);
                return;
            }

            Webhook = new WebhookLogger(Config.webhookUrl);
            Checkpoints = new CheckpointService();

            // L'ordre compte : le moteur de craft est utilise par l'etabli et par les districts.
            SafeInit("districts", () => Districts = new DistrictSystem(this, _root));
            SafeInit("craft", () => Craft = new CraftEngine(this, _root));
            SafeInit("etabli", () => Etabli = new EtabliSystem(this, _root));
            SafeInit("farm", () => Farm = new FarmSystem(this, _root));
            SafeInit("arrivee", () => Arrival = new ArrivalSystem(this, _root));
            SafeInit("pied de biche", () => Spawn = new SpawnSystem(this));
            SafeInit("vehicules", () => Vehicles = new VehicleSystem(this, _root));

            if (Etabli != null) { Checkpoints.AddProvider(() => Etabli.Points()); }
            if (Farm != null) { Checkpoints.AddProvider(() => Farm.Points()); }
            if (Districts != null) { Checkpoints.AddProvider(() => Districts.CraftPoints()); }
            if (Vehicles != null) { Checkpoints.AddProvider(() => Vehicles.Points()); }

            RegisterCommands();
            StartLoop();

            Utils.Log("actif — districts=" + (Districts != null ? Districts.All.Count() : 0)
                      + " recettes=" + (Craft != null ? Craft.ValidCount : 0)
                      + (Craft != null && Craft.InvalidCount > 0 ? " (" + Craft.InvalidCount + " desactivees)" : "")
                      + " gisements=" + (Farm != null ? Farm.ValidCount : 0)
                      + " points d'arrivee=" + (Arrival != null ? Arrival.Points.Count() : 0)
                      + " webhook=" + (Webhook.Enabled ? "ON" : "OFF"));

            Webhook.LogInfo("PostApo v" + Version + " demarre.");
        }

        private void SafeInit(string label, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Utils.Error("module " + label + " indisponible : " + ex);
                if (Webhook != null) { Webhook.LogError("init " + label, ex); }
            }
        }

        private void StartLoop()
        {
            try
            {
                var host = LifeManager.instance as MonoBehaviour;
                if (host == null)
                {
                    Utils.Warn("LifeManager indisponible : boucle interne non demarree.");
                    return;
                }

                _loop = host.StartCoroutine(TickLoop());
            }
            catch (Exception ex)
            {
                Utils.Error("demarrage de la boucle : " + ex.Message);
            }
        }

        private IEnumerator TickLoop()
        {
            var wait = new WaitForSeconds(1f);
            var seconds = 0;

            while (!_disposed)
            {
                yield return wait;
                seconds++;

                try
                {
                    if (!_commands.Registered && seconds % 5 == 0) { _commands.TryRegister(); }

                    // Les items du jeu ne sont pas encore charges quand les plugins s'initialisent :
                    // on rejoue la resolution des slugs des que le catalogue est disponible.
                    if (!_itemsResolved && Utils.ItemsReady()) { ResolveItems(); }

                    if (seconds % 30 == 0 && Farm != null) { Farm.Tick(); }

                    // Passage rapproche mais sans churn : Tick ne reconstruit que chez les joueurs
                    // dont l'environnement a reellement change (deplacement, nouveau point, droits).
                    if (seconds % 10 == 0 && Checkpoints != null) { Checkpoints.Tick(); }

                    var recheck = Config.crowbar.recheckIntervalSeconds;
                    if (Spawn != null && recheck > 0 && seconds % Mathf.Max(5, Mathf.RoundToInt(recheck)) == 0)
                    {
                        Spawn.EnsureAll();
                    }
                }
                catch (Exception ex)
                {
                    Utils.Warn("boucle interne : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Resout les slugs d'items une fois le catalogue du serveur peuple, puis reconstruit les
        /// checkpoints (des gisements ont pu devenir valides).
        /// </summary>
        private void ResolveItems()
        {
            _itemsResolved = true;

            try
            {
                // Les caches ont pu se remplir de « objet #123 » avant que le catalogue soit pret.
                Utils.ClearItemCaches();

                if (Craft != null) { Craft.Reload(); }
                if (Farm != null) { Farm.Reload(); }
                if (Etabli != null) { Etabli.Reload(); }
                if (Spawn != null) { Spawn.Reload(); }
                if (Vehicles != null) { Vehicles.Reload(); }
                if (Checkpoints != null) { Checkpoints.RefreshAll(); }

                Utils.Log("items du serveur resolus — recettes valides=" + (Craft != null ? Craft.ValidCount : 0)
                          + (Craft != null && Craft.InvalidCount > 0 ? " / desactivees=" + Craft.InvalidCount : "")
                          + " gisements=" + (Farm != null ? Farm.ValidCount : 0)
                          + " pied de biche=" + (Spawn != null && Spawn.Configured ? "OK" : "non configure"));
            }
            catch (Exception ex)
            {
                Utils.Error("resolution des items : " + ex);
                Webhook.LogError("ResolveItems", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;

            try
            {
                var host = LifeManager.instance as MonoBehaviour;
                if (host != null && _loop != null) { host.StopCoroutine(_loop); }
            }
            catch { }

            try { if (Craft != null) { Craft.AbortAll(); } } catch { }
            try { if (Checkpoints != null) { Checkpoints.ClearAll(); } } catch { }
            try { if (Webhook != null) { Webhook.Dispose(); } } catch { }

            GC.SuppressFinalize(this);
        }

        // ------------------------------------------------------------------ callbacks natifs

        public override void OnPlayerSpawnCharacter(Player player, NetworkConnection conn, Characters character)
        {
            base.OnPlayerSpawnCharacter(player, conn, character);

            try
            {
                if (Districts != null) { Districts.RefreshMemberIdentity(player); }
                if (Spawn != null) { Spawn.Ensure(player); }
                if (Arrival != null) { Arrival.OnSpawn(player); }

                var host = LifeManager.instance as MonoBehaviour;
                if (host != null) { host.StartCoroutine(RefreshSoon(player)); }
                else if (Checkpoints != null) { Checkpoints.Refresh(player); }
            }
            catch (Exception ex)
            {
                Utils.Warn("OnPlayerSpawnCharacter : " + ex.Message);
            }
        }

        private IEnumerator RefreshSoon(Player player)
        {
            yield return new WaitForSeconds(3f);
            if (player != null && player.setup != null && Checkpoints != null)
            {
                // Force : apres une apparition, le client n'a plus aucun checkpoint,
                // meme si le decor est identique a la derniere fois.
                Checkpoints.Refresh(player, true);
            }
        }

        public override void OnPlayerDeath(Player player)
        {
            base.OnPlayerDeath(player);

            try
            {
                var steamId = Utils.SteamId(player);
                if (Craft != null) { Craft.AbortFor(steamId); }
                if (Farm != null) { Farm.AbortFor(steamId); }

                // Le pied de biche est rendu apres la reapparition, pas au moment de la mort.
                var host = LifeManager.instance as MonoBehaviour;
                if (host != null) { host.StartCoroutine(EnsureAfterRespawn(player)); }
            }
            catch (Exception ex)
            {
                Utils.Warn("OnPlayerDeath : " + ex.Message);
            }
        }

        private IEnumerator EnsureAfterRespawn(Player player)
        {
            yield return new WaitForSeconds(8f);
            if (player != null && player.setup != null && Spawn != null)
            {
                Spawn.Ensure(player);
            }
        }

        public override void OnPlayerDisconnect(NetworkConnection conn)
        {
            base.OnPlayerDisconnect(conn);

            try
            {
                var player = Nova.server != null ? Nova.server.GetPlayer(conn) : null;
                if (player == null) { return; }

                var steamId = Utils.SteamId(player);
                if (Craft != null) { Craft.AbortFor(steamId); }
                if (Farm != null) { Farm.AbortFor(steamId); }
                if (Arrival != null) { Arrival.OnDisconnect(steamId); }
                if (Checkpoints != null) { Checkpoints.Clear(player); }
            }
            catch (Exception ex)
            {
                Utils.Warn("OnPlayerDisconnect : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ commandes

        private void RegisterCommands()
        {
            _commands.Add("postapo", new[] { "pa" },
                "/postapo status|reload|finditem <texte>|iteminfo|resetjoueur <steamid>|etablidel <steamid>",
                CmdPostApo);

            _commands.Add("spawn_arrivee", new[] { "spawnarrivee" },
                "/spawn_arrivee set [nom] | remove <id> | list | tp <id>",
                CmdSpawnArrivee);

            _commands.Add("district", new[] { "districts" },
                "/district [list|info <id>|create <id> <nom>|delete <id>|owner <id> <steamid>|spec <id> add|remove <specialite>|setgrade <joueur> <gradeId>|kick <joueur>|leave]",
                CmdDistrict);

            _commands.Add("district_base", null,
                "/district_base set <districtId> | remove <districtId> | teleport <districtId>",
                CmdDistrictBase);

            _commands.Add("district_craft", null,
                "/district_craft set <districtId> <specialite> | remove <districtId> <pointId> | list",
                CmdDistrictCraft);

            _commands.Add("etabli_point", null,
                "/etabli_point set [nom] | remove | list",
                CmdEtabliPoint);

            _commands.Add("etabli", null,
                "/etabli [pose]",
                CmdEtabli);

            _commands.Add("farm_point", new[] { "gisement" },
                "/farm_point set <slug|itemId> [nom] | epave <palier 1-5> [nom] | remove <id> | list | tp <id>",
                CmdFarmPoint);

            _commands.Add("plans", new[] { "plan" },
                "/plans — mes plans de vehicule et ce qu'ils ouvrent",
                (p, a) => { if (Vehicles != null) { Vehicles.OpenPlans(p); } });

            _commands.Add("epaves", new[] { "epave", "fouille" },
                "/epaves — boussole des epaves et caches connues",
                (p, a) => { if (Farm != null) { Farm.OpenCompass(p, PostApo.Farm.NodeKind.Epave); } });

            _commands.Add("gisements", new[] { "filons" },
                "/gisements — boussole des gisements connus",
                (p, a) => { if (Farm != null) { Farm.OpenCompass(p, PostApo.Farm.NodeKind.Gisement); } });

            _commands.Add("atelier", new[] { "chantier" },
                "/atelier — mes chantiers | set <districtId> [nom] | remove <id> | list | palier <districtId> <1-5>",
                CmdAtelier);

            _commands.Add("staffapo", new[] { "sa" },
                "/staffapo — menu staff PostApo (gestion en jeu)",
                CmdStaffApo);

            _commands.TryRegister();
        }

        private bool RequireStaff(Player player)
        {
            if (Utils.IsStaff(player, Config.staffLevelMin)) { return true; }

            Reply(player, Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
            Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player), "commande staff refusee");
            return false;
        }

        private void Reply(Player player, string message)
        {
            Utils.Send(player, Prefix + message);
        }

        private static string Arg(string[] args, int index)
        {
            return args != null && args.Length > index ? args[index] : string.Empty;
        }

        private static string Rest(string[] args, int from)
        {
            if (args == null || args.Length <= from) { return string.Empty; }
            return string.Join(" ", args.Skip(from).ToArray()).Trim();
        }

        // ---------------------------------------------------------- /postapo

        private void CmdPostApo(Player player, string[] args)
        {
            if (!RequireStaff(player)) { return; }

            switch (Arg(args, 0).ToLowerInvariant())
            {
                case "reload":
                    ReloadAll();
                    Reply(player, Ui.Ok("✓ Configuration et donnees rechargees."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player), "rechargement du plugin");
                    break;

                case "finditem":
                {
                    var query = Rest(args, 1);
                    if (query.Length < 2)
                    {
                        Reply(player, "Usage : /postapo finditem <texte>");
                        return;
                    }

                    var found = Utils.SearchItems(query, 25).ToList();
                    if (found.Count == 0)
                    {
                        Reply(player, Ui.Bad("Aucun item ne correspond a « " + query + " »."));
                        return;
                    }

                    var text = "Items correspondant a « " + query + " » :";
                    foreach (var item in found)
                    {
                        text += "\n  id " + item.id + "  slug " + Ui.Accent(item.slug ?? "-") + "  — " + item.itemName;
                    }

                    Reply(player, text);
                    break;
                }

                case "iteminfo":
                {
                    var inventory = Utils.InventoryOf(player);
                    if (inventory == null || inventory.items == null)
                    {
                        Reply(player, Ui.Bad("Inventaire indisponible."));
                        return;
                    }

                    var text = "Votre inventaire :";
                    var count = 0;
                    for (var i = 0; i < inventory.items.Count; i++)
                    {
                        var slot = inventory.items[i];
                        if (slot.itemId <= 0) { continue; }
                        text += "\n  id " + slot.itemId + " ×" + Math.Max(1, slot.number) + "  — " + Utils.ItemName(slot.itemId);
                        count++;
                    }

                    Reply(player, count == 0 ? "Inventaire vide." : text);
                    break;
                }

                case "resetjoueur":
                {
                    var steamId = Arg(args, 1);
                    if (string.IsNullOrWhiteSpace(steamId))
                    {
                        Reply(player, "Usage : /postapo resetjoueur <steamid>");
                        return;
                    }

                    var done = Arrival != null && Arrival.ResetPlayer(steamId.Trim(), true);
                    Reply(player, done
                        ? Ui.Ok("✓ Parcours d'arrivee et recompense reinitialises pour " + steamId + ".")
                        : Ui.Bad("✕ Ce SteamID n'avait aucun etat enregistre."));

                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "reinitialisation du parcours de " + steamId);
                    break;
                }

                case "etablidel":
                {
                    var steamId = Arg(args, 1);
                    if (string.IsNullOrWhiteSpace(steamId))
                    {
                        Reply(player, "Usage : /postapo etablidel <steamid>");
                        return;
                    }

                    var done = Etabli != null && Etabli.RemovePlaced(steamId.Trim(), true);
                    Reply(player, done
                        ? Ui.Ok("✓ Etabli supprime, la pose est rendue au joueur.")
                        : Ui.Bad("✕ Aucun etabli pose pour ce SteamID."));

                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "suppression de l'etabli de " + steamId);
                    break;
                }

                default:
                    ShowStatus(player);
                    break;
            }
        }

        private void ShowStatus(Player player)
        {
            var text = "<b>PostApo v" + Version + "</b>"
                       + "\nDossier : " + _root
                       + "\nWebhook : " + (Webhook.Enabled ? Ui.Ok("actif") : Ui.Dim("desactive"))
                       + "\nNiveau staff requis : " + Config.staffLevelMin
                       + "\n"
                       + "\nArrivee : " + (Config.arrival.enabled ? Ui.Ok("ON") : Ui.Dim("OFF"))
                       + " — " + (Arrival != null ? Arrival.Points.Count() : 0) + " point(s)"
                       + "\nDistricts : " + (Districts != null ? Districts.All.Count() : 0)
                       + " — " + (Districts != null ? Districts.All.Count(d => d.HasBase) : 0) + " base(s)"
                       + "\nRecettes : " + (Craft != null ? Craft.ValidCount : 0) + " valides"
                       + (Craft != null && Craft.InvalidCount > 0 ? Ui.Bad(" / " + Craft.InvalidCount + " desactivees") : "")
                       + "\nEtablis : " + (Etabli != null ? Etabli.Data.staffPoints.Count : 0) + " point(s) staff, "
                       + (Etabli != null ? Etabli.Data.placed.Count : 0) + " pose(s)"
                       + "\nGisements / epaves : " + (Farm != null ? Farm.ValidCount : 0)
                       + "\nPlans vehicule : " + (Vehicles != null ? Vehicles.ValidBlueprints : 0)
                       + " — ateliers " + (Vehicles != null ? Vehicles.Workshops.Count() : 0)
                       + ", chantiers " + (Vehicles != null ? Vehicles.ProjectCount : 0)
                       + "\nPied de biche : " + (Spawn != null && Spawn.Configured
                           ? Ui.Ok("item " + Spawn.ItemId) : Ui.Bad("non configure"))
                       + "\n"
                       + "\nDifficulte : craft ×" + Config.difficulty.craftTimeMultiplier
                       + ", echec " + Mathf.RoundToInt(Config.difficulty.craftFailureChance * 100f) + "%"
                       + ", rendement ×" + Config.difficulty.farmYieldMultiplier;

            Ui.Info(player, "PostApo — etat", text);
        }

        private void ReloadAll()
        {
            try
            {
                Config = _configStore.Load();
                Webhook.SetUrl(Config.webhookUrl);

                if (Districts != null) { Districts.Reload(); }
                if (Craft != null) { Craft.Reload(); }
                if (Etabli != null) { Etabli.Reload(); }
                if (Farm != null) { Farm.Reload(); }
                if (Arrival != null) { Arrival.Reload(); }
                if (Spawn != null) { Spawn.Reload(); }
                if (Vehicles != null) { Vehicles.Reload(); }
                if (Checkpoints != null) { Checkpoints.RefreshAll(); }
            }
            catch (Exception ex)
            {
                Utils.Error("rechargement : " + ex);
                Webhook.LogError("reload", ex);
            }
        }

        // ---------------------------------------------------------- /spawn_arrivee

        private void CmdSpawnArrivee(Player player, string[] args)
        {
            if (!RequireStaff(player)) { return; }
            if (Arrival == null) { Reply(player, Ui.Bad("Module d'arrivee indisponible.")); return; }

            switch (Arg(args, 0).ToLowerInvariant())
            {
                case "set":
                {
                    var point = Arrival.AddPoint(Utils.Position(player), Rest(args, 1));
                    Reply(player, Ui.Ok("✓ Point d'arrivee #" + point.id + " enregistre."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "ajout du point d'arrivee #" + point.id);
                    break;
                }

                case "remove":
                {
                    int id;
                    if (!int.TryParse(Arg(args, 1), out id))
                    {
                        Reply(player, "Usage : /spawn_arrivee remove <id>");
                        return;
                    }

                    Reply(player, Arrival.RemovePoint(id)
                        ? Ui.Ok("✓ Point d'arrivee #" + id + " supprime.")
                        : Ui.Bad("✕ Ce point n'existe pas."));
                    break;
                }

                case "tp":
                {
                    int id;
                    var point = int.TryParse(Arg(args, 1), out id) ? Arrival.GetPoint(id) : null;
                    if (point == null) { Reply(player, Ui.Bad("✕ Ce point n'existe pas.")); return; }

                    Utils.Teleport(player, new Vector3(point.x, point.y, point.z) + Vector3.up * 0.5f);
                    Reply(player, Ui.Ok("✓ Teleporte au point #" + id + "."));
                    break;
                }

                default:
                {
                    var points = Arrival.Points.ToList();
                    if (points.Count == 0)
                    {
                        Reply(player, "Aucun point d'arrivee. Placez-vous puis tapez /spawn_arrivee set");
                        return;
                    }

                    var text = "Points d'arrivee (" + points.Count + ") :";
                    foreach (var point in points)
                    {
                        text += "\n  #" + point.id
                                + (string.IsNullOrWhiteSpace(point.name) ? "" : " " + point.name)
                                + "  " + Ui.Dim(EtabliSystem.Format(new Vector3(point.x, point.y, point.z)));
                    }

                    Reply(player, text);
                    break;
                }
            }
        }

        // ---------------------------------------------------------- /district

        private void CmdDistrict(Player player, string[] args)
        {
            if (Districts == null) { Reply(player, Ui.Bad("Module districts indisponible.")); return; }

            var sub = Arg(args, 0).ToLowerInvariant();

            switch (sub)
            {
                case "":
                    OpenDistrictMenu(player);
                    return;

                case "list":
                {
                    var text = "Districts :";
                    foreach (var district in Districts.All.OrderBy(d => d.id))
                    {
                        text += "\n  #" + district.id + " " + district.name
                                + Ui.Dim("  membres=" + district.members.Count
                                         + " base=" + (district.HasBase ? "oui" : "non"));
                    }

                    Reply(player, text);
                    return;
                }

                case "info":
                {
                    int id;
                    var district = int.TryParse(Arg(args, 1), out id) ? Districts.Get(id) : Districts.DistrictOf(player);
                    if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

                    ShowDistrictInfo(player, district);
                    return;
                }

                case "leave":
                {
                    var district = Districts.DistrictOf(player);
                    if (district == null) { Reply(player, Ui.Bad("✕ Vous n'appartenez a aucun district.")); return; }

                    Ui.Confirm(player, district.name,
                        "Quitter definitivement le " + district.name + " ?\n"
                        + Ui.Dim("Vous perdrez l'acces aux terrains et vehicules partages."),
                        "Oui, partir", "Annuler",
                        () =>
                        {
                            Districts.Leave(player, district, false);
                            Reply(player, Ui.Ok("✓ Vous avez quitte le " + district.name + "."));
                            Checkpoints.Refresh(player);
                        }, null);
                    return;
                }
            }

            // Sous-commandes de gestion : staff ou droits de district.
            switch (sub)
            {
                case "create":
                {
                    if (!RequireStaff(player)) { return; }

                    int id;
                    if (!int.TryParse(Arg(args, 1), out id) || string.IsNullOrWhiteSpace(Rest(args, 2)))
                    {
                        Reply(player, "Usage : /district create <id> <nom>");
                        return;
                    }

                    string error;
                    if (!Districts.Create(id, Rest(args, 2), out error))
                    {
                        Reply(player, Ui.Bad("✕ " + error));
                        return;
                    }

                    Reply(player, Ui.Ok("✓ District #" + id + " cree."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player), "creation du district #" + id);
                    return;
                }

                case "delete":
                {
                    if (!RequireStaff(player)) { return; }

                    int id;
                    if (!int.TryParse(Arg(args, 1), out id))
                    {
                        Reply(player, "Usage : /district delete <id>");
                        return;
                    }

                    string error;
                    if (!Districts.Delete(id, out error))
                    {
                        Reply(player, Ui.Bad("✕ " + error));
                        return;
                    }

                    Reply(player, Ui.Ok("✓ District #" + id + " supprime."));
                    Checkpoints.RefreshAll();
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player), "suppression du district #" + id);
                    return;
                }

                case "owner":
                {
                    if (!RequireStaff(player)) { return; }

                    int id;
                    var steamId = Arg(args, 2);
                    if (!int.TryParse(Arg(args, 1), out id) || string.IsNullOrWhiteSpace(steamId))
                    {
                        Reply(player, "Usage : /district owner <id> <steamid>");
                        return;
                    }

                    var district = Districts.Get(id);
                    if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

                    var member = district.FindMember(steamId.Trim());
                    if (member == null)
                    {
                        Reply(player, Ui.Bad("✕ Ce joueur n'est pas membre du district."));
                        return;
                    }

                    district.ownerSteamId = member.steamId;
                    district.ownerCharacterId = member.characterId;

                    var top = district.HighestGrade();
                    if (top != null) { member.gradeId = top.id; }

                    Districts.Save();
                    Districts.SyncSharedProperties(district);

                    Reply(player, Ui.Ok("✓ " + member.name + " est desormais proprietaire du " + district.name + "."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "attribution du district #" + id + " a " + member.steamId);
                    return;
                }

                case "spec":
                {
                    if (!RequireStaff(player)) { return; }

                    int id;
                    var action = Arg(args, 2).ToLowerInvariant();
                    var specialite = Arg(args, 3).Trim().ToLowerInvariant();

                    if (!int.TryParse(Arg(args, 1), out id) || string.IsNullOrWhiteSpace(specialite)
                        || (action != "add" && action != "remove"))
                    {
                        Reply(player, "Usage : /district spec <id> add|remove <specialite>");
                        return;
                    }

                    var district = Districts.Get(id);
                    if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

                    if (action == "add")
                    {
                        if (district.HasSpecialite(specialite))
                        {
                            Reply(player, Ui.Bad("✕ Ce district possede deja cette specialite."));
                            return;
                        }

                        district.specialites.Add(specialite);
                        Reply(player, Ui.Ok("✓ Specialite « " + specialite + " » ajoutee au " + district.name + "."));
                    }
                    else
                    {
                        district.specialites.RemoveAll(s => string.Equals(s, specialite, StringComparison.OrdinalIgnoreCase));
                        Reply(player, Ui.Ok("✓ Specialite « " + specialite + " » retiree."));
                    }

                    Districts.Save();
                    return;
                }

                case "setgrade":
                {
                    var district = Districts.DistrictOf(player);
                    if (district == null) { Reply(player, Ui.Bad("✕ Vous n'appartenez a aucun district.")); return; }

                    if (!Districts.HasPermission(player, Perm.GererGrades) && !Utils.IsStaff(player, Config.staffLevelMin))
                    {
                        Reply(player, Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                        return;
                    }

                    int gradeId;
                    var target = Utils.FindOnline(Arg(args, 1));
                    if (target == null || !int.TryParse(Arg(args, 2), out gradeId))
                    {
                        Reply(player, "Usage : /district setgrade <joueur> <gradeId>");
                        return;
                    }

                    var member = district.FindMember(Utils.SteamId(target));
                    var grade = district.FindGrade(gradeId);

                    if (member == null) { Reply(player, Ui.Bad("✕ Ce joueur n'est pas membre du district.")); return; }
                    if (grade == null) { Reply(player, Ui.Bad("✕ Ce grade n'existe pas.")); return; }

                    member.gradeId = grade.id;
                    Districts.Save();
                    Districts.SyncSharedProperties(district);

                    Reply(player, Ui.Ok("✓ " + member.name + " est desormais " + grade.name + "."));
                    Utils.Send(target, Prefix + Ui.Ok("✓ Vous etes desormais " + grade.name + " du " + district.name + "."));
                    return;
                }

                case "kick":
                {
                    var district = Districts.DistrictOf(player);
                    if (district == null) { Reply(player, Ui.Bad("✕ Vous n'appartenez a aucun district.")); return; }

                    if (!Districts.HasPermission(player, Perm.ExpulserMembre) && !Utils.IsStaff(player, Config.staffLevelMin))
                    {
                        Reply(player, Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                        return;
                    }

                    var target = Utils.FindOnline(Arg(args, 1));
                    if (target == null) { Reply(player, "Usage : /district kick <joueur connecte>"); return; }

                    var targetSteam = Utils.SteamId(target);
                    if (district.IsOwner(targetSteam))
                    {
                        Reply(player, Ui.Bad("✕ Le proprietaire ne peut pas etre expulse."));
                        return;
                    }

                    if (!Districts.Leave(target, district, false))
                    {
                        Reply(player, Ui.Bad("✕ Ce joueur n'est pas membre du district."));
                        return;
                    }

                    Reply(player, Ui.Ok("✓ " + Utils.Name(target) + " a ete expulse."));
                    Utils.Send(target, Prefix + Ui.Bad("✕ Vous avez ete expulse du " + district.name + "."));
                    Checkpoints.Refresh(target);
                    return;
                }

                default:
                    Reply(player, "Usage : /district [list|info <id>|leave] — staff : create, delete, owner, spec");
                    return;
            }
        }

        private void ShowDistrictInfo(Player player, DistrictEntity district)
        {
            var body = "<b>" + district.name + "</b>\n";
            if (!string.IsNullOrWhiteSpace(district.description)) { body += "\n" + district.description + "\n"; }

            body += "\nSpecialites : " + (district.specialites.Count > 0
                ? string.Join(", ", district.specialites.ToArray()) : Ui.Dim("aucune"));
            body += "\nMembres : " + district.members.Count;
            body += "\nBase : " + (district.HasBase ? Ui.Ok("configuree") : Ui.Dim("aucune"));
            body += "\nAteliers : " + district.craftPoints.Count;

            if (Vehicles != null)
            {
                body += "\nPalier vehicule : " + Vehicles.UnlockedTier(district.id) + "/5"
                        + Ui.Dim("  (" + Vehicles.CompletedCount(district.id) + " termine(s))");
            }

            var owner = district.members.FirstOrDefault(m => district.IsOwner(m.steamId));
            body += "\nProprietaire : " + (owner != null ? owner.name : Ui.Dim("aucun"));

            body += "\n\nGrades :";
            foreach (var grade in district.grades.OrderByDescending(g => g.rank))
            {
                var count = district.members.Count(m => m.gradeId == grade.id);
                body += "\n  " + grade.name + Ui.Dim(" — " + count + " membre(s)");
            }

            Ui.Info(player, district.name, body);
        }

        private void OpenDistrictMenu(Player player)
        {
            var district = Districts.DistrictOf(player);

            if (district == null)
            {
                var entries = Districts.All.OrderBy(d => d.id).Select(d =>
                {
                    var captured = d;
                    return new Ui.MenuEntry(captured.name, () => ShowDistrictInfo(player, captured));
                }).ToList();

                Ui.Menu(player, "Districts d'Amboise",
                    Ui.Dim("Vous n'appartenez a aucun district."), entries, "Fermer", null);
                return;
            }

            var steamId = Utils.SteamId(player);
            var grade = district.GradeOf(steamId);
            var header = district.name + "\n"
                         + Ui.Dim("Votre grade : " + (grade != null ? grade.name : "?"));

            var menu = new List<Ui.MenuEntry>
            {
                new Ui.MenuEntry("Informations", 1202, "", () => ShowDistrictInfo(player, district)),
                new Ui.MenuEntry("Membres", 1181, district.members.Count.ToString(),
                    () => ShowMembers(player, district)),
            };

            if (district.HasBase && Districts.HasPermission(player, Perm.TeleportBase))
            {
                menu.Add(new Ui.MenuEntry(Ui.Accent("Rejoindre la base"), 1077, "",
                    () => Districts.TeleportToBase(player, district, true)));
            }

            // Gestion des grades : reservee au proprietaire (ou a qui il delegue).
            if (district.IsOwner(steamId)
                || Districts.HasPermission(player, Perm.GererGrades)
                || Utils.IsStaff(player, Config.staffLevelMin))
            {
                menu.Add(new Ui.MenuEntry(Ui.Accent("Gerer les grades"), 1213, "",
                    () => Districts.Grades.Open(player, district)));
            }

            menu.Add(new Ui.MenuEntry(Ui.Bad("Quitter le district"), 1580, "",
                () => CmdDistrict(player, new[] { "leave" })));

            Ui.Menu(player, "Mon district", header, menu, "Fermer", null);
        }

        private void ShowMembers(Player player, DistrictEntity district)
        {
            var body = district.name + "\n";

            foreach (var member in district.members
                         .OrderByDescending(m => { var g = district.FindGrade(m.gradeId); return g != null ? g.rank : 0; })
                         .Take(40))
            {
                var grade = district.FindGrade(member.gradeId);
                var online = Utils.FindOnlineBySteamId(member.steamId) != null;

                body += "\n" + (online ? Ui.Ok("● ") : Ui.Dim("○ "))
                        + member.name + Ui.Dim("  — " + (grade != null ? grade.name : "?"))
                        + (district.IsOwner(member.steamId) ? Ui.Accent("  ★") : string.Empty);
            }

            Ui.Info(player, "Membres", body);
        }

        // ---------------------------------------------------------- /district_base

        private void CmdDistrictBase(Player player, string[] args)
        {
            if (Districts == null) { Reply(player, Ui.Bad("Module districts indisponible.")); return; }

            var action = Arg(args, 0).ToLowerInvariant();

            int id;
            if (!int.TryParse(Arg(args, 1), out id))
            {
                Reply(player, "Usage : /district_base set|remove|teleport <districtId>");
                return;
            }

            var district = Districts.Get(id);
            if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

            switch (action)
            {
                case "set":
                    if (!RequireStaff(player)) { return; }
                    Districts.SetBase(district, Utils.Position(player));
                    Reply(player, Ui.Ok("✓ Base du " + district.name + " enregistree a votre position."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "base du district #" + id + " definie");
                    break;

                case "remove":
                    if (!RequireStaff(player)) { return; }
                    Districts.RemoveBase(district);
                    Reply(player, Ui.Ok("✓ Base du " + district.name + " supprimee."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "base du district #" + id + " supprimee");
                    break;

                case "teleport":
                {
                    // Le staff se teleporte partout ; un joueur uniquement vers la base de son district.
                    var isStaff = Utils.IsStaff(player, Config.staffLevelMin);
                    var ownDistrict = Districts.DistrictOf(player);

                    if (!isStaff && (ownDistrict == null || ownDistrict.id != district.id))
                    {
                        Reply(player, Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                        return;
                    }

                    Districts.TeleportToBase(player, district, !isStaff);
                    break;
                }

                default:
                    Reply(player, "Usage : /district_base set|remove|teleport <districtId>");
                    break;
            }
        }

        // ---------------------------------------------------------- /district_craft

        private void CmdDistrictCraft(Player player, string[] args)
        {
            if (!RequireStaff(player)) { return; }
            if (Districts == null) { Reply(player, Ui.Bad("Module districts indisponible.")); return; }

            switch (Arg(args, 0).ToLowerInvariant())
            {
                case "set":
                {
                    int id;
                    var specialite = Arg(args, 2);
                    if (!int.TryParse(Arg(args, 1), out id) || string.IsNullOrWhiteSpace(specialite))
                    {
                        Reply(player, "Usage : /district_craft set <districtId> <specialite>"
                                      + "\nSpecialites connues : " + string.Join(", ", Craft.KnownSpecialites().ToArray()));
                        return;
                    }

                    var district = Districts.Get(id);
                    if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

                    DistrictCraftPoint created;
                    if (!Districts.AddCraftPoint(district, specialite, Utils.Position(player), out created))
                    {
                        Reply(player, Ui.Bad("✕ Creation impossible."));
                        return;
                    }

                    Checkpoints.RefreshAll();

                    var recipeCount = Craft.RecipesForSpecialite(created.specialite).Count();
                    Reply(player, Ui.Ok("✓ Atelier « " + created.specialite + " » #" + created.id
                                        + " cree pour le " + district.name + ".")
                                  + "\n" + Ui.Dim(recipeCount + " recette(s) disponible(s) a ce point."));

                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "atelier " + created.specialite + " ajoute au district #" + id);
                    break;
                }

                case "remove":
                {
                    int id, pointId;
                    if (!int.TryParse(Arg(args, 1), out id) || !int.TryParse(Arg(args, 2), out pointId))
                    {
                        Reply(player, "Usage : /district_craft remove <districtId> <pointId>");
                        return;
                    }

                    var district = Districts.Get(id);
                    if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

                    if (!Districts.RemoveCraftPoint(district, pointId))
                    {
                        Reply(player, Ui.Bad("✕ Cet atelier n'existe pas."));
                        return;
                    }

                    Checkpoints.RefreshAll();
                    Reply(player, Ui.Ok("✓ Atelier #" + pointId + " supprime."));
                    break;
                }

                default:
                {
                    var text = "Ateliers de district :";
                    var any = false;

                    foreach (var district in Districts.All.OrderBy(d => d.id))
                    {
                        foreach (var point in district.craftPoints)
                        {
                            any = true;
                            text += "\n  #" + district.id + "/" + point.id + " " + district.name
                                    + " — " + Ui.Accent(point.specialite)
                                    + Ui.Dim("  " + EtabliSystem.Format(point.position.ToVector3()));
                        }
                    }

                    Reply(player, any ? text : "Aucun atelier de district.");
                    break;
                }
            }
        }

        // ---------------------------------------------------------- /etabli_point

        private void CmdEtabliPoint(Player player, string[] args)
        {
            if (!RequireStaff(player)) { return; }
            if (Etabli == null) { Reply(player, Ui.Bad("Module etabli indisponible.")); return; }

            switch (Arg(args, 0).ToLowerInvariant())
            {
                case "set":
                {
                    var point = Etabli.AddStaffPoint(Utils.Position(player), Rest(args, 1), player);
                    Reply(player, Ui.Ok("✓ Point de fabrication #" + point.id + " (« " + point.name + " ») cree."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "point d'etabli #" + point.id + " cree");
                    break;
                }

                case "remove":
                {
                    int id;
                    if (int.TryParse(Arg(args, 1), out id))
                    {
                        Reply(player, Etabli.RemoveStaffPoint(id)
                            ? Ui.Ok("✓ Point #" + id + " supprime.")
                            : Ui.Bad("✕ Ce point n'existe pas."));
                        return;
                    }

                    var nearest = Etabli.RemoveNearest(Utils.Position(player), 10f);
                    Reply(player, nearest != null
                        ? Ui.Ok("✓ Point #" + nearest.id + " (« " + nearest.name + " ») supprime.")
                        : Ui.Bad("✕ Aucun point a moins de 10 m. Precisez un id."));
                    break;
                }

                default:
                {
                    var points = Etabli.Data.staffPoints;
                    if (points.Count == 0) { Reply(player, "Aucun point de fabrication."); return; }

                    var text = "Points de fabrication (" + points.Count + ") :";
                    foreach (var point in points)
                    {
                        text += "\n  #" + point.id + " " + point.name
                                + Ui.Dim("  " + EtabliSystem.Format(point.position.ToVector3()));
                    }

                    Reply(player, text);
                    break;
                }
            }
        }

        // ---------------------------------------------------------- /etabli

        private void CmdEtabli(Player player, string[] args)
        {
            if (Etabli == null) { Reply(player, Ui.Bad("Module etabli indisponible.")); return; }

            if (string.Equals(Arg(args, 0), "pose", StringComparison.OrdinalIgnoreCase))
            {
                Etabli.Place(player);
                return;
            }

            var steamId = Utils.SteamId(player);
            var placed = Etabli.PlacedOf(steamId);

            var body = Ui.Dim("L'etabli permet de fabriquer les composants de base.") + "\n\n";
            body += placed != null
                ? "Votre etabli : " + Ui.Ok(EtabliSystem.Format(placed.position.ToVector3()))
                : Etabli.HasPlaced(steamId)
                    ? Ui.Bad("Vous avez deja pose votre etabli.")
                    : Ui.Dim("Vous n'avez pas encore pose d'etabli.");

            var entries = new List<Ui.MenuEntry>();

            if (placed == null && !Etabli.HasPlaced(steamId))
            {
                entries.Add(new Ui.MenuEntry(Ui.Ok("Poser mon etabli ici"), () => Etabli.Place(player)));
            }

            if (placed != null)
            {
                entries.Add(new Ui.MenuEntry(
                    placed.sharedWithDistrict ? "Partage avec mon district : oui" : "Partage avec mon district : non",
                    () =>
                    {
                        placed.sharedWithDistrict = !placed.sharedWithDistrict;
                        Etabli.Save();
                        Checkpoints.RefreshAll();
                        Reply(player, Ui.Ok("✓ Partage " + (placed.sharedWithDistrict ? "active" : "desactive") + "."));
                    }));
            }

            Ui.Menu(player, "Etabli", body, entries, "Fermer", null);
        }

        // ---------------------------------------------------------- /farm_point

        private void CmdFarmPoint(Player player, string[] args)
        {
            if (!RequireStaff(player)) { return; }
            if (Farm == null) { Reply(player, Ui.Bad("Module gisements indisponible.")); return; }

            switch (Arg(args, 0).ToLowerInvariant())
            {
                case "set":
                {
                    var resource = Arg(args, 1);
                    if (string.IsNullOrWhiteSpace(resource))
                    {
                        Reply(player, "Usage : /farm_point set <slug|itemId> [nom]"
                                      + "\nAstuce : /postapo finditem <texte> pour trouver un slug.");
                        return;
                    }

                    var node = Farm.Add(Utils.Position(player), resource, Rest(args, 2));
                    if (!node.Valid)
                    {
                        Reply(player, Ui.Bad("✕ Gisement cree mais desactive : " + node.InvalidReason));
                        return;
                    }

                    Reply(player, Ui.Ok("✓ Gisement #" + node.id + " (« " + node.name + " ») cree — "
                                        + Utils.ItemName(node.ResolvedResourceId) + ".")
                                  + "\n" + Ui.Dim("Ajustez rendement, stock et outil requis dans farm_data.json, "
                                                  + "puis /postapo reload."));

                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "gisement #" + node.id + " cree (" + resource + ")");
                    break;
                }

                case "epave":
                case "cache":
                {
                    int tier;
                    if (!int.TryParse(Arg(args, 1), out tier) || tier < 1 || tier > 5)
                    {
                        Reply(player, "Usage : /farm_point epave <palier 1-5> [nom]"
                                      + "\n" + Ui.Dim("Palier 1-3 = epave, 4-5 = cache (outillage requis)."));
                        return;
                    }

                    var node = Farm.AddScavengePoint(Utils.Position(player), tier, Rest(args, 2));
                    Reply(player, Ui.Ok("✓ " + node.name + " #" + node.id + " cree.")
                                  + "\n" + Ui.Dim("Plan « " + VehicleSystem.TierName(tier) + " » trouvable ici. "
                                                  + "Ajustez le butin dans farm_data.json puis /postapo reload."));

                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "point de fouille #" + node.id + " cree (palier " + tier + ")");
                    break;
                }

                case "remove":
                {
                    int id;
                    if (!int.TryParse(Arg(args, 1), out id))
                    {
                        Reply(player, "Usage : /farm_point remove <id>");
                        return;
                    }

                    Reply(player, Farm.Remove(id)
                        ? Ui.Ok("✓ Point #" + id + " supprime.")
                        : Ui.Bad("✕ Ce point n'existe pas."));
                    break;
                }

                case "tp":
                {
                    int id;
                    var node = int.TryParse(Arg(args, 1), out id) ? Farm.Get(id) : null;
                    if (node == null) { Reply(player, Ui.Bad("✕ Ce gisement n'existe pas.")); return; }

                    Utils.Teleport(player, node.position.ToVector3() + Vector3.up * 0.5f);
                    Reply(player, Ui.Ok("✓ Teleporte au gisement #" + id + "."));
                    break;
                }

                default:
                {
                    var nodes = Farm.Nodes.ToList();
                    if (nodes.Count == 0)
                    {
                        Reply(player, "Aucun gisement. Placez-vous puis tapez /farm_point set <slug>");
                        return;
                    }

                    var text = "Gisements (" + nodes.Count + ") :";
                    foreach (var node in nodes)
                    {
                        text += "\n  #" + node.id + " " + node.name
                                + " — " + (node.Valid ? Utils.ItemName(node.ResolvedResourceId) : Ui.Bad(node.InvalidReason))
                                + Ui.Dim("  stock " + node.charges + "/" + node.maxCharges);
                    }

                    Reply(player, text);
                    break;
                }
            }
        }

        // ---------------------------------------------------------- /atelier

        private void CmdAtelier(Player player, string[] args)
        {
            if (Vehicles == null || Districts == null)
            {
                Reply(player, Ui.Bad("Module vehicules indisponible."));
                return;
            }

            var sub = Arg(args, 0).ToLowerInvariant();

            // Sans argument : le joueur consulte les chantiers de son district.
            if (sub.Length == 0)
            {
                var district = Districts.DistrictOf(player);
                if (district == null)
                {
                    Reply(player, Ui.Bad("✕ Vous n'appartenez a aucun district."));
                    return;
                }

                var projects = Vehicles.ProjectsOf(district.id).ToList();
                var tier = Vehicles.UnlockedTier(district.id);

                var body = district.name + "\n"
                           + Ui.Dim("Palier debloque : ") + Ui.Accent(VehicleSystem.TierName(tier) + " (" + tier + "/5)") + "\n"
                           + Ui.Dim("Vehicules termines : " + Vehicles.CompletedCount(district.id)) + "\n\n";

                if (projects.Count == 0)
                {
                    body += Ui.Dim("Aucun chantier en cours. Trouvez un plan dans une epave, "
                                   + "puis rendez-vous a l'atelier de votre district.");
                    Ui.Info(player, "Chantiers", body);
                    return;
                }

                var entries = projects.Select(p =>
                {
                    var captured = p;
                    return new Ui.MenuEntry("#" + captured.id + " " + captured.modelName
                                            + Ui.Dim("  etape " + (captured.stageIndex + 1)),
                        () => Vehicles.OpenProject(player, captured));
                }).ToList();

                Ui.Menu(player, "Chantiers du district", body, entries, "Fermer", null);
                return;
            }

            if (!RequireStaff(player)) { return; }

            switch (sub)
            {
                case "set":
                {
                    int districtId;
                    if (!int.TryParse(Arg(args, 1), out districtId))
                    {
                        Reply(player, "Usage : /atelier set <districtId> [nom]");
                        return;
                    }

                    var district = Districts.Get(districtId);
                    if (district == null) { Reply(player, Ui.Bad("✕ Ce district n'existe pas.")); return; }

                    var workshop = Vehicles.AddWorkshop(districtId, Utils.Position(player), Rest(args, 2));
                    Checkpoints.RefreshAll();

                    Reply(player, Ui.Ok("✓ Atelier #" + workshop.id + " (« " + workshop.name + " ») cree pour "
                                        + district.name + "."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "atelier vehicule #" + workshop.id + " cree pour le district #" + districtId);
                    break;
                }

                case "remove":
                {
                    int id;
                    if (!int.TryParse(Arg(args, 1), out id))
                    {
                        Reply(player, "Usage : /atelier remove <id>");
                        return;
                    }

                    if (!Vehicles.RemoveWorkshop(id))
                    {
                        Reply(player, Ui.Bad("✕ Cet atelier n'existe pas."));
                        return;
                    }

                    Checkpoints.RefreshAll();
                    Reply(player, Ui.Ok("✓ Atelier #" + id + " supprime."));
                    break;
                }

                case "palier":
                {
                    int districtId, tier;
                    if (!int.TryParse(Arg(args, 1), out districtId) || !int.TryParse(Arg(args, 2), out tier))
                    {
                        Reply(player, "Usage : /atelier palier <districtId> <1-5>");
                        return;
                    }

                    if (Districts.Get(districtId) == null)
                    {
                        Reply(player, Ui.Bad("✕ Ce district n'existe pas."));
                        return;
                    }

                    Vehicles.SetTier(districtId, tier);
                    Reply(player, Ui.Ok("✓ Palier du district #" + districtId + " force a "
                                        + Vehicles.UnlockedTier(districtId) + "."));
                    Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "palier vehicule du district #" + districtId + " force a " + tier);
                    break;
                }

                default:
                {
                    var workshops = Vehicles.Workshops.ToList();
                    if (workshops.Count == 0)
                    {
                        Reply(player, "Aucun atelier. Placez-vous puis tapez /atelier set <districtId>");
                        return;
                    }

                    var text = "Ateliers de reconstruction (" + workshops.Count + ") :";
                    foreach (var workshop in workshops)
                    {
                        var district = Districts.Get(workshop.districtId);
                        text += "\n  #" + workshop.id + " " + workshop.name
                                + " — " + (district != null ? district.name : Ui.Bad("district #" + workshop.districtId + " absent"))
                                + Ui.Dim("  palier " + Vehicles.UnlockedTier(workshop.districtId)
                                         + ", chantiers " + Vehicles.ProjectsOf(workshop.districtId).Count());
                    }

                    Reply(player, text);
                    break;
                }
            }
        }

        // ---------------------------------------------------------- /staffapo

        /// <summary>
        /// Menu staff in-game. Centralise tout ce que le staff doit pouvoir faire
        /// sans taper de commandes texte : voir l'état, gérer les districts, les joueurs,
        /// les établis, forcer des rechargements.
        /// </summary>
        private void CmdStaffApo(Player player, string[] args)
        {
            if (!RequireStaff(player)) { return; }
            OpenStaffMainMenu(player);
        }

        private void OpenStaffMainMenu(Player player)
        {
            var districtCount = Districts != null ? Districts.All.Count() : 0;
            var memberCount = Districts != null ? Districts.All.SelectMany(d => d.members).Count() : 0;
            var etabliCount = Etabli != null ? Etabli.Data.placed.Count : 0;
            var craftOk = Craft != null ? Craft.ValidCount : 0;
            var craftKo = Craft != null ? Craft.InvalidCount : 0;

            var body = Ui.Accent("<b>PostApo v" + Version + " — Panneau Staff</b>") + "\n"
                + Ui.Dim("Districts : " + districtCount + "  •  Membres : " + memberCount
                    + "  •  Établis : " + etabliCount
                    + "  •  Recettes : " + craftOk + (craftKo > 0 ? Ui.Bad(" /" + craftKo + " KO") : ""))
                + "\n" + Ui.Dim("Utilisez /postapo status pour le rapport complet.");

            var entries = new List<Ui.MenuEntry>
            {
                new Ui.MenuEntry(Ui.Accent("🏘 Districts"), 1077, districtCount + " districts",
                    () => OpenStaffDistrictList(player)),
                new Ui.MenuEntry(Ui.Accent("👥 Joueurs en ligne"), 1202, Utils.OnlinePlayers().Count() + " connectés",
                    () => OpenStaffPlayerList(player)),
                new Ui.MenuEntry(Ui.Accent("🔨 Établis posés"), 1755, etabliCount + " posés",
                    () => OpenStaffEtabliList(player)),
                new Ui.MenuEntry(Ui.Accent("🔧 Ateliers craft"), 1213, Districts != null ? Districts.All.Sum(d => d.craftPoints.Count) + " points" : "—",
                    () => OpenStaffCraftPoints(player)),
                new Ui.MenuEntry(Ui.Accent("🚗 Ateliers véhicule"), Mat.BoiteAOutils, Vehicles != null ? Vehicles.Workshops.Count() + " ateliers" : "—",
                    () => OpenStaffWorkshops(player)),
                new Ui.MenuEntry(Ui.Ok("↺ Recharger la config"), 0, "/postapo reload",
                    () =>
                    {
                        ReloadAll();
                        Reply(player, Ui.Ok("✓ Configuration rechargée."));
                        Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player), "reload via /staffapo");
                        OpenStaffMainMenu(player);
                    }),
            };

            Ui.Menu(player, "Staff — PostApo", body, entries, "Fermer", null);
        }

        // ---- Districts ----

        private void OpenStaffDistrictList(Player player)
        {
            if (Districts == null) { Reply(player, Ui.Bad("Module districts indisponible.")); return; }

            var entries = Districts.All.OrderBy(d => d.id).Select(d =>
            {
                var captured = d;
                var tierLabel = Vehicles != null ? " P" + Vehicles.UnlockedTier(captured.id) : "";
                return new Ui.MenuEntry(
                    "#" + captured.id + " " + captured.name,
                    1077,
                    captured.members.Count + " membres" + tierLabel,
                    () => OpenStaffDistrictDetail(player, captured));
            }).ToList();

            Ui.Menu(player, "Staff — Districts", Ui.Dim("Choisissez un district à inspecter ou gérer."),
                entries, "← Retour", () => OpenStaffMainMenu(player));
        }

        private void OpenStaffDistrictDetail(Player player, PostApo.District.District district)
        {
            var owner = district.members.FirstOrDefault(m => district.IsOwner(m.steamId));
            var tier = Vehicles != null ? Vehicles.UnlockedTier(district.id) : 1;
            var completed = Vehicles != null ? Vehicles.CompletedCount(district.id) : 0;

            var body = "<b>" + district.name + "</b>  #" + district.id + "\n"
                + "Propriétaire : " + (owner != null ? Ui.Accent(owner.name) : Ui.Dim("aucun")) + "\n"
                + "Membres : " + district.members.Count + "\n"
                + "Spécialités : " + (district.specialites.Count > 0 ? string.Join(", ", district.specialites.ToArray()) : Ui.Dim("aucune")) + "\n"
                + "Base : " + (district.HasBase ? Ui.Ok("configurée") : Ui.Bad("manquante")) + "\n"
                + "Ateliers craft : " + district.craftPoints.Count + "\n"
                + "Palier véhicule : " + Ui.Accent(tier + "/5") + "  (" + completed + " terminés)\n"
                + "\nGrades :\n";

            foreach (var grade in district.grades.OrderByDescending(g => g.rank))
            {
                var cnt = district.members.Count(m => m.gradeId == grade.id);
                var perms = grade.permissions != null ? grade.permissions.Count : 0;
                body += "  " + Ui.Accent(grade.name) + Ui.Dim(" rang " + grade.rank + " · " + cnt + " membre(s) · " + perms + " droits") + "\n";

                // Affiche les permissions actives pour ce grade
                if (grade.permissions != null && grade.permissions.Count > 0)
                {
                    body += Ui.Dim("    → " + string.Join(", ", grade.permissions.Take(5).ToArray())
                        + (grade.permissions.Count > 5 ? "..." : "")) + "\n";
                }
            }

            var entries = new List<Ui.MenuEntry>
            {
                new Ui.MenuEntry("👥 Voir les membres", 1202, district.members.Count + " membres",
                    () => OpenStaffMemberList(player, district)),
                new Ui.MenuEntry("🔐 Vérifier les accès (grades)", 1213, district.grades.Count + " grades",
                    () => OpenStaffGradeAudit(player, district)),
            };

            if (Vehicles != null && tier < 5)
            {
                entries.Add(new Ui.MenuEntry("▲ Forcer palier " + (tier + 1), Mat.PlanT1, "",
                    () =>
                    {
                        Vehicles.SetTier(district.id, tier + 1);
                        Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                            "palier district #" + district.id + " forcé à " + (tier + 1));
                        Reply(player, Ui.Ok("✓ Palier du " + district.name + " forcé à " + Vehicles.UnlockedTier(district.id) + "."));
                        OpenStaffDistrictDetail(player, district);
                    }));
            }

            if (district.HasBase)
            {
                entries.Add(new Ui.MenuEntry(Ui.Accent("⊙ Téléporter à la base"), 1077, "",
                    () => Districts.TeleportToBase(player, district, false)));
            }

            Ui.Menu(player, district.name, body, entries, "← Districts", () => OpenStaffDistrictList(player));
        }

        private void OpenStaffMemberList(Player player, PostApo.District.District district)
        {
            var body = Ui.Accent(district.name) + " — " + district.members.Count + " membres\n"
                + Ui.Dim("● = connecté  ○ = hors ligne");

            var entries = district.members
                .OrderByDescending(m => { var g = district.FindGrade(m.gradeId); return g != null ? g.rank : 0; })
                .Select(m =>
                {
                    var captured = m;
                    var grade = district.FindGrade(captured.gradeId);
                    var online = Utils.FindOnlineBySteamId(captured.steamId) != null;
                    return new Ui.MenuEntry(
                        (online ? Ui.Ok("● ") : Ui.Dim("○ ")) + captured.name
                        + (district.IsOwner(captured.steamId) ? Ui.Accent(" ★") : ""),
                        1202,
                        grade != null ? grade.name : "?",
                        () => OpenStaffMemberDetail(player, district, captured));
                }).ToList();

            Ui.Menu(player, "Membres — " + district.name, body, entries,
                "← District", () => OpenStaffDistrictDetail(player, district));
        }

        private void OpenStaffMemberDetail(Player player, PostApo.District.District district, PostApo.District.Member member)
        {
            var grade = district.FindGrade(member.gradeId);
            var online = Utils.FindOnlineBySteamId(member.steamId) != null;

            var body = (online ? Ui.Ok("● Connecté") : Ui.Dim("○ Hors ligne")) + "\n"
                + "Nom : " + Ui.Accent(member.name) + "\n"
                + "SteamID : " + Ui.Dim(member.steamId) + "\n"
                + "Grade : " + (grade != null ? Ui.Accent(grade.name) + Ui.Dim(" (rang " + grade.rank + ")") : Ui.Bad("inconnu")) + "\n"
                + "Propriétaire : " + (district.IsOwner(member.steamId) ? Ui.Ok("oui") : "non") + "\n\n";

            if (grade != null && grade.permissions != null && grade.permissions.Count > 0)
            {
                body += "Droits de ce grade :\n";
                foreach (var perm in grade.permissions)
                    body += Ui.Ok("  ✓ ") + PostApo.District.GradeMenu.LabelPublic(perm) + "\n";
            }
            else
            {
                body += Ui.Dim("Aucun droit actif pour ce grade.");
            }

            var entries = new List<Ui.MenuEntry>();

            if (!district.IsOwner(member.steamId))
            {
                entries.Add(new Ui.MenuEntry(Ui.Bad("✕ Expulser " + member.name), 1580, "",
                    () =>
                    {
                        Ui.Confirm(player, "Expulser " + member.name,
                            Ui.Bad("Expulser définitivement " + member.name + " du " + district.name + " ?"),
                            Ui.Bad("Oui, expulser"), "Annuler",
                            () =>
                            {
                                var target = Utils.FindOnlineBySteamId(member.steamId);
                                if (target != null)
                                {
                                    Districts.Leave(target, district, false);
                                    Utils.Send(target, Prefix + Ui.Bad("Vous avez été expulsé du " + district.name + " par le staff."));
                                }
                                else
                                {
                                    district.members.Remove(member);
                                    Districts.Save();
                                }
                                Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                                    "expulsion de " + member.name + " du " + district.name);
                                Reply(player, Ui.Ok("✓ " + member.name + " expulsé."));
                                OpenStaffMemberList(player, district);
                            },
                            () => OpenStaffMemberDetail(player, district, member));
                    }));
            }

            Ui.Menu(player, member.name, body, entries, "← Membres", () => OpenStaffMemberList(player, district));
        }

        /// <summary>Audit des grades : affiche tous les grades et leurs permissions pour vérification staff.</summary>
        private void OpenStaffGradeAudit(Player player, PostApo.District.District district)
        {
            var body = Ui.Accent("<b>Audit des grades — " + district.name + "</b>") + "\n"
                + Ui.Dim("Vérifiez que chaque grade a les bons droits. "
                    + "Modifiez via /district → Gérer les grades.") + "\n";

            foreach (var grade in district.grades.OrderByDescending(g => g.rank))
            {
                body += "\n" + Ui.Accent(grade.name) + Ui.Dim("  rang " + grade.rank
                    + " · " + district.members.Count(m => m.gradeId == grade.id) + " membre(s)") + "\n";

                var allPerms = PostApo.District.Perm.DistrictPermissions
                    .Concat(PostApo.District.Perm.TerrainPermissions)
                    .Concat(PostApo.District.Perm.VehiclePermissions);

                foreach (var perm in allPerms)
                {
                    var has = grade.Has(perm);
                    body += (has ? Ui.Ok("  ✓ ") : Ui.Dim("  ✕ "))
                        + PostApo.District.GradeMenu.LabelPublic(perm) + "\n";
                }
            }

            Ui.LongText(player, "Audit grades — " + district.name, body, "← District",
                () => OpenStaffDistrictDetail(player, district));
        }

        // ---- Joueurs en ligne ----

        private void OpenStaffPlayerList(Player player)
        {
            var online = Utils.OnlinePlayers().ToList();
            if (online.Count == 0)
            {
                Ui.Info(player, "Joueurs en ligne", Ui.Dim("Aucun joueur connecté."));
                return;
            }

            var entries = online.Select(p =>
            {
                var captured = p;
                var district = Districts != null ? Districts.DistrictOf(captured) : null;
                return new Ui.MenuEntry(
                    Ui.Ok("● ") + Utils.Name(captured),
                    1202,
                    district != null ? district.name : Ui.Dim("sans district"),
                    () => OpenStaffPlayerDetail(player, captured));
            }).ToList();

            Ui.Menu(player, "Joueurs en ligne — " + online.Count, Ui.Dim("Cliquez un joueur pour le gérer."),
                entries, "← Staff", () => OpenStaffMainMenu(player));
        }

        private void OpenStaffPlayerDetail(Player player, Player target)
        {
            var steamId = Utils.SteamId(target);
            var district = Districts != null ? Districts.DistrictOf(target) : null;
            var grade = district != null ? district.GradeOf(steamId) : null;
            var etabli = Etabli != null ? Etabli.PlacedOf(steamId) : null;
            var isBusy = Craft != null && Craft.IsBusy(target);

            var body = Ui.Ok("● " + Utils.Name(target)) + "\n"
                + "SteamID : " + Ui.Dim(steamId) + "\n"
                + "District : " + (district != null ? Ui.Accent(district.name) : Ui.Dim("aucun")) + "\n"
                + "Grade : " + (grade != null ? grade.name : Ui.Dim("—")) + "\n"
                + "Établi posé : " + (etabli != null ? Ui.Ok("oui") : Ui.Dim("non")) + "\n"
                + "Craft en cours : " + (isBusy ? Ui.Accent("oui") : Ui.Dim("non")) + "\n";

            var entries = new List<Ui.MenuEntry>();

            if (etabli != null)
            {
                entries.Add(new Ui.MenuEntry(Ui.Bad("Supprimer l'établi (rend la pose)"), 1755, "",
                    () =>
                    {
                        Etabli.RemovePlaced(steamId, true);
                        Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                            "établi de " + Utils.Name(target) + " supprimé via /staffapo");
                        Reply(player, Ui.Ok("✓ Établi supprimé, la pose est rendue."));
                        OpenStaffPlayerDetail(player, target);
                    }));
            }

            if (Arrival != null)
            {
                entries.Add(new Ui.MenuEntry("Réinitialiser le parcours d'arrivée", 0, "",
                    () =>
                    {
                        Arrival.ResetPlayer(steamId, true);
                        Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                            "arrivée de " + Utils.Name(target) + " réinitialisée via /staffapo");
                        Reply(player, Ui.Ok("✓ Parcours d'arrivée réinitialisé."));
                        OpenStaffPlayerDetail(player, target);
                    }));
            }

            Ui.Menu(player, Utils.Name(target), body, entries, "← Joueurs", () => OpenStaffPlayerList(player));
        }

        // ---- Établis ----

        private void OpenStaffEtabliList(Player player)
        {
            if (Etabli == null) { Reply(player, Ui.Bad("Module établi indisponible.")); return; }

            var placed = Etabli.Data.placed.ToList();
            if (placed.Count == 0)
            {
                Ui.Info(player, "Établis posés", Ui.Dim("Aucun établi posé pour l'instant."));
                return;
            }

            var entries = placed.Select(e =>
            {
                var captured = e;
                var online = Utils.FindOnlineBySteamId(captured.ownerSteamId) != null;
                return new Ui.MenuEntry(
                    (online ? Ui.Ok("● ") : Ui.Dim("○ ")) + captured.ownerName,
                    1755,
                    captured.sharedWithDistrict ? "partagé" : "privé",
                    () =>
                    {
                        Ui.Confirm(player, "Établi de " + captured.ownerName,
                            "SteamID : " + Ui.Dim(captured.ownerSteamId)
                            + "\nPartagé avec district : " + (captured.sharedWithDistrict ? Ui.Ok("oui") : "non")
                            + "\n\n" + Ui.Bad("Supprimer cet établi ? La pose sera rendue au joueur."),
                            Ui.Bad("Supprimer"), "Annuler",
                            () =>
                            {
                                Etabli.RemovePlaced(captured.ownerSteamId, true);
                                Checkpoints.RefreshAll();
                                Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                                    "établi de " + captured.ownerName + " supprimé via /staffapo");
                                Reply(player, Ui.Ok("✓ Établi supprimé."));
                                OpenStaffEtabliList(player);
                            },
                            () => OpenStaffEtabliList(player));
                    });
            }).ToList();

            Ui.Menu(player, "Établis posés — " + placed.Count,
                Ui.Dim("Cliquez un établi pour l'inspecter ou le supprimer."),
                entries, "← Staff", () => OpenStaffMainMenu(player));
        }

        // ---- Points de craft de district ----

        private void OpenStaffCraftPoints(Player player)
        {
            if (Districts == null) { Reply(player, Ui.Bad("Module districts indisponible.")); return; }

            var lines = new List<string>();
            var total = 0;

            foreach (var district in Districts.All.OrderBy(d => d.id))
            {
                foreach (var point in district.craftPoints)
                {
                    total++;
                    var recipeCount = Craft != null ? Craft.RecipesForSpecialite(point.specialite).Count() : 0;
                    lines.Add(Ui.Accent(district.name) + " — " + point.specialite
                        + Ui.Dim("  #" + district.id + "/" + point.id
                            + "  " + recipeCount + " recette(s)"));
                }
            }

            var body = total + " atelier(s) de craft configuré(s).\n\n"
                + (total > 0 ? string.Join("\n", lines.ToArray()) : Ui.Dim("Aucun point."))
                + "\n\n" + Ui.Dim("Pour en ajouter : /district_craft set <districtId> <specialite>")
                + "\n" + Ui.Dim("Pour supprimer : /district_craft remove <districtId> <pointId>");

            Ui.Info(player, "Ateliers craft", body);
        }

        // ---- Ateliers véhicule ----

        private void OpenStaffWorkshops(Player player)
        {
            if (Vehicles == null || Districts == null) { Reply(player, Ui.Bad("Module véhicules indisponible.")); return; }

            var workshops = Vehicles.Workshops.ToList();
            if (workshops.Count == 0)
            {
                Ui.Info(player, "Ateliers véhicule",
                    Ui.Dim("Aucun atelier configuré.") + "\n\n"
                    + Ui.Dim("Placez-vous dans une base de district puis : /atelier set <districtId>"));
                return;
            }

            var entries = workshops.Select(w =>
            {
                var captured = w;
                var district = Districts.Get(captured.districtId);
                var tier = Vehicles.UnlockedTier(captured.districtId);
                var projects = Vehicles.ProjectsOf(captured.districtId).Count();

                return new Ui.MenuEntry(
                    captured.name + (district != null ? Ui.Dim(" (" + district.name + ")") : ""),
                    Mat.BoiteAOutils,
                    "P" + tier + "/5 · " + projects + " chantier(s)",
                    () =>
                    {
                        var body = captured.name + "\n"
                            + "District : " + (district != null ? district.name : Ui.Bad("inconnu")) + "\n"
                            + "Palier débloqué : " + Ui.Accent(tier + "/5 — " + VehicleSystem.TierName(tier)) + "\n"
                            + "Chantiers en cours : " + projects + "\n"
                            + "Véhicules terminés : " + Vehicles.CompletedCount(captured.districtId) + "\n\n"
                            + Ui.Dim("Pour forcer le palier : /atelier palier " + captured.districtId + " <1-5>");

                        Ui.Info(player, captured.name, body);
                    });
            }).ToList();

            Ui.Menu(player, "Ateliers véhicule", Ui.Dim("Vue d'ensemble des ateliers de reconstruction."),
                entries, "← Staff", () => OpenStaffMainMenu(player));
        }

        // ------------------------------------------------------------------ chemins

        /// <summary>
        /// Dossier de donnees. On privilegie <c>thisPath</c> fourni par le jeu, avec repli sur
        /// l'emplacement de la DLL — le plugin fonctionne dans les deux cas.
        /// </summary>
        private static string ResolveRoot()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                var directory = string.IsNullOrWhiteSpace(location)
                    ? Directory.GetCurrentDirectory()
                    : (Path.GetDirectoryName(location) ?? Directory.GetCurrentDirectory());

                var name = new DirectoryInfo(directory).Name;
                return name.Equals("PostApo", StringComparison.OrdinalIgnoreCase)
                    ? directory
                    : Path.Combine(directory, "PostApo");
            }
            catch
            {
                return Path.Combine(Directory.GetCurrentDirectory(), "PostApo");
            }
        }
    }
}
