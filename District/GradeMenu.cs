using System;
using System.Collections.Generic;
using System.Linq;
using Life.Network;
using PostApo.Core;

namespace PostApo.District
{
    /// <summary>
    /// Gestion des grades en jeu, reservee au proprietaire du district (et au staff).
    ///
    /// Le proprietaire cree ses propres grades, les hierarchise et coche leurs permissions.
    /// Le modele par defaut illustre l'intention : un adjoint a tout, un officier a tout sauf
    /// poser et retirer des objets, un membre n'a que l'acces.
    /// </summary>
    public sealed class GradeMenu
    {
        /// <summary>Icones d'items utilisees pour rendre les listes lisibles.</summary>
        private const int IconGrade = 1181;   // Livre
        private const int IconMember = 1202;  // Feuille de papier
        private const int IconTerrain = 1077; // Tableau carte d'Amboise
        private const int IconVehicle = 1530; // Pneu
        private const int IconOn = 1219;      // Verre
        private const int IconOff = 29;       // Pierre

        private readonly PostApoPlugin _plugin;
        private readonly DistrictSystem _districts;

        public GradeMenu(PostApoPlugin plugin, DistrictSystem districts)
        {
            _plugin = plugin;
            _districts = districts;
        }

        /// <summary>
        /// Autorite pleine : creer, supprimer, renommer un grade, changer son rang et ses droits.
        /// Reservee au proprietaire et au staff — un delegue ne doit jamais pouvoir se fabriquer
        /// un grade tout-puissant ni s'ajouter des permissions.
        /// </summary>
        private bool IsAdminOf(Player player, District district)
        {
            if (district == null) { return false; }
            return district.IsOwner(Utils.SteamId(player))
                   || Utils.IsStaff(player, _plugin.Config.staffLevelMin);
        }

        /// <summary>Autorite deleguee : affecter un grade a un membre, dans la limite de son propre rang.</summary>
        private bool CanManage(Player player, District district)
        {
            if (district == null) { return false; }

            return IsAdminOf(player, district)
                   || _districts.HasPermission(district, Utils.SteamId(player), Perm.GererGrades);
        }

        /// <summary>
        /// Rang de reference du joueur. Le proprietaire et le staff dominent toute la hierarchie ;
        /// un membre est plafonne par son propre grade.
        /// </summary>
        private int RankOf(Player player, District district)
        {
            if (IsAdminOf(player, district)) { return int.MaxValue; }

            var grade = district.GradeOf(Utils.SteamId(player));
            return grade != null ? grade.rank : 0;
        }

        /// <summary>
        /// Un membre ne peut agir que <b>strictement en dessous</b> de son propre rang : impossible
        /// de se promouvoir a son niveau ou au-dessus, ni de toucher un superieur.
        /// </summary>
        private bool CanActOn(Player player, District district, Grade target)
        {
            if (target == null) { return false; }
            if (IsAdminOf(player, district)) { return true; }

            return target.rank < RankOf(player, district);
        }

        // ------------------------------------------------------------------ racine

        public void Open(Player player, District district)
        {
            if (!CanManage(player, district))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Seul le proprietaire du district peut gerer les grades."));
                return;
            }

            var admin = IsAdminOf(player, district);
            var myRank = RankOf(player, district);

            var body = district.name + "\n"
                       + Ui.Dim("Les grades definissent qui peut ouvrir, prendre, poser et conduire.")
                       + "\n" + (admin
                           ? Ui.Ok("Vous administrez ce district.")
                           : Ui.Dim("Vous ne pouvez affecter que des grades inferieurs au votre (rang "
                                    + myRank + ")."));

            var entries = new List<Ui.MenuEntry>();

            foreach (var grade in district.grades.OrderByDescending(g => g.rank))
            {
                var captured = grade;
                var count = district.members.Count(m => m.gradeId == captured.id);
                var locked = !CanActOn(player, district, captured);

                entries.Add(new Ui.MenuEntry(
                    (locked ? Ui.Dim("🔒 " + captured.name) : captured.name)
                    + Ui.Dim("  rang " + captured.rank),
                    IconGrade,
                    count + " membre" + (count > 1 ? "s" : ""),
                    () =>
                    {
                        if (locked)
                        {
                            Utils.Send(player, _plugin.Prefix
                                + Ui.Bad("✕ Ce grade est au-dessus du votre."));
                            return;
                        }

                        OpenGrade(player, district, captured);
                    }));
            }

            // Creer un grade revient a definir des droits : reserve au proprietaire.
            if (admin)
            {
                entries.Add(new Ui.MenuEntry(Ui.Ok("+ Creer un grade"), IconGrade, "",
                    () => CreateGrade(player, district)));
            }

