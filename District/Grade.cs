using System;
using System.Collections.Generic;
using System.Linq;

namespace PostApo.District
{
    /// <summary>
    /// Cles de permission reconnues par le plugin.
    ///
    /// Deux familles :
    ///  - les permissions <b>de district</b> (gestion, base, craft) sont appliquees par le plugin
    ///    lui-meme, sur les actions dont il a le controle ;
    ///  - les permissions <b>terrain / vehicule</b> pilotent la synchronisation des co-proprietaires
    ///    natifs (voir <c>DistrictSystem.SyncSharedProperties</c>). Nova-Life n'expose aucun hook
    ///    annulable sur l'ouverture d'une porte ou d'un coffre : la granularite fine est donc
    ///    conservee en configuration et appliquee la ou l'API le permet reellement.
    /// </summary>
    public static class Perm
    {
        // Gestion du district
        public const string GererDistrict = "gererDistrict";
        public const string GererGrades = "gererGrades";
        public const string GererPermissions = "gererPermissions";
        public const string InviterMembre = "inviterMembre";
        public const string ExpulserMembre = "expulserMembre";

        // Usage courant
        public const string TeleportBase = "teleportBase";
        public const string CraftDistrict = "craftDistrict";

        // Terrains
        public const string AccesTerrain = "accesTerrain";
        public const string OuvrirPorte = "ouvrirPorte";
        public const string OuvrirCoffre = "ouvrirCoffre";
        public const string PoserItem = "poserItem";
        public const string PrendreItem = "prendreItem";

        // Vehicules
        public const string UtiliserVehicule = "utiliserVehicule";
        public const string OuvrirVehicule = "ouvrirVehicule";
        public const string DemarrerVehicule = "demarrerVehicule";

        public static readonly string[] DistrictPermissions =
        {
            GererDistrict, GererGrades, GererPermissions, InviterMembre, ExpulserMembre,
            TeleportBase, CraftDistrict,
        };

        public static readonly string[] TerrainPermissions =
        {
            AccesTerrain, OuvrirPorte, OuvrirCoffre, PoserItem, PrendreItem,
        };

        public static readonly string[] VehiclePermissions =
        {
            UtiliserVehicule, OuvrirVehicule, DemarrerVehicule,
        };
    }

    /// <summary>Grade interne a un district : un nom, un rang, et un jeu de permissions.</summary>
    public sealed class Grade
    {
        public int id = 1;
        public string name = "Recrue";

        /// <summary>Plus le rang est eleve, plus le grade est haut place. Sert aux controles hierarchiques.</summary>
        public int rank = 10;

        /// <summary>Permissions globales du district accordees par ce grade.</summary>
        public List<string> permissions = new List<string>();

        /// <summary>Permissions par terrain : terrainId -> (permission -> autorise).</summary>
        public Dictionary<string, Dictionary<string, bool>> terrains =
            new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Permissions par vehicule : vehiculeId -> (permission -> autorise).</summary>
        public Dictionary<string, Dictionary<string, bool>> vehicules =
            new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string permission)
        {
            if (string.IsNullOrEmpty(permission) || permissions == null) { return false; }
            return permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Permission scopee. <paramref name="scope"/> null = on interroge la permission globale.
        /// Une entree explicite sur le terrain/vehicule prime toujours sur la permission globale.
        /// </summary>
        public bool? HasScoped(Dictionary<string, Dictionary<string, bool>> table, string scope, string permission)
        {
            if (table == null || string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(permission)) { return null; }

            Dictionary<string, bool> perScope;
            if (!table.TryGetValue(scope, out perScope) || perScope == null) { return null; }

            bool value;
            return perScope.TryGetValue(permission, out value) ? (bool?)value : null;
        }

        /// <summary>
        /// Grades livres avec chaque nouveau district. Le proprietaire peut tout reecrire en jeu
        /// (voir <see cref="GradeMenu"/>) ; ce jeu par defaut sert de modele lisible :
        ///
        ///  - <b>Adjoint</b> : tout, y compris poser et retirer ;
        ///  - <b>Officier</b> : tout sauf poser et retirer des objets ;
        ///  - <b>Membre</b> : entrer, ouvrir les portes, utiliser les vehicules ;
        ///  - <b>Recrue</b> : entrer et travailler, rien d'autre.
        /// </summary>
        public static List<Grade> DefaultSet()
        {
            return new List<Grade>
            {
                new Grade
                {
                    id = 4, name = "Adjoint", rank = 90,
                    permissions = new List<string>
                    {
                        Perm.GererGrades, Perm.GererPermissions,
                        Perm.InviterMembre, Perm.ExpulserMembre, Perm.TeleportBase, Perm.CraftDistrict,
                        Perm.AccesTerrain, Perm.OuvrirPorte, Perm.OuvrirCoffre, Perm.PoserItem, Perm.PrendreItem,
                        Perm.UtiliserVehicule, Perm.OuvrirVehicule, Perm.DemarrerVehicule,
                    },
                },
                new Grade
                {
                    id = 3, name = "Officier", rank = 75,
                    permissions = new List<string>
                    {
                        // Tout, sauf poser et retirer : il gere les hommes, pas les stocks.
                        Perm.InviterMembre, Perm.ExpulserMembre, Perm.TeleportBase, Perm.CraftDistrict,
                        Perm.AccesTerrain, Perm.OuvrirPorte, Perm.OuvrirCoffre,
                        Perm.UtiliserVehicule, Perm.OuvrirVehicule, Perm.DemarrerVehicule,
                    },
                },
                new Grade
                {
                    id = 2, name = "Membre", rank = 50,
                    permissions = new List<string>
                    {
                        Perm.TeleportBase, Perm.CraftDistrict,
                        Perm.AccesTerrain, Perm.OuvrirPorte,
                        Perm.UtiliserVehicule, Perm.OuvrirVehicule,
                    },
                },
                new Grade
                {
                    id = 1, name = "Recrue", rank = 10,
                    permissions = new List<string>
                    {
                        Perm.TeleportBase, Perm.CraftDistrict, Perm.AccesTerrain,
                    },
                },
            };
        }
    }
}
