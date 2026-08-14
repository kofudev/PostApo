using System;
using System.Collections.Generic;
using System.Linq;

namespace PostApo.District
{
    public static class Perm
    {
        public const string GererDistrict = "gererDistrict";
        public const string GererGrades = "gererGrades";
        public const string GererPermissions = "gererPermissions";
        public const string InviterMembre = "inviterMembre";
        public const string ExpulserMembre = "expulserMembre";

        public const string TeleportBase = "teleportBase";
        public const string CraftDistrict = "craftDistrict";

        public const string AccesTerrain = "accesTerrain";
        public const string OuvrirPorte = "ouvrirPorte";
        public const string OuvrirCoffre = "ouvrirCoffre";
        public const string PoserItem = "poserItem";
        public const string PrendreItem = "prendreItem";

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

    public sealed class Grade
    {
        public int id = 1;
        public string name = "Recrue";

        public int rank = 10;

        public List<string> permissions = new List<string>();

        public Dictionary<string, Dictionary<string, bool>> terrains =
            new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, bool>> vehicules =
            new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string permission)
        {
            if (string.IsNullOrEmpty(permission) || permissions == null) { return false; }
            return permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
        }

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