            entries.Add(new Ui.MenuEntry("Affecter un membre", IconMember, "",
                () => OpenMemberList(player, district)));

            Ui.Menu(player, "Grades — " + Utils.Sanitize(district.name, 20), body, entries, "Fermer", null);
        }

        // ------------------------------------------------------------------ creation

        private void CreateGrade(Player player, District district)
        {
            if (!IsAdminOf(player, district))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Seul le proprietaire peut creer un grade."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player),
                    "tentative de creation de grade dans " + district.name);
                return;
            }

            Ui.Input(player, "Nouveau grade",
                "Nom du grade a creer.\n" + Ui.Dim("Exemples : Adjoint, Officier, Mecanicien, Recrue."),
                "Nom du grade",
                name =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Nom vide."));
                        Open(player, district);
                        return;
                    }

                    var clean = Utils.Sanitize(name, 24);
                    if (district.grades.Any(g => string.Equals(g.name, clean, StringComparison.OrdinalIgnoreCase)))
                    {
                        Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Un grade porte deja ce nom."));
                        Open(player, district);
                        return;
                    }

                    // Le nouveau grade se place juste au-dessus du plus bas, sans droits :
                    // le proprietaire coche ensuite ce qu'il veut accorder.
                    var lowest = district.grades.Count == 0 ? 10 : district.grades.Min(g => g.rank);
                    var grade = new Grade
                    {
                        id = district.grades.Count == 0 ? 1 : district.grades.Max(g => g.id) + 1,
                        name = clean,
                        rank = Math.Max(1, lowest + 5),
                        permissions = new List<string> { Perm.AccesTerrain },
                    };

                    district.grades.Add(grade);
                    _districts.Save();

                    Utils.Send(player, _plugin.Prefix + Ui.Ok("✓ Grade « " + clean + " » cree."));
                    OpenGrade(player, district, grade);
                },
                () => Open(player, district));
        }

        // ------------------------------------------------------------------ fiche d'un grade

        private void OpenGrade(Player player, District district, Grade grade)
        {
            if (!CanActOn(player, district, grade))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Ce grade est au-dessus du votre."));
                return;
            }

            // Modifier des droits, c'est en accorder : seul le proprietaire peut le faire.
            var admin = IsAdminOf(player, district);

            var count = district.members.Count(m => m.gradeId == grade.id);
            var body = "<b>" + grade.name + "</b>\n"
                       + Ui.Dim("Rang " + grade.rank + " · " + count + " membre(s)") + "\n"
                       + (admin
                           ? Ui.Dim("Cochez les droits. Le rang ordonne la hierarchie.")
                           : Ui.Bad("Seul le proprietaire peut modifier les droits d'un grade."));

            var entries = new List<Ui.MenuEntry>();

            if (admin)
            {
                entries.Add(new Ui.MenuEntry("Droits sur les terrains", IconTerrain, "",
                    () => OpenPermissionGroup(player, district, grade, "terrain")));
                entries.Add(new Ui.MenuEntry("Droits sur les vehicules", IconVehicle, "",
                    () => OpenPermissionGroup(player, district, grade, "vehicule")));
                entries.Add(new Ui.MenuEntry("Droits de gestion", IconGrade, "",
                    () => OpenPermissionGroup(player, district, grade, "district")));
                entries.Add(new Ui.MenuEntry("Renommer", 0, "", () => RenameGrade(player, district, grade)));
                entries.Add(new Ui.MenuEntry("Changer le rang", 0, "rang " + grade.rank,
                    () => ChangeRank(player, district, grade)));

                if (district.grades.Count > 1)
                {
                    entries.Add(new Ui.MenuEntry(Ui.Bad("Supprimer ce grade"), 0, "",
                        () => ConfirmDeleteGrade(player, district, grade)));
                }
            }
            else
            {
                // Consultation seule : la liste des droits reste utile a un officier.
                foreach (var key in Perm.TerrainPermissions.Concat(Perm.VehiclePermissions))
                {
                    var enabled = grade.Has(key);
                    entries.Add(new Ui.MenuEntry(
                        (enabled ? Ui.Ok("✓ ") : Ui.Dim("✕ ")) + Label(key),
                        enabled ? IconOn : IconOff,
                        enabled ? "actif" : "inactif",
                        null));
                }
            }

            Ui.Menu(player, grade.name, body, entries, "Retour", () => Open(player, district));
        }

        /// <summary>Libellés lisibles des permissions : accessible depuis PostApoPlugin pour l'audit staff.</summary>
        public static string LabelPublic(string permission) { return Label(permission); }

        /// <summary>Libellés lisibles des permissions : « ouvrirCoffre » ne parle a personne en jeu.</summary>
        private static string Label(string permission)
        {
            switch (permission)
            {
                case Perm.AccesTerrain: return "Entrer sur les terrains";
                case Perm.OuvrirPorte: return "Ouvrir les portes";
                case Perm.OuvrirCoffre: return "Ouvrir les coffres";
                case Perm.PoserItem: return "Poser des objets";
                case Perm.PrendreItem: return "Prendre des objets";
                case Perm.UtiliserVehicule: return "Utiliser les vehicules";
                case Perm.OuvrirVehicule: return "Ouvrir les vehicules";
                case Perm.DemarrerVehicule: return "Demarrer les vehicules";
                case Perm.TeleportBase: return "Rejoindre la base";
                case Perm.CraftDistrict: return "Utiliser les ateliers";
                case Perm.InviterMembre: return "Inviter des membres";
                case Perm.ExpulserMembre: return "Expulser des membres";
                case Perm.GererGrades: return "Gerer les grades";
                case Perm.GererPermissions: return "Gerer les permissions";
                case Perm.GererDistrict: return "Administrer le district";
                default: return permission;
            }
        }

        private void OpenPermissionGroup(Player player, District district, Grade grade, string group)
        {
            // Re-verification serveur : un menu ouvert survit a un changement de situation.
            if (!IsAdminOf(player, district))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Reserve au proprietaire du district."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player),
                    "tentative de modification des droits du grade « " + grade.name + " »");
                return;
            }

            string[] keys;
            string title;

            switch (group)
            {
                case "terrain":
                    keys = Perm.TerrainPermissions;
                    title = "Terrains — " + grade.name;
                    break;
                case "vehicule":
                    keys = Perm.VehiclePermissions;
                    title = "Vehicules — " + grade.name;
                    break;
                default:
                    keys = Perm.DistrictPermissions;
                    title = "Gestion — " + grade.name;
                    break;
            }

            var body = Ui.Dim("Cliquez une ligne pour l'activer ou la desactiver.");
            var entries = new List<Ui.MenuEntry>();

            foreach (var key in keys)
            {
                var captured = key;
                var enabled = grade.Has(captured);

                entries.Add(new Ui.MenuEntry(
                    (enabled ? Ui.Ok("✓ ") : Ui.Bad("✕ ")) + Label(captured),
                    enabled ? IconOn : IconOff,
                    enabled ? "actif" : "inactif",
                    () =>
                    {
                        Toggle(grade, captured);
                        _districts.Save();
                        _districts.SyncSharedProperties(district);
                        OpenPermissionGroup(player, district, grade, group);
                    }));
            }

            Ui.Menu(player, title, body, entries, "Retour", () => OpenGrade(player, district, grade));
        }

        private static void Toggle(Grade grade, string permission)
        {
            if (grade.permissions == null) { grade.permissions = new List<string>(); }

            var existing = grade.permissions
                .FirstOrDefault(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));

            if (existing != null) { grade.permissions.Remove(existing); }
            else { grade.permissions.Add(permission); }
        }

        private void RenameGrade(Player player, District district, Grade grade)
        {
            if (!IsAdminOf(player, district)) { return; }

            Ui.Input(player, "Renommer", "Nouveau nom pour « " + grade.name + " ».", grade.name,
                name =>
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        grade.name = Utils.Sanitize(name, 24);
                        _districts.Save();
                    }

                    OpenGrade(player, district, grade);
                },
                () => OpenGrade(player, district, grade));
        }

        private void ChangeRank(Player player, District district, Grade grade)
        {
            if (!IsAdminOf(player, district)) { return; }

            Ui.Input(player, "Rang de " + grade.name,
                "Un nombre entre 1 et 100.\n" + Ui.Dim("Plus il est eleve, plus le grade est haut place."),
                grade.rank.ToString(),
                value =>
                {
                    int rank;
                    if (int.TryParse(value, out rank))
                    {
                        grade.rank = Math.Max(1, Math.Min(100, rank));
                        _districts.Save();
                    }

                    OpenGrade(player, district, grade);
                },
                () => OpenGrade(player, district, grade));
        }

        private void ConfirmDeleteGrade(Player player, District district, Grade grade)
        {
            if (!IsAdminOf(player, district)) { return; }

            var affected = district.members.Count(m => m.gradeId == grade.id);

            Ui.Confirm(player, "Supprimer " + grade.name,
                affected > 0
                    ? Ui.Bad(affected + " membre(s) seront reclasses au grade le plus bas.")
                    : Ui.Dim("Aucun membre ne porte ce grade."),
                Ui.Bad("Supprimer"), "Annuler",
                () =>
                {
                    district.grades.Remove(grade);

                    var fallback = district.LowestGrade();
                    var fallbackId = fallback != null ? fallback.id : 1;
                    foreach (var member in district.members.Where(m => m.gradeId == grade.id))
                    {
                        member.gradeId = fallbackId;
                    }

                    if (district.defaultGradeId == grade.id) { district.defaultGradeId = fallbackId; }

                    _districts.Save();
                    _districts.SyncSharedProperties(district);

                    Utils.Send(player, _plugin.Prefix + Ui.Ok("✓ Grade supprime."));
                    Open(player, district);
                },
                () => OpenGrade(player, district, grade));
        }

        // ------------------------------------------------------------------ affectation

        private void OpenMemberList(Player player, District district)
        {
            var entries = district.members
                .OrderByDescending(m => { var g = district.FindGrade(m.gradeId); return g != null ? g.rank : 0; })
                .Take(30)
                .Select(m =>
                {
                    var captured = m;
                    var grade = district.FindGrade(captured.gradeId);
                    var online = Utils.FindOnlineBySteamId(captured.steamId) != null;

                    return new Ui.MenuEntry(
                        (online ? Ui.Ok("● ") : Ui.Dim("○ ")) + captured.name,
                        IconMember,
                        grade != null ? grade.name : "?",
                        () => OpenAssign(player, district, captured));
                })
                .ToList();

            Ui.Menu(player, "Membres", Ui.Dim("Choisissez un membre pour changer son grade."),
                entries, "Retour", () => Open(player, district));
        }

        private void OpenAssign(Player player, District district, Member member)
        {
            if (district.IsOwner(member.steamId))
            {
                Ui.Info(player, member.name,
                    Ui.Dim("C'est le proprietaire du district : il dispose de tous les droits."));
                return;
            }

            // On ne touche pas a un membre de rang superieur ou egal au sien.
            var memberGrade = district.FindGrade(member.gradeId);
            if (memberGrade != null && !CanActOn(player, district, memberGrade))
            {
                Utils.Send(player, _plugin.Prefix
                    + Ui.Bad("✕ " + member.name + " est d'un grade superieur ou egal au votre."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player),
                    "tentative de modification du grade de " + member.name + " (superieur)");
                return;
            }

            var myRank = RankOf(player, district);
            var admin = IsAdminOf(player, district);

            var entries = district.grades
                .OrderByDescending(g => g.rank)
                .Select(g =>
                {
                    var captured = g;
                    var current = member.gradeId == captured.id;
                    var allowed = CanActOn(player, district, captured);

                    return new Ui.MenuEntry(
                        (current ? Ui.Ok("● ") : allowed ? "" : Ui.Dim("🔒 ")) + captured.name,
                        IconGrade,
                        "rang " + captured.rank,
                        () => Assign(player, district, member, captured));
                })
                .ToList();

            var body = admin
                ? Ui.Dim("Attribuer un grade.")
                : Ui.Dim("Vous ne pouvez attribuer qu'un grade de rang inferieur a " + myRank + ".");

            Ui.Menu(player, member.name, body, entries, "Retour", () => OpenMemberList(player, district));
        }

        /// <summary>
        /// Applique un changement de grade apres re-verification cote serveur.
        ///
        /// Le controle est refait ici et pas seulement a l'affichage : un menu reste ouvert pendant
        /// que la situation change, et rien ne garantit qu'une entree grisee ne sera pas declenchee.
        /// </summary>
        private void Assign(Player player, District district, Member member, Grade grade)
        {
            if (!CanManage(player, district))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                return;
            }

            if (district.IsOwner(member.steamId)) { return; }

            var memberGrade = district.FindGrade(member.gradeId);
            if (memberGrade != null && !CanActOn(player, district, memberGrade)) { return; }

            if (!CanActOn(player, district, grade))
            {
                Utils.Send(player, _plugin.Prefix
                    + Ui.Bad("✕ Vous ne pouvez pas attribuer un grade superieur ou egal au votre."));

                _plugin.Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player),
                    "tentative d'attribution du grade « " + grade.name + " » (superieur au sien) dans " + district.name);
                return;
            }

            member.gradeId = grade.id;
            _districts.Save();
            _districts.SyncSharedProperties(district);

            Utils.Send(player, _plugin.Prefix + Ui.Ok("✓ " + member.name + " est desormais " + grade.name + "."));

            var target = Utils.FindOnlineBySteamId(member.steamId);
            if (target != null)
            {
                Utils.Send(target, _plugin.Prefix + Ui.Ok("✓ Vous etes desormais "
                    + grade.name + " du " + district.name + "."));
            }

            _plugin.Webhook.LogStaffAction(Utils.Name(player), Utils.SteamId(player),
                "a nomme " + member.name + " au grade « " + grade.name + " » dans " + district.name);

            OpenMemberList(player, district);
        }
    }
}
