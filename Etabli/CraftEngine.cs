using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.Network;
using PostApo.Core;
using UnityEngine;

namespace PostApo.Etabli
{
    /// <summary>
    /// Moteur de craft partage entre l'etabli personnel et les ateliers specialises des districts.
    ///
    /// Regles anti-abus :
    ///  - un seul craft simultane par joueur ;
    ///  - les materiaux sont consommes au demarrage, pas a la fin : impossible de dupliquer en
    ///    lancant plusieurs crafts ou en se deconnectant a la derniere seconde ;
    ///  - le joueur doit rester pres de l'etabli pendant toute la duree ;
    ///  - la place en inventaire est verifiee avant de commencer.
    /// </summary>
    public sealed class CraftEngine
    {
        private readonly PostApoPlugin _plugin;
        private readonly JsonStore<RecipeData> _store;
        private RecipeData _data;

        /// <summary>Crafts en cours, indexes par SteamID.</summary>
        private readonly Dictionary<string, Coroutine> _running = new Dictionary<string, Coroutine>();

        public CraftEngine(PostApoPlugin plugin, string root)
        {
            _plugin = plugin;
            _store = new JsonStore<RecipeData>(root, "recipes.json");
            Reload();
        }

        public IEnumerable<Recipe> AllRecipes { get { return _data.recipes ?? new List<Recipe>(); } }

        public int ValidCount { get { return AllRecipes.Count(r => r.Valid); } }
        public int InvalidCount { get { return AllRecipes.Count(r => !r.Valid); } }

        /// <summary>Recharge et revalide toutes les recettes contre le catalogue d'items du serveur.</summary>
        public void Reload()
        {
            _data = _store.Load();
            if (_data.recipes == null) { _data.recipes = new List<Recipe>(); }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var recipe in _data.recipes)
            {
                if (recipe == null) { continue; }

                recipe.Valid = true;
                recipe.InvalidReason = string.Empty;

                if (string.IsNullOrWhiteSpace(recipe.id) || !seen.Add(recipe.id.Trim()))
                {
                    recipe.Valid = false;
                    recipe.InvalidReason = "identifiant manquant ou en double";
                    continue;
                }

                if (recipe.inputs == null) { recipe.inputs = new List<RecipeItem>(); }

                if (recipe.output == null)
                {
                    recipe.Valid = false;
                    recipe.InvalidReason = "aucun resultat defini";
                    continue;
                }

                foreach (var input in recipe.inputs)
                {
                    input.ResolvedId = Utils.ResolveItemId(input.slug, input.itemId);
                    if (input.ResolvedId <= 0 || !Utils.ItemExists(input.ResolvedId))
                    {
                        recipe.Valid = false;
                        recipe.InvalidReason = "ingredient introuvable ("
                                               + (string.IsNullOrWhiteSpace(input.slug) ? "id " + input.itemId : input.slug) + ")";
                    }

                    if (input.qty <= 0) { input.qty = 1; }
                }

                recipe.output.ResolvedId = Utils.ResolveItemId(recipe.output.slug, recipe.output.itemId);
                if (recipe.output.ResolvedId <= 0 || !Utils.ItemExists(recipe.output.ResolvedId))
                {
                    recipe.Valid = false;
                    recipe.InvalidReason = "resultat introuvable ("
                                           + (string.IsNullOrWhiteSpace(recipe.output.slug) ? "id " + recipe.output.itemId : recipe.output.slug) + ")";
                }

                if (recipe.output.qty <= 0) { recipe.output.qty = 1; }

                recipe.ResolvedToolId = Utils.ResolveItemId(recipe.toolSlug, recipe.toolItemId);
                if (recipe.craftTime < 1f) { recipe.craftTime = 1f; }
            }

            // Tant que le catalogue d'items n'est pas peuple, l'invalidite n'a aucun sens :
            // le plugin relancera cette validation des que le jeu sera pret.
            if (!Utils.ItemsReady()) { return; }

            var invalid = _data.recipes.Where(r => r != null && !r.Valid).ToArray();
            if (invalid.Length > 0)
            {
                Utils.Warn(invalid.Length + " recette(s) desactivee(s) — items absents de ce serveur :");
                foreach (var recipe in invalid.Take(20))
                {
                    Utils.Warn("  - " + recipe.id + " : " + recipe.InvalidReason);
                }

                Utils.Warn("Utilisez /postapo finditem <texte> en jeu pour retrouver les slugs corrects.");
            }
        }

        public bool Save() { return _store.Save(_data); }

        // ------------------------------------------------------------------ selection

