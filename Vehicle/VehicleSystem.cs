using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.DB;
using Life.Network;
using Life.PermissionSystem;
using Newtonsoft.Json;
using PostApo.Core;
using PostApo.District;
using PostApo.Etabli;
using UnityEngine;

namespace PostApo.Vehicle
{
    /// <summary>
    /// Filiere vehicule : ateliers de reconstruction, chantiers multi-etapes, deblocage progressif
    /// des paliers, puis creation d'un vrai vehicule persistant.
    ///
    /// La creation finale s'appuie sur l'API native :
    /// <c>LifeDB.CreateVehicle</c> (insertion en base) puis <c>VehiclesManager.LoadNewVehicle</c>
    /// et <c>SpawnVehicle</c>. Le vehicule obtenu est un vehicule de jeu normal, immatricule,
    /// possede et sauvegarde — pas un item symbolique.
    /// </summary>
    public sealed class VehicleSystem
    {
        private readonly PostApoPlugin _plugin;
        private readonly JsonStore<VehicleData> _dataStore;
        private readonly JsonStore<VehicleProjectData> _projectStore;

        private VehicleData _data;
        private VehicleProjectData _projects;

        public VehicleSystem(PostApoPlugin plugin, string root)
        {
            _plugin = plugin;
            _dataStore = new JsonStore<VehicleData>(root, "vehicles.json");
            _projectStore = new JsonStore<VehicleProjectData>(root, "vehicle_projects.json");
            Reload();
        }

        public IEnumerable<VehicleBlueprint> Blueprints
        {
            get { return _data.blueprints ?? new List<VehicleBlueprint>(); }
        }

        public IEnumerable<VehicleWorkshop> Workshops
        {
            get { return _data.workshops ?? new List<VehicleWorkshop>(); }
        }

        public int ValidBlueprints { get { return Blueprints.Count(b => b.Valid); } }
        public int ProjectCount { get { return _projects.projects.Count; } }

        public void Reload()
        {
            _data = _dataStore.Load();
            if (_data.blueprints == null) { _data.blueprints = new List<VehicleBlueprint>(); }
            if (_data.workshops == null) { _data.workshops = new List<VehicleWorkshop>(); }

            _projects = _projectStore.Load();
            if (_projects.projects == null) { _projects.projects = new List<VehicleProject>(); }
            if (_projects.unlockedTier == null) { _projects.unlockedTier = new Dictionary<string, int>(); }
            if (_projects.completed == null) { _projects.completed = new Dictionary<string, int>(); }

            foreach (var project in _projects.projects) { if (project != null) { project.Normalize(); } }

            Validate();
            Save();
        }

        private void Validate()
        {
            var ready = Utils.ItemsReady();

            foreach (var blueprint in _data.blueprints)
            {
                if (blueprint == null) { continue; }

                blueprint.Valid = true;
                blueprint.InvalidReason = string.Empty;

                if (blueprint.stages == null || blueprint.stages.Count == 0)
                {
                    blueprint.Valid = false;
                    blueprint.InvalidReason = "aucune etape definie";
                    continue;
                }

                foreach (var stage in blueprint.stages)
                {
                    if (stage.inputs == null) { stage.inputs = new List<RecipeItem>(); }
                    if (stage.workSeconds < 1f) { stage.workSeconds = 1f; }

                    foreach (var input in stage.inputs)
                    {
                        input.ResolvedId = Utils.ResolveItemId(input.slug, input.itemId);
                        if (input.qty <= 0) { input.qty = 1; }

                        if (ready && (input.ResolvedId <= 0 || !Utils.ItemExists(input.ResolvedId)))
                        {
                            blueprint.Valid = false;
                            blueprint.InvalidReason = "materiau introuvable dans l'etape « " + stage.name + " »";
                        }
                    }
                }

                if (ready && blueprint.planItemId > 0 && !Utils.ItemExists(blueprint.planItemId))
                {
                    blueprint.Valid = false;
                    blueprint.InvalidReason = "item de plan introuvable (" + blueprint.planItemId + ")";
                }
            }

            if (!ready) { return; }

            var invalid = _data.blueprints.Where(b => b != null && !b.Valid).ToArray();
            if (invalid.Length > 0)
            {
                Utils.Warn(invalid.Length + " plan(s) de vehicule desactive(s) :");
                foreach (var blueprint in invalid.Take(10))
                {
                    Utils.Warn("  - " + blueprint.name + " : " + blueprint.InvalidReason);
                }
            }
        }

        public bool Save()
        {
            var a = _dataStore.Save(_data);
            var b = _projectStore.Save(_projects);
            return a && b;
        }

        public VehicleBlueprint GetBlueprint(int modelId)
        {
            return _data.blueprints.FirstOrDefault(b => b != null && b.modelId == modelId);
        }

        /// <summary>
        /// Decrit ce qu'ouvre un item s'il s'agit d'un plan, sinon null.
        /// Sert a expliquer une trouvaille au moment ou elle tombe.
        /// </summary>
        public string PlanDescription(int itemId)
        {
            if (itemId <= 0) { return null; }

            var models = Blueprints.Where(b => b.Valid && b.planItemId == itemId).ToList();
            if (models.Count == 0) { return null; }

            var tier = models[0].tier;
            var names = string.Join(", ", models.Take(3).Select(b => b.name).ToArray());
            if (models.Count > 3) { names += "..."; }

            return models[0].planLabel + " — palier " + tier + " (" + TierName(tier) + ") : "
                   + names;
        }