        /// <summary>Recettes de l'etabli generique (celles sans specialite).</summary>
        public IEnumerable<Recipe> GenericRecipes()
        {
            return AllRecipes.Where(r => r != null && r.Valid && string.IsNullOrWhiteSpace(r.specialite));
        }

        /// <summary>Recettes reservees a une specialite de district.</summary>
        public IEnumerable<Recipe> RecipesForSpecialite(string specialite)
        {
            if (string.IsNullOrWhiteSpace(specialite)) { return GenericRecipes(); }
            var wanted = specialite.Trim();

            return AllRecipes.Where(r => r != null && r.Valid
                                         && string.Equals(r.specialite, wanted, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<string> KnownSpecialites()
        {
            return AllRecipes.Where(r => r != null && !string.IsNullOrWhiteSpace(r.specialite))
                             .Select(r => r.specialite.Trim().ToLowerInvariant())
                             .Distinct()
                             .OrderBy(s => s);
        }

        // ------------------------------------------------------------------ interface

        public void OpenMenu(Player player, string title, string header, List<Recipe> recipes, Vector3 anchor)
        {
            if (player == null) { return; }

            if (recipes == null || recipes.Count == 0)
            {
                Ui.Info(player, title, header + "\n\n" + Ui.Dim("Aucune recette disponible ici."));
                return;
            }

            var entries = new List<Ui.MenuEntry>();
            var ready = 0;

            foreach (var recipe in recipes)
            {
                var captured = recipe;
                var missing = MissingCount(player, captured);
                var affordable = missing == 0;
                if (affordable) { ready++; }

                // La colonne de droite reste courte par principe : y lister tous les manques
                // deborde des qu'une recette demande plus de deux ou trois ingredients.
                // Le detail complet est une page a part, ou chaque ressource a sa propre ligne.
                var priceText = affordable
                    ? Ui.Ok("✓ prêt")
                    : Ui.Bad("✕ " + missing + " manque" + (missing > 1 ? "s" : ""));

                var label = (affordable ? Ui.Ok("● ") : Ui.Bad("○ "))
                            + captured.name
                            + Ui.Dim("  → " + captured.output.qty + " × "
                                     + Utils.ItemName(captured.output.ResolvedId));

                entries.Add(new Ui.MenuEntry(label, captured.output.ResolvedId, priceText,
                    () => OpenRecipeDetail(player, title, header, recipes, captured, anchor)));
            }

            var summary = Ui.Dim(recipes.Count + " recette(s) — ")
                          + Ui.Ok(ready + " réalisable(s)")
                          + Ui.Dim(" · cliquez pour voir les ressources");

            Ui.Menu(player, title, header + "\n" + summary, entries, "Fermer", null);
        }

        /// <summary>Nombre d'ingredients (outil compris) dont le joueur manque.</summary>
        private int MissingCount(Player player, Recipe recipe)
        {
            var missing = 0;

            foreach (var input in recipe.inputs)
            {
                if (Utils.CountItem(player, input.ResolvedId) < input.qty) { missing++; }
            }

            if (recipe.ResolvedToolId > 0 && Utils.CountItem(player, recipe.ResolvedToolId) <= 0)
            {
                missing++;
            }

            return missing;
        }

        private void OpenRecipeDetail(Player player, string title, string header, List<Recipe> recipes,
                                      Recipe recipe, Vector3 anchor)
        {
            if (player == null || recipe == null) { return; }

            // Le resume reste court pour tenir sur une page : le detail chiffre vit dans les
            // lignes ci-dessous, ou chaque ressource occupe sa propre entree avec son icone.
            var failPct = Mathf.RoundToInt(EffectiveFailure(recipe) * 100f);

            var body = Ui.Accent("Produit : " + recipe.output.qty + " × "
                                 + Utils.ItemName(recipe.output.ResolvedId)) + "\n";

            if (!string.IsNullOrWhiteSpace(recipe.description))
            {
                body += Ui.Dim(recipe.description) + "\n";
            }

            body += Ui.Dim("Durée " + FormatDuration(EffectiveTime(recipe))
                           + (failPct > 0 ? " · risque d'échec " + failPct + "%" : "")
                           + " · ressources prélevées au démarrage");

            var entries = new List<Ui.MenuEntry>();
            var canStart = true;

            foreach (var input in recipe.inputs)
            {
                var have = Utils.CountItem(player, input.ResolvedId);
                var enough = have >= input.qty;
                if (!enough) { canStart = false; }

                entries.Add(new Ui.MenuEntry(
                    (enough ? Ui.Ok("✓ ") : Ui.Bad("✕ ")) + Utils.ItemName(input.ResolvedId)
                    + (enough ? "" : Ui.Bad("  il manque " + (input.qty - have))),
                    input.ResolvedId,
                    have + "/" + input.qty,
                    null));
            }

            if (recipe.ResolvedToolId > 0)
            {
                var hasTool = Utils.CountItem(player, recipe.ResolvedToolId) > 0;
                if (!hasTool) { canStart = false; }

                entries.Add(new Ui.MenuEntry(
                    (hasTool ? Ui.Ok("✓ ") : Ui.Bad("✕ ")) + "Outil : "
                    + Utils.ItemName(recipe.ResolvedToolId) + Ui.Dim("  (non consommé)"),
                    recipe.ResolvedToolId,
                    hasTool ? "ok" : "requis",
                    null));
            }

            // Le bouton d'action est en tete : c'est ce que le joueur cherche en priorite.
            if (canStart)
            {
                entries.Insert(0, new Ui.MenuEntry(Ui.Ok("▶ FABRIQUER"),
                    recipe.output.ResolvedId, "", () => Start(player, recipe, anchor)));
            }
            else
            {
                entries.Insert(0, new Ui.MenuEntry(Ui.Bad("✕ Ressources insuffisantes"),
                    recipe.output.ResolvedId, "", null));
            }

            entries.Add(new Ui.MenuEntry("← Retour à la liste", 0, "",
                () => OpenMenu(player, title, header, recipes, anchor)));

            Ui.Menu(player, recipe.name, body, entries, "Fermer", null);
        }

        // ------------------------------------------------------------------ execution

        public float EffectiveTime(Recipe recipe)
        {
            var multiplier = Mathf.Max(0.1f, _plugin.Config.difficulty.craftTimeMultiplier);
            return Mathf.Max(1f, recipe.craftTime * multiplier);
        }

        public float EffectiveFailure(Recipe recipe)
        {
            var chance = recipe.failureChance >= 0f
                ? recipe.failureChance
                : _plugin.Config.difficulty.craftFailureChance;

            return Mathf.Clamp01(chance);
        }

        public bool HasAllInputs(Player player, Recipe recipe)
        {
            if (player == null || recipe == null || !recipe.Valid) { return false; }

            if (recipe.ResolvedToolId > 0 && Utils.CountItem(player, recipe.ResolvedToolId) <= 0) { return false; }

            foreach (var input in recipe.inputs)
            {
                if (Utils.CountItem(player, input.ResolvedId) < input.qty) { return false; }
            }

            return true;
        }

        public bool IsBusy(Player player)
        {
            var steamId = Utils.SteamId(player);
            return !string.IsNullOrEmpty(steamId) && _running.ContainsKey(steamId);
        }

        public void Start(Player player, Recipe recipe, Vector3 anchor)
        {
            if (player == null || recipe == null) { return; }

            var steamId = Utils.SteamId(player);
            var prefix = _plugin.Prefix;

            if (!recipe.Valid)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Cette recette est indisponible sur ce serveur."));
                return;
            }

            if (IsBusy(player))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous etes deja en train de fabriquer quelque chose."));
                return;
            }

            if (Utils.Distance(player, anchor) > _plugin.Config.etabli.interactionRadius + 1f)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Rapprochez-vous de l'etabli."));
                return;
            }

            if (recipe.ResolvedToolId > 0 && Utils.CountItem(player, recipe.ResolvedToolId) <= 0)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Il vous faut : " + Utils.ItemName(recipe.ResolvedToolId) + "."));
                return;
            }