        /// <summary>
        /// Panneau « mes plans » : ce que le joueur possede, ce que chaque plan ouvre, et le
        /// rappel que le plan lui appartient mais sert au district.
        /// </summary>
        public void OpenPlans(Player player)
        {
            if (player == null) { return; }

            var district = _plugin.Districts.DistrictOf(player);
            var unlocked = district != null ? UnlockedTier(district.id) : 0;

            var body = "<b>Les plans</b>\n"
                       + Ui.Dim("Un plan est un objet de VOTRE inventaire, trouve en fouillant les "
                                + "epaves et les caches. Personne ne peut vous le prendre.")
                       + "\n\n"
                       + Ui.Dim("Il se consomme au moment ou vous ouvrez un chantier a l'atelier de "
                                + "votre district. Le vehicule termine est immatricule a VOTRE nom, "
                                + "meme si vos coequipiers ont livre des materiaux.")
                       + "\n";

            if (district == null)
            {
                body += "\n" + Ui.Bad("Vous n'appartenez a aucun district : vous ne pouvez pas ouvrir de chantier.");
            }
            else
            {
                body += "\nVotre district : " + district.name
                        + "\n" + Ui.Dim("Palier debloque : " + unlocked + "/5 — un plan de palier "
                                        + "superieur reste inutilisable tant que le district n'a pas progresse.");
            }

            var entries = new List<Ui.MenuEntry>();
            var held = 0;

            foreach (var group in Blueprints.Where(b => b.Valid)
                         .GroupBy(b => b.planItemId).OrderBy(g => g.First().tier))
            {
                var planId = group.Key;
                var count = Utils.CountItem(player, planId);
                var tier = group.First().tier;
                var usable = district != null && tier <= unlocked;

                if (count > 0) { held += count; }

                entries.Add(new Ui.MenuEntry(
                    (count > 0 ? Ui.Ok("● ") : Ui.Dim("○ ")) + group.First().planLabel
                    + Ui.Dim("  palier " + tier)
                    + (count > 0 && !usable ? Ui.Bad("  (palier verrouille)") : ""),
                    planId,
                    count > 0 ? "×" + count : "—",
                    () => Ui.Info(player, group.First().planLabel,
                        "Ce plan ouvre les chantiers suivants :\n\n"
                        + string.Join("\n", group.Select(b => "  • " + b.name).ToArray())
                        + "\n\n" + Ui.Dim("Trouvable dans les epaves et caches de palier " + tier + ".")
                        + "\n" + Ui.Dim("Utilisez /epaves pour localiser les plus proches."))));
            }

            if (held == 0)
            {
                body += "\n\n" + Ui.Bad("Vous n'avez aucun plan pour l'instant.")
                        + "\n" + Ui.Dim("Tapez /epaves pour trouver ou fouiller.");
            }

            Ui.Menu(player, "Mes plans", body, entries, "Fermer", null);
        }

        public int UnlockedTier(int districtId) { return _projects.TierOf(districtId); }
        public int CompletedCount(int districtId) { return _projects.CompletedOf(districtId); }

        // ------------------------------------------------------------------ ateliers

        public VehicleWorkshop AddWorkshop(int districtId, Vector3 position, string name)
        {
            var workshop = new VehicleWorkshop
            {
                id = _data.workshops.Count == 0 ? 1 : _data.workshops.Max(w => w.id) + 1,
                districtId = districtId,
                name = string.IsNullOrWhiteSpace(name) ? "Atelier de reconstruction" : Utils.Sanitize(name, 32),
                position = new Position(position),
            };

            _data.workshops.Add(workshop);
            Save();
            return workshop;
        }

        public bool RemoveWorkshop(int id)
        {
            var workshop = _data.workshops.FirstOrDefault(w => w != null && w.id == id);
            if (workshop == null) { return false; }

            _data.workshops.Remove(workshop);
            Save();
            return true;
        }

        public IEnumerable<InteractionPoint> Points()
        {
            foreach (var workshop in _data.workshops.ToArray())
            {
                if (workshop == null || workshop.position == null) { continue; }
                var captured = workshop;

                yield return new InteractionPoint
                {
                    Key = "atelier-vehicule-" + captured.id,
                    Position = captured.position.ToVector3(),
                    VisibleTo = p => IsMember(p, captured.districtId)
                                     || Utils.IsStaff(p, _plugin.Config.staffLevelMin),
                    OnEnter = p => OpenWorkshop(p, captured),
                };
            }
        }

        private bool IsMember(Player player, int districtId)
        {
            var district = _plugin.Districts.DistrictOf(player);
            return district != null && district.id == districtId;
        }

        // ------------------------------------------------------------------ menu atelier

        private void OpenWorkshop(Player player, VehicleWorkshop workshop)
        {
            if (player == null || workshop == null) { return; }

            var district = _plugin.Districts.Get(workshop.districtId);
            if (district == null)
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Cet atelier n'appartient a aucun district valide."));
                return;
            }

            var isStaff = Utils.IsStaff(player, _plugin.Config.staffLevelMin);

            if (!IsMember(player, workshop.districtId) && !isStaff)
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Cet atelier appartient au " + district.name + "."));
                return;
            }

            if (!isStaff && !_plugin.Districts.HasPermission(player, Perm.CraftDistrict))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                return;
            }

            var tier = UnlockedTier(district.id);
            var done = CompletedCount(district.id);
            var active = _projects.projects.Where(p => p.districtId == district.id).ToList();

            var body = district.name + "\n"
                       + Ui.Dim("Palier debloque : ") + Ui.Accent(TierName(tier) + " (" + tier + "/5)") + "\n"
                       + Ui.Dim("Vehicules termines : " + done) + "\n"
                       + Ui.Dim("Chantiers en cours : " + active.Count);

            if (tier < 5)
            {
                body += "\n\n" + Ui.Dim("Terminez un vehicule de palier " + tier
                                        + " pour debloquer le palier " + (tier + 1) + ".");
            }

            var entries = new List<Ui.MenuEntry>
            {
                new Ui.MenuEntry(Ui.Ok("▶ Demarrer un chantier"), Mat.PlanT1, "plan requis",
                    () => OpenStartMenu(player, workshop, district)),
                new Ui.MenuEntry("Chantiers en cours", Mat.BoiteAOutils, active.Count.ToString(),
                    () => OpenProjectList(player, workshop, district)),
                new Ui.MenuEntry("Catalogue des vehicules", Mat.Calculateur, "20 modeles",
                    () => OpenCatalog(player, district, 0)),
                new Ui.MenuEntry("Comment ca marche ?", Mat.PlanT3, "aide",
                    () => OpenHelp(player)),
            };

            Ui.Menu(player, workshop.name, body, entries, "Fermer", null);
        }

        /// <summary>Explique la boucle complete : sans ca, un joueur ne devine pas par ou commencer.</summary>
        private void OpenHelp(Player player)
        {
            var body =
                "<b>Construire un vehicule, etape par etape</b>\n\n"
                + Ui.Accent("1. Trouver un plan") + "\n"
                + "Fouillez les epaves et les caches disseminees sur la carte. "
                + "Un plan tombe rarement : 18 % sur une epave de palier 1, 3 % sur une cache de palier 5.\n\n"
                + Ui.Accent("2. Recolter les matieres premieres") + "\n"
                + "Magnetite, cuivre, buches, sable, caoutchouc. Les gisements sont marques au sol.\n\n"
                + Ui.Accent("3. Fabriquer les composants") + "\n"
                + "Sur votre etabli pour les pieces de base (lingots, plaques, planches), "
                + "puis a l'atelier specialise de votre district pour les pieces avancees.\n\n"
                + Ui.Accent("4. Ouvrir un chantier ici") + "\n"
                + "Le plan est consomme. Le chantier reste ouvert autant de temps qu'il faut.\n\n"
                + Ui.Accent("5. Livrer et travailler") + "\n"
                + "Chaque etape demande des composants precis. Vous livrez ce que vous portez, "
                + "vos coequipiers completent, puis on lance les travaux. "
                + "Une etape ratee coute les materiaux de l'etape, jamais le chantier.\n\n"
                + Ui.Accent("6. Le vehicule sort de l'atelier") + "\n"
                + "Il est immatricule au nom de celui qui a ouvert le chantier, "
                + "et votre district debloque le palier suivant.";

            Ui.Info(player, "Guide de reconstruction", body);
        }

        public static string TierName(int tier)
        {
            switch (tier)
            {
                case 1: return "Epave roulante";
                case 2: return "Utilitaire";
                case 3: return "Routiere";
                case 4: return "Sportive";
                default: return "Legende";
            }
        }

        /// <summary>
        /// Catalogue navigable : un palier par page, chaque véhicule cliquable pour voir le détail
        /// complet de ses ressources. C'est le point d'entrée pour « quoi construire et avec quoi ».
        /// </summary>
        private void OpenCatalog(Player player, PostApo.District.District district, int tierPage)
        {
            var unlocked = UnlockedTier(district.id);
            var tiers = Blueprints.Where(b => b.Valid).Select(b => b.tier).Distinct().OrderBy(t => t).ToList();
            if (tiers.Count == 0)
            {
                Ui.Info(player, "Catalogue", Ui.Bad("Aucun plan disponible sur ce serveur."));
                return;
            }

            tierPage = Mathf.Clamp(tierPage, 0, tiers.Count - 1);
            var tier = tiers[tierPage];
            var locked = tier > unlocked;

            var models = Blueprints.Where(b => b.Valid && b.tier == tier).OrderBy(b => b.name).ToList();
            var planId = models.Count > 0 ? models[0].planItemId : 0;
            var planLabel = models.Count > 0 ? models[0].planLabel : "?";

            var body = "<b>Palier " + tier + " — " + TierName(tier) + "</b>\n"
                       + (locked
                           ? Ui.Bad("🔒 Verrouillé. Terminez un véhicule de palier " + unlocked + " pour débloquer.")
                           : Ui.Ok("✓ Débloqué pour votre district."))
                       + "\nPlan requis : " + Ui.Accent(planLabel)
                       + " — " + Ui.Dim("trouvable en fouillant les épaves palier " + tier)
                       + "\n" + Ui.Dim("Cliquez un modèle pour voir toutes ses ressources étape par étape.");

            var entries = new List<Ui.MenuEntry>();

            foreach (var blueprint in models)
            {
                var captured = blueprint;
                var held = Utils.CountItem(player, captured.planItemId);

                // Calcul rapide du total de ressources pour l'afficher en résumé
                var totalItems = 0;
                foreach (var stage in captured.stages)
                    foreach (var inp in stage.inputs)
                        totalItems += inp.qty;

                entries.Add(new Ui.MenuEntry(
                    (locked ? Ui.Dim(captured.name) : captured.name)
                    + (held > 0 ? Ui.Ok("  ★ plan en main") : ""),
                    captured.planItemId,
                    captured.stages.Count + " étapes · " + totalItems + " pièces",
                    () => OpenBlueprintDetail(player, district, captured, tierPage)));
            }

            if (tierPage > 0)
            {
                entries.Add(new Ui.MenuEntry("◀ Palier " + tiers[tierPage - 1] + " — " + TierName(tiers[tierPage - 1]), 0, "",
                    () => OpenCatalog(player, district, tierPage - 1)));
            }

            if (tierPage < tiers.Count - 1)
            {
                entries.Add(new Ui.MenuEntry("▶ Palier " + tiers[tierPage + 1] + " — " + TierName(tiers[tierPage + 1]), 0, "",
                    () => OpenCatalog(player, district, tierPage + 1)));
            }

            Ui.Menu(player, "Catalogue", body, entries, "Fermer", null);
        }

        /// <summary>
        /// Fiche complète d'un véhicule : toutes les étapes, tous les composants avec ce que le
        /// joueur possède déjà, et le total agrégé. Répond à « on ne sait pas les ressources qu'il faut ».
        /// </summary>
        private void OpenBlueprintDetail(Player player, PostApo.District.District district,
                                         VehicleBlueprint blueprint, int tierPage)
        {
            var hasPlan = Utils.CountItem(player, blueprint.planItemId) > 0;

            var body = "<b>" + blueprint.name + "</b>  "
                       + Ui.Dim("Palier " + blueprint.tier + " — " + TierName(blueprint.tier)) + "\n"
                       + "Plan : " + Ui.Accent(blueprint.planLabel)
                       + "  " + (hasPlan ? Ui.Ok("✓ vous l'avez") : Ui.Bad("✕ à trouver en épave"))
                       + "\n";

            // Total agrégé par item : la vraie question est « combien au total ».
            var totals = new Dictionary<int, int>();

            for (var i = 0; i < blueprint.stages.Count; i++)
            {
                var stage = blueprint.stages[i];
                body += "\n" + Ui.Accent("Étape " + (i + 1) + " : " + stage.name)
                        + Ui.Dim("  (" + CraftEngine.FormatDuration(EffectiveWork(stage)) + ")");

                if (!string.IsNullOrWhiteSpace(stage.description))
                    body += "\n" + Ui.Dim("   " + stage.description);

                body += "\n";

                foreach (var input in stage.inputs)
                {
                    var have = Utils.CountItem(player, input.ResolvedId);
                    var ok = have >= input.qty;
                    body += (ok ? Ui.Ok("   ✓ ") : "   ")
                            + input.qty + " × " + Utils.ItemName(input.ResolvedId)
                            + Ui.Dim("  (vous : " + have + ")") + "\n";

                    int current;
                    totals[input.ResolvedId] = (totals.TryGetValue(input.ResolvedId, out current) ? current : 0) + input.qty;
                }

                var failPct = stage.failureChance >= 0f ? stage.failureChance : _plugin.Config.difficulty.craftFailureChance;
                var failInt = Mathf.RoundToInt(Mathf.Clamp01(failPct) * 100f);
                if (failInt > 0)
                    body += Ui.Bad("   ⚠ Risque d'échec : " + failInt + "% (perd les matériaux de l'étape)") + "\n";
            }

            body += "\n<b>── TOTAL À RÉUNIR ──</b>\n";
            foreach (var pair in totals.OrderByDescending(p => p.Value))
            {
                var have = Utils.CountItem(player, pair.Key);
                var ok = have >= pair.Value;
                body += (ok ? Ui.Ok("  ✓ ") : "  ")
                        + pair.Value + " × " + Utils.ItemName(pair.Key)
                        + Ui.Dim("  (sur vous : " + have + "/" + pair.Value + ")") + "\n";
            }

            Ui.LongText(player, blueprint.name, body, "← Retour au catalogue",
                () => OpenCatalog(player, district, tierPage));
        }

        private void OpenStartMenu(Player player, VehicleWorkshop workshop, PostApo.District.District district)
        {
            var tier = UnlockedTier(district.id);

            var available = Blueprints
                .Where(b => b.Valid && b.tier <= tier && Utils.CountItem(player, b.planItemId) > 0)
                .OrderBy(b => b.tier).ThenBy(b => b.name)
                .ToList();

            if (available.Count == 0)
            {
                Ui.Info(player, "Demarrer un chantier",
                    Ui.Bad("Vous n'avez aucun plan exploitable ici.") + "\n\n"
                    + Ui.Dim("Un chantier demande un plan, trouve en fouillant les epaves et les caches. "
                             + "Votre district a debloque le palier " + tier + " (" + TierName(tier) + ")."));
                return;
            }

            var entries = available.Select(b =>
            {
                var captured = b;
                return new Ui.MenuEntry(
                    captured.name,
                    captured.planItemId,
                    "P" + captured.tier + " · " + captured.stages.Count + " et.",
                    () => ConfirmStart(player, workshop, district, captured));
            }).ToList();

            Ui.Menu(player, "Demarrer un chantier",
                Ui.Dim("Le plan sera consomme. Les materiaux se livrent etape par etape."),
                entries, "Retour", () => OpenWorkshop(player, workshop));
        }

        private void ConfirmStart(Player player, VehicleWorkshop workshop,
                                  PostApo.District.District district, VehicleBlueprint blueprint)
        {
            var body = "<b>" + blueprint.name + "</b>\n"
                       + Ui.Dim("Palier " + blueprint.tier + " — " + TierName(blueprint.tier)) + "\n\n"
                       + "Etapes :\n";

            for (var i = 0; i < blueprint.stages.Count; i++)
            {
                body += Ui.Dim("  " + blueprint.stages[i].name) + "\n";
            }

            body += "\nPlan consomme : " + Ui.Accent(blueprint.planLabel);

            Ui.Confirm(player, "Demarrer : " + blueprint.name, body,
                Ui.Ok("Demarrer le chantier"), "Annuler",
                () => StartProject(player, workshop, district, blueprint),
                () => OpenStartMenu(player, workshop, district));
        }

        private void StartProject(Player player, VehicleWorkshop workshop,
                                  PostApo.District.District district, VehicleBlueprint blueprint)
        {
            var prefix = _plugin.Prefix;
            var steamId = Utils.SteamId(player);

            if (blueprint.tier > UnlockedTier(district.id))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Votre district n'a pas encore debloque ce palier."));
                return;
            }

            if (Utils.CountItem(player, blueprint.planItemId) <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous n'avez pas le plan requis."));
                return;
            }

            if (!Utils.TakeItem(player, blueprint.planItemId, 1))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Impossible de consommer le plan."));
                return;
            }

            var project = new VehicleProject
            {
                id = _projects.nextId++,
                districtId = district.id,
                workshopId = workshop.id,
                modelId = blueprint.modelId,
                modelName = blueprint.name,
                tier = blueprint.tier,
                ownerSteamId = steamId,
                ownerName = Utils.Name(player),
                stageIndex = 0,
                startedAt = Utils.NowUnix(),
                lastActivityAt = Utils.NowUnix(),
                position = new Position(workshop.position.ToVector3()),
            };

            _projects.projects.Add(project);

            if (!Save())
            {
                _projects.projects.Remove(project);
                Utils.GiveItem(player, blueprint.planItemId, 1);
                Utils.Send(player, prefix + Ui.Bad("✕ Sauvegarde impossible, le plan vous a ete rendu."));
                return;
            }

            Utils.Send(player, prefix + Ui.Ok("✓ Chantier #" + project.id + " ouvert : " + blueprint.name + "."));
            _plugin.Webhook.LogStaffAction(Utils.Name(player), steamId,
                "a ouvert le chantier #" + project.id + " (" + blueprint.name + ") pour " + district.name);

            OpenProject(player, project);
        }

        // ------------------------------------------------------------------ chantiers

        private void OpenProjectList(Player player, VehicleWorkshop workshop, PostApo.District.District district)
        {
            var projects = _projects.projects
                .Where(p => p.districtId == district.id)
                .OrderBy(p => p.id)
                .ToList();

            if (projects.Count == 0)
            {
                Ui.Info(player, "Chantiers", Ui.Dim("Aucun chantier en cours pour le " + district.name + "."));
                return;
            }

            var entries = projects.Select(p =>
            {
                var captured = p;
                var blueprint = GetBlueprint(captured.modelId);
                var total = blueprint != null ? blueprint.stages.Count : 0;

                return new Ui.MenuEntry(
                    "#" + captured.id + " " + captured.modelName
                    + Ui.Dim("  etape " + (captured.stageIndex + 1) + "/" + total),
                    () => OpenProject(player, captured));
            }).ToList();

            Ui.Menu(player, "Chantiers en cours", district.name, entries,
                "Retour", () => OpenWorkshop(player, workshop));
        }

        /// <summary>
        /// Retrouve l'instance vivante d'un chantier.
        ///
        /// Un <c>/postapo reload</c> remplace la liste en memoire : un menu ouvert ou une coroutine
        /// en cours detiendrait alors un objet orphelin, et ses modifications seraient perdues au
        /// prochain enregistrement. On repasse donc systematiquement par l'id.
        /// </summary>
        private VehicleProject Live(VehicleProject project)
        {
            if (project == null) { return null; }
            return _projects.projects.FirstOrDefault(p => p != null && p.id == project.id);
        }

        public void OpenProject(Player player, VehicleProject project)
        {
            project = Live(project);
            if (player == null || project == null) { return; }

            var blueprint = GetBlueprint(project.modelId);
            if (blueprint == null || !blueprint.Valid)
            {
                Ui.Info(player, "Chantier #" + project.id,
                    Ui.Bad("Le plan de ce vehicule n'est plus disponible sur ce serveur."));
                return;
            }

            if (project.stageIndex >= blueprint.stages.Count)
            {
                Ui.Info(player, "Chantier #" + project.id, Ui.Ok("Toutes les etapes sont terminees."));
                return;
            }

            var stage = blueprint.stages[project.stageIndex];
            var body = "<b>" + project.modelName + "</b>  "
                       + Ui.Dim("etape " + (project.stageIndex + 1) + "/" + blueprint.stages.Count) + "\n"
                       + Ui.Accent(stage.name) + "\n"
                       + Ui.Dim(stage.description) + "\n"
                       + Ui.Dim("Travaux : " + CraftEngine.FormatDuration(EffectiveWork(stage))
                                + " · ouvert par " + project.ownerName);

            var entries = new List<Ui.MenuEntry>();
            var complete = true;

            if (project.Working)
            {
                entries.Add(new Ui.MenuEntry(Ui.Dim("⏳ Travaux en cours..."), 0, "", null));
            }

            // Chaque materiau est une ligne avec son icone, sa quantite livree et ce que le joueur
            // porte : il voit d'un coup d'oeil quoi aller chercher.
            foreach (var input in stage.inputs)
            {
                var need = input.qty;
                var have = project.DeliveredOf(input.ResolvedId);
                var ok = have >= need;
                if (!ok) { complete = false; }

                var carried = Utils.CountItem(player, input.ResolvedId);

                entries.Add(new Ui.MenuEntry(
                    (ok ? Ui.Ok("✓ ") : Ui.Bad("✕ ")) + Utils.ItemName(input.ResolvedId)
                    + (!ok && carried > 0 ? Ui.Dim("  (sur vous : " + carried + ")") : ""),
                    input.ResolvedId,
                    have + "/" + need,
                    null));
            }

            if (!project.Working)
            {
                if (!complete)
                {
                    entries.Insert(0, new Ui.MenuEntry(Ui.Accent("▶ LIVRER MES MATERIAUX"),
                        Mat.BoiteAOutils, "", () => Deliver(player, project)));
                }
                else
                {
                    entries.Insert(0, new Ui.MenuEntry(Ui.Ok("▶ LANCER LES TRAVAUX"),
                        Mat.BoiteAOutils, "", () => BeginStageWork(player, project)));
                }
            }

            if (project.contributions.Count > 0)
            {
                var top = project.contributions.OrderByDescending(kv => kv.Value).Take(3)
                    .Select(kv =>
                    {
                        string n;
                        return (project.contributorNames.TryGetValue(kv.Key, out n) ? n : "?") + " (" + kv.Value + ")";
                    });

                entries.Add(new Ui.MenuEntry(Ui.Dim("Contributeurs : " + string.Join(", ", top.ToArray())),
                    0, "", null));
            }

            var canAbandon = project.ownerSteamId == Utils.SteamId(player)
                             || Utils.IsStaff(player, _plugin.Config.staffLevelMin);

            if (canAbandon && !project.Working)
            {
                entries.Add(new Ui.MenuEntry(Ui.Bad("Abandonner le chantier"), 0, "",
                    () => ConfirmAbandon(player, project)));
            }

            Ui.Menu(player, "Chantier #" + project.id, body, entries, "Fermer", null);
        }

        private float EffectiveWork(VehicleStage stage)
        {
            var multiplier = Mathf.Max(0.1f, _plugin.Config.difficulty.craftTimeMultiplier);
            return Mathf.Max(1f, stage.workSeconds * multiplier);
        }

        private float EffectiveFailure(VehicleStage stage)
        {
            var chance = stage.failureChance >= 0f
                ? stage.failureChance
                : _plugin.Config.difficulty.craftFailureChance;

            return Mathf.Clamp01(chance);
        }

        /// <summary>
        /// Livre tout ce que le joueur porte et qui manque encore. Les livraisons sont partielles
        /// et cumulatives : plusieurs joueurs peuvent alimenter le meme chantier.
        /// </summary>
        private void Deliver(Player player, VehicleProject project)
        {
            var prefix = _plugin.Prefix;

            project = Live(project);
            if (project == null)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Ce chantier n'existe plus."));
                return;
            }

            var blueprint = GetBlueprint(project.modelId);
            if (blueprint == null || project.stageIndex >= blueprint.stages.Count) { return; }

            if (project.Working)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Les travaux sont en cours."));
                return;
            }

            if (Utils.Distance(player, project.position.ToVector3()) > _plugin.Config.etabli.interactionRadius + 2f)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Rapprochez-vous de l'atelier."));
                return;
            }

            var stage = blueprint.stages[project.stageIndex];
            var moved = new List<string>();
            var totalMoved = 0;

            foreach (var input in stage.inputs)
            {
                var missing = input.qty - project.DeliveredOf(input.ResolvedId);
                if (missing <= 0) { continue; }

                var carried = Utils.CountItem(player, input.ResolvedId);
                var amount = Math.Min(missing, carried);
                if (amount <= 0) { continue; }

                if (!Utils.TakeItem(player, input.ResolvedId, amount)) { continue; }

                project.AddDelivered(input.ResolvedId, amount);
                moved.Add(amount + " × " + Utils.ItemName(input.ResolvedId));
                totalMoved += amount;
            }

            if (totalMoved == 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous ne portez aucun materiau utile a cette etape."));
                return;
            }

            project.AddContribution(Utils.SteamId(player), Utils.Name(player), totalMoved);
            project.lastActivityAt = Utils.NowUnix();
            Save();

            Utils.Send(player, prefix + Ui.Ok("✓ Livre : " + string.Join(", ", moved.ToArray()) + "."));
            OpenProject(player, project);
        }

        private void BeginStageWork(Player player, VehicleProject project)
        {
            var prefix = _plugin.Prefix;

            project = Live(project);
            if (project == null)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Ce chantier n'existe plus."));
                return;
            }

            var blueprint = GetBlueprint(project.modelId);
            if (blueprint == null || project.stageIndex >= blueprint.stages.Count) { return; }

            if (project.Working)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Les travaux sont deja en cours."));
                return;
            }

            var stage = blueprint.stages[project.stageIndex];

            // Re-verification serveur : l'etat a pu changer depuis l'ouverture du menu.
            foreach (var input in stage.inputs)
            {
                if (project.DeliveredOf(input.ResolvedId) < input.qty)
                {
                    Utils.Send(player, prefix + Ui.Bad("✕ Il manque encore des materiaux."));
                    return;
                }
            }

            // Derniere etape : l'immatriculation exige un proprietaire en base. On refuse de lancer
            // les travaux plutot que de perdre le chantier a l'arrivee.
            var isFinal = project.stageIndex == blueprint.stages.Count - 1;
            if (isFinal && OwnerCharacterId(project) <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ " + project.ownerName
                    + " doit etre connecte : le vehicule sera immatricule a son nom."));
                return;
            }

            var host = LifeManager.instance as MonoBehaviour;
            if (host == null)
            {
                CompleteStage(project, blueprint, stage, true);
                return;
            }

            project.Working = true;
            host.StartCoroutine(StageRoutine(player, project, blueprint, stage));
        }

        /// <summary>Identifiant de personnage du proprietaire, en ligne ou connu du district. 0 si introuvable.</summary>
        private int OwnerCharacterId(VehicleProject project)
        {
            var owner = Utils.FindOnlineBySteamId(project.ownerSteamId);
            var characterId = owner != null ? Utils.CharacterId(owner) : 0;
            if (characterId > 0) { return characterId; }

            var district = _plugin.Districts.Get(project.districtId);
            if (district == null) { return 0; }

            var member = district.FindMember(project.ownerSteamId);
            return member != null ? member.characterId : 0;
        }

        private IEnumerator StageRoutine(Player player, VehicleProject project,
                                         VehicleBlueprint blueprint, VehicleStage stage)
        {
            var total = EffectiveWork(stage);
            var elapsed = 0f;
            var wait = new WaitForSeconds(5f);

            Utils.Center(player, project.modelName, stage.name + " — travaux en cours", 4f);

            // Les travaux avancent meme si le joueur s'eloigne : un chantier est un ouvrage collectif,
            // pas une action maintenue. Seul le temps compte.
            while (elapsed < total)
            {
                yield return wait;
                elapsed += 5f;
            }

            project.Working = false;
            CompleteStage(project, blueprint, stage, Utils.RandomDouble() >= EffectiveFailure(stage));
        }

        private void CompleteStage(VehicleProject project, VehicleBlueprint blueprint,
                                   VehicleStage stage, bool success)
        {
            var prefix = _plugin.Prefix;

            // Le chantier a pu etre recharge ou abandonne pendant les travaux.
            project = Live(project);
            if (project == null) { return; }
            project.Working = false;

            if (!success)
            {
                // L'etape rate : les materiaux de l'etape sont perdus, mais le chantier survit.
                project.ResetStageDeliveries();
                project.lastActivityAt = Utils.NowUnix();
                Save();

                Announce(project, prefix + Ui.Bad("✕ " + project.modelName + " — « " + stage.name
                                                  + " » a echoue. Les materiaux de l'etape sont perdus."));
                _plugin.Webhook.LogCraft(project.ownerName, project.ownerSteamId,
                    "chantier #" + project.id + " — echec de « " + stage.name + " »", false);
                return;
            }

            var isFinal = project.stageIndex >= blueprint.stages.Count - 1;

            if (!isFinal)
            {
                project.stageIndex++;
                project.ResetStageDeliveries();
                project.lastActivityAt = Utils.NowUnix();
                Save();

                var next = blueprint.stages[project.stageIndex];
                Announce(project, prefix + Ui.Ok("✓ " + project.modelName + " — « " + stage.name + " » terminee.")
                                  + "\n" + Ui.Dim("Etape suivante : " + next.name));

                _plugin.Webhook.LogCraft(project.ownerName, project.ownerSteamId,
                    "chantier #" + project.id + " — « " + stage.name + " » terminee", true);
                return;
            }

            // Derniere etape : on fabrique le vehicule pour de vrai. Le chantier n'est ni avance ni
            // vide tant que l'immatriculation n'a pas abouti — un echec technique doit rester
            // rejouable, pas detruire des heures de travail collectif.
            FinishProject(project, blueprint);
        }

        /// <summary>Informe tous les membres du district connectes : un chantier est une affaire collective.</summary>
        private void Announce(VehicleProject project, string message)
        {
            var district = _plugin.Districts.Get(project.districtId);
            if (district == null) { return; }

            foreach (var member in district.members.ToArray())
            {
                var online = Utils.FindOnlineBySteamId(member.steamId);
                if (online != null) { Utils.Send(online, message); }
            }
        }

        // ------------------------------------------------------------------ creation du vehicule

        private void FinishProject(VehicleProject project, VehicleBlueprint blueprint)
        {
            var host = LifeManager.instance as MonoBehaviour;
            if (host == null)
            {
                Utils.Error("LifeManager indisponible : creation du vehicule impossible.");
                return;
            }

            host.StartCoroutine(CreateVehicleRoutine(project, blueprint));
        }

        private IEnumerator CreateVehicleRoutine(VehicleProject project, VehicleBlueprint blueprint)
        {
            var prefix = _plugin.Prefix;
            var characterId = OwnerCharacterId(project);

            if (characterId <= 0)
            {
                // Le chantier reste intact, materiaux livres compris : il suffira de relancer
                // les travaux quand le proprietaire sera connecte.
                Announce(project, prefix + Ui.Bad("✕ " + project.modelName
                    + " est pret, mais " + project.ownerName + " doit etre connecte pour l'immatriculer."));
                yield break;
            }

            string permissions;
            try
            {
                permissions = JsonConvert.SerializeObject(new Permissions
                {
                    owner = new Entity { characterId = characterId, groupId = 0u },
                    coOwners = new List<Entity>(),
                });
            }
            catch (Exception ex)
            {
                Utils.Error("serialisation des permissions vehicule : " + ex.Message);
                yield break;
            }

            System.Threading.Tasks.Task<Vehicles> task = null;
            try
            {
                task = LifeDB.CreateVehicle(blueprint.modelId, permissions);
            }
            catch (Exception ex)
            {
                Utils.Error("LifeDB.CreateVehicle : " + ex.Message);
                _plugin.Webhook.LogError("CreateVehicle chantier #" + project.id, ex);
            }

            if (task == null)
            {
                Announce(project, prefix + Ui.Bad("✕ Immatriculation impossible. Le chantier reste ouvert, "
                                                  + "prevenez le staff."));
                yield break;
            }

            // Attente non bloquante de la tache asynchrone de la base (~10 s a 60 FPS).
            var guard = 0;
            while (!task.IsCompleted && guard < 600)
            {
                guard++;
                yield return null;
            }

            if (!task.IsCompleted || task.IsFaulted || task.Result == null)
            {
                Utils.Error("creation du vehicule echouee pour le chantier #" + project.id
                            + (task.Exception != null ? " : " + task.Exception.Message : " (delai depasse)"));
                _plugin.Webhook.LogError("CreateVehicle chantier #" + project.id, task.Exception);

                Announce(project, prefix + Ui.Bad("✕ Immatriculation impossible. Le chantier reste ouvert, "
                                                  + "relancez les travaux."));
                yield break;
            }

            var dbVehicle = task.Result;
            var spawnPosition = project.position.ToVector3() + Vector3.up * 0.5f;

            try
            {
                var manager = Nova.v;
                if (manager == null) { throw new Exception("VehiclesManager indisponible"); }

                manager.LoadNewVehicle(dbVehicle);

                var lifeVehicle = manager.GetVehicle(dbVehicle.Id);
                if (lifeVehicle == null) { throw new Exception("vehicule introuvable apres creation"); }

                lifeVehicle.x = spawnPosition.x;
                lifeVehicle.y = spawnPosition.y;
                lifeVehicle.z = spawnPosition.z;
                lifeVehicle.isStowed = false;

                manager.SpawnVehicle(lifeVehicle, spawnPosition, Quaternion.identity);
                lifeVehicle.Save();
            }
            catch (Exception ex)
            {
                Utils.Error("apparition du vehicule : " + ex.Message);
                _plugin.Webhook.LogError("SpawnVehicle chantier #" + project.id, ex);
                Announce(project, prefix + Ui.Bad("✕ Le vehicule est immatricule mais n'a pas pu apparaitre. "
                                                  + "Le staff peut le sortir du garage."));
            }

            // Deblocage du palier suivant pour le district.
            var current = UnlockedTier(project.districtId);
            var unlockedNew = false;
            if (project.tier >= current && current < 5)
            {
                _projects.SetTier(project.districtId, current + 1);
                unlockedNew = true;
            }

            _projects.AddCompleted(project.districtId);

            // Le vehicule existe desormais en base : le chantier doit disparaitre meme si la liste
            // a ete rechargee entre-temps, sous peine de pouvoir etre termine deux fois.
            _projects.projects.Remove(Live(project) ?? project);
            Save();

            var message = prefix + Ui.Ok("✓ " + project.modelName + " est sorti de l'atelier !");
            if (unlockedNew)
            {
                message += "\n" + Ui.Accent("★ Palier " + (current + 1) + " debloque : "
                                            + TierName(current + 1) + ".");
            }

            Announce(project, message);

            _plugin.Webhook.LogCraft(project.ownerName, project.ownerSteamId,
                "VEHICULE TERMINE : " + project.modelName + " (palier " + project.tier
                + ", chantier #" + project.id + ")" + (unlockedNew ? " — palier " + (current + 1) + " debloque" : ""),
                true);
        }

        // ------------------------------------------------------------------ abandon

        private void ConfirmAbandon(Player player, VehicleProject project)
        {
            Ui.Confirm(player, "Abandonner #" + project.id,
                Ui.Bad("Tous les materiaux deja livres seront perdus.") + "\n\n"
                + Ui.Dim("Le plan ne sera pas rendu."),
                Ui.Bad("Oui, abandonner"), "Annuler",
                () =>
                {
                    _projects.projects.Remove(Live(project) ?? project);
                    Save();
                    Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Chantier #" + project.id + " abandonne."));
                    _plugin.Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                        "a abandonne le chantier #" + project.id + " (" + project.modelName + ")");
                },
                () => OpenProject(player, project));
        }

        // ------------------------------------------------------------------ administration

        public void SetTier(int districtId, int tier)
        {
            _projects.SetTier(districtId, Mathf.Clamp(tier, 1, 5));
            Save();
        }

        public IEnumerable<VehicleProject> ProjectsOf(int districtId)
        {
            return _projects.projects.Where(p => p != null && p.districtId == districtId);
        }
    }
}