            if (!HasAllInputs(player, recipe))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Vous n'avez pas les ressources necessaires."));
                return;
            }

            if (!Utils.CanGiveItem(player, recipe.output.ResolvedId, recipe.output.qty))
            {
                Utils.Send(player, prefix + Ui.Bad("✕ Votre inventaire est trop plein pour recevoir le resultat."));
                return;
            }

            // Consommation immediate : toute duplication par craft parallele ou deconnexion est bloquee.
            var consumed = new List<RecipeItem>();
            foreach (var input in recipe.inputs)
            {
                if (Utils.TakeItem(player, input.ResolvedId, input.qty))
                {
                    consumed.Add(input);
                }
                else
                {
                    // Retour arriere complet si un retrait echoue en cours de route.
                    foreach (var done in consumed)
                    {
                        Utils.GiveItem(player, done.ResolvedId, done.qty);
                    }

                    Utils.Send(player, prefix + Ui.Bad("✕ Vous n'avez pas les ressources necessaires."));
                    return;
                }
            }

            var host = LifeManager.instance as MonoBehaviour;
            if (host == null)
            {
                // Sans coroutine possible, on livre immediatement plutot que de perdre les materiaux.
                Finish(player, recipe, true);
                return;
            }

            _running[steamId] = host.StartCoroutine(CraftRoutine(player, steamId, recipe, anchor));
        }

        private IEnumerator CraftRoutine(Player player, string steamId, Recipe recipe, Vector3 anchor)
        {
            var total = EffectiveTime(recipe);
            var elapsed = 0f;
            var maxDrift = Mathf.Max(2f, _plugin.Config.etabli.interactionRadius + 2f);
            var wait = new WaitForSeconds(1f);

            Utils.Center(player, recipe.name, "Fabrication en cours...", 2f);

            while (elapsed < total)
            {
                yield return wait;
                elapsed += 1f;

                // Le joueur s'est deconnecte : le craft est perdu, materiaux compris.
                if (player == null || player.setup == null)
                {
                    _running.Remove(steamId);
                    yield break;
                }

                if (Vector3.Distance(Utils.Position(player), anchor) > maxDrift)
                {
                    _running.Remove(steamId);
                    Cancel(player, recipe);
                    yield break;
                }

                if (elapsed % 5f < 0.01f || Mathf.Abs(total - elapsed) < 1.01f)
                {
                    var percent = Mathf.Clamp(Mathf.RoundToInt(elapsed / total * 100f), 0, 100);
                    Utils.Center(player, recipe.name, percent + " %", 1.5f);
                }
            }

            _running.Remove(steamId);
            Finish(player, recipe, Utils.RandomDouble() >= EffectiveFailure(recipe));
        }

        private void Cancel(Player player, Recipe recipe)
        {
            var ratio = Mathf.Clamp01(_plugin.Config.difficulty.craftCancelRefundRatio);
            var refunded = new List<string>();

            foreach (var input in recipe.inputs)
            {
                var amount = Mathf.FloorToInt(input.qty * ratio);
                if (amount <= 0) { continue; }

                if (Utils.GiveItem(player, input.ResolvedId, amount))
                {
                    refunded.Add(amount + " × " + Utils.ItemName(input.ResolvedId));
                }
            }

            var message = _plugin.Prefix + Ui.Bad("✕ Travail interrompu : vous vous etes trop eloigne.");
            if (refunded.Count > 0)
            {
                message += "\n" + Ui.Dim("Recupere : " + string.Join(", ", refunded.ToArray()));
            }

            Utils.Send(player, message);
            Utils.Center(player, "Travail interrompu", recipe.name, 3f);
        }

        private void Finish(Player player, Recipe recipe, bool success)
        {
            if (player == null) { return; }

            var prefix = _plugin.Prefix;

            if (!success)
            {
                Utils.Send(player, prefix + Ui.Bad("✕ La piece est ratee. Les materiaux sont perdus."));
                Utils.Center(player, "Echec", recipe.name, 4f);
                _plugin.Webhook.LogCraft(Utils.Name(player), Utils.SteamId(player), recipe.name + " (materiaux perdus)", false);
                return;
            }

            if (!Utils.GiveItem(player, recipe.output.ResolvedId, recipe.output.qty))
            {
                // Inventaire devenu plein pendant le craft : on rend les materiaux plutot que de tout perdre.
                foreach (var input in recipe.inputs)
                {
                    Utils.GiveItem(player, input.ResolvedId, input.qty);
                }

                Utils.Send(player, prefix + Ui.Bad("✕ Inventaire plein : les materiaux vous ont ete rendus."));
                return;
            }

            var label = recipe.output.qty + " × " + Utils.ItemName(recipe.output.ResolvedId);
            Utils.Send(player, prefix + Ui.Ok("✓ Fabrication terminee : " + label + "."));
            Utils.Center(player, "Termine", label, 4f);
            _plugin.Webhook.LogCraft(Utils.Name(player), Utils.SteamId(player), label, true);
        }

        /// <summary>Interrompt le craft d'un joueur qui se deconnecte.</summary>
        public void AbortFor(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) { return; }

            Coroutine routine;
            if (!_running.TryGetValue(steamId, out routine)) { return; }

            _running.Remove(steamId);

            try
            {
                var host = LifeManager.instance as MonoBehaviour;
                if (host != null && routine != null) { host.StopCoroutine(routine); }
            }
            catch { }
        }

        public void AbortAll()
        {
            foreach (var steamId in _running.Keys.ToArray())
            {
                AbortFor(steamId);
            }
        }

        public static string FormatDuration(float seconds)
        {
            var total = Mathf.RoundToInt(seconds);
            if (total < 60) { return total + " s"; }

            var minutes = total / 60;
            var rest = total % 60;
            return rest == 0 ? minutes + " min" : minutes + " min " + rest + " s";
        }
    }
}
