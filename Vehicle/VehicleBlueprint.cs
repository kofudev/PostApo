using System.Collections.Generic;
using Newtonsoft.Json;
using PostApo.Etabli;

namespace PostApo.Vehicle
{
    public static class Mat
    {
        public const int Pierre = 29;
        public const int Cuivre = 30;
        public const int Diamant = 31;
        public const int Buche = 33;
        public const int Sable = 82;
        public const int Magnetite = 1419;

        public const int CuivreRaffine = 79;
        public const int LingotCuivre = 1722;
        public const int LingotMagnetite = 1425;
        public const int PetitLingotOr = 1724;
        public const int Planche = 1081;
        public const int Verre = 1219;
        public const int Plastique = 1088;
        public const int Caoutchouc = 1089;
        public const int PlaqueMetal = 1429;
        public const int PoutreMetal = 1430;
        public const int StructureMetallique = 1222;

        public const int Bougie = 3;
        public const int Batterie = 5;
        public const int Pneu = 1530;
        public const int BoiteAOutils = 1213;

        public const int Calculateur = 95;
        public const int FaisceauElec = 1336;
        public const int OutilPrecision = 1373;
        public const int CelluleHD = 1590;

        public const int PlanT1 = 1202;
        public const int PlanT2 = 1302;
        public const int PlanT3 = 1181;
        public const int PlanT4 = 1321;
        public const int PlanT5 = 41;
    }

    public sealed class VehicleStage
    {
        public string name = "";
        public string description = "";
        public List<RecipeItem> inputs = new List<RecipeItem>();

        public float workSeconds = 60f;

        public float failureChance = -1f;
    }

    public sealed class VehicleBlueprint
    {
        public int modelId;

        public string name = "";

        public int tier = 1;

        public string planLabel = "";

        public int planItemId;

        public List<VehicleStage> stages = new List<VehicleStage>();

        [JsonIgnore] public bool Valid;
        [JsonIgnore] public string InvalidReason = "";
    }

    public sealed class VehicleWorkshop
    {
        public int id;
        public int districtId;
        public string name = "Atelier de reconstruction";
        public PostApo.District.Position position = new PostApo.District.Position();
    }

    public sealed class VehicleData
    {
        public List<VehicleBlueprint> blueprints = DefaultBlueprints();
        public List<VehicleWorkshop> workshops = new List<VehicleWorkshop>();

        private static RecipeItem I(int itemId, int qty) { return new RecipeItem(itemId, qty); }

        private static List<VehicleStage> StagesForTier(int tier)
        {
            var s = tier;
            var stages = new List<VehicleStage>();

            stages.Add(new VehicleStage
            {
                name = "1. Chassis et structure",
                description = "Redresser le chassis, souder les longerons, remettre la coque d'aplomb.",
                inputs = new List<RecipeItem>
                {
                    I(Mat.PoutreMetal, 2 * s),
                    I(Mat.PlaqueMetal, 4 * s),
                    I(Mat.LingotMagnetite, 3 * s),
                },
                workSeconds = 120f * s,
            });

            stages.Add(new VehicleStage
            {
                name = "2. Train roulant",
                description = "Suspensions, freins, quatre pneus qui tiennent la route.",
                inputs = new List<RecipeItem>
                {
                    I(Mat.Pneu, 4),
                    I(Mat.PlaqueMetal, 2 * s),
                    I(Mat.Caoutchouc, 6 * s),
                },
                workSeconds = 90f * s,
            });

            stages.Add(new VehicleStage
            {
                name = "3. Motorisation",
                description = "Le bloc moteur : la piece qui decide si l'epave roulera un jour.",
                inputs = new List<RecipeItem>
                {
                    I(Mat.StructureMetallique, s),
                    I(Mat.Bougie, 2 * s),
                    I(Mat.LingotCuivre, 4 * s),
                    I(Mat.BoiteAOutils, 1),
                },
                workSeconds = 180f * s,
                failureChance = 0.10f + 0.03f * s,
            });

            if (tier >= 2)
            {
                stages.Add(new VehicleStage
                {
                    name = "4. Circuit electrique",
                    description = "Faisceau, batterie, allumage. Sans courant, rien ne demarre.",
                    inputs = new List<RecipeItem>
                    {
                        I(Mat.Batterie, 1),
                        I(Mat.CuivreRaffine, 6 * s),
                        I(Mat.Plastique, 4 * s),
                        I(Mat.FaisceauElec, 1),
                    },
                    workSeconds = 120f * s,
                });
            }

            if (tier >= 3)
            {
                stages.Add(new VehicleStage
                {
                    name = "5. Carrosserie et vitrage",
                    description = "Toles ajustees, vitres taillees, portes qui ferment.",
                    inputs = new List<RecipeItem>
                    {
                        I(Mat.PlaqueMetal, 6 * s),
                        I(Mat.Verre, 4 * s),
                        I(Mat.Planche, 4 * s),
                    },
                    workSeconds = 150f * s,
                });
            }

            if (tier >= 4)
            {
                stages.Add(new VehicleStage
                {
                    name = "6. Reglages et injection",
                    description = "Calculateur, cartographie, equilibrage. Le travail d'un specialiste.",
                    inputs = new List<RecipeItem>
                    {
                        I(Mat.Calculateur, 1),
                        I(Mat.OutilPrecision, 1),
                        I(Mat.PetitLingotOr, 2 * s),
                        I(Mat.CuivreRaffine, 8 * s),
                    },
                    workSeconds = 240f * s,
                    failureChance = 0.20f,
                });
            }

            if (tier >= 5)
            {
                stages.Add(new VehicleStage
                {
                    name = "7. Homologation",
                    description = "La derniere ligne droite. Rien ne garantit qu'elle demarre.",
                    inputs = new List<RecipeItem>
                    {
                        I(Mat.CelluleHD, 2),
                        I(Mat.Diamant, 6),
                        I(Mat.PetitLingotOr, 10),
                        I(Mat.StructureMetallique, 2),
                    },
                    workSeconds = 420f,
                    failureChance = 0.25f,
                });
            }

            return stages;
        }

        private static int PlanForTier(int tier)
        {
            switch (tier)
            {
                case 1: return Mat.PlanT1;
                case 2: return Mat.PlanT2;
                case 3: return Mat.PlanT3;
                case 4: return Mat.PlanT4;
                default: return Mat.PlanT5;
            }
        }

        private static string PlanLabelForTier(int tier)
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

        private static VehicleBlueprint Make(int modelId, string name, int tier)
        {
            return new VehicleBlueprint
            {
                modelId = modelId,
                name = name,
                tier = tier,
                planItemId = PlanForTier(tier),
                planLabel = PlanLabelForTier(tier),
                stages = StagesForTier(tier),
            };
        }

        public static List<VehicleBlueprint> DefaultBlueprints()
        {
            return new List<VehicleBlueprint>
            {
                Make(44, "206", 1),
                Make(53, "Kart", 1),
                Make(16, "Renaud Express", 1),

                Make(8,  "Berlingo civil", 2),
                Make(13, "Megane IV civil", 2),
                Make(1,  "Renaud Master", 2),
                Make(15, "C4 Grand Picasso", 2),

                Make(41, "5008 civil", 3),
                Make(24, "Range River", 3),
                Make(56, "Korn Ranger 2021", 3),
                Make(0,  "Premier", 3),
                Make(12, "Depanneuse", 3),

                Make(14, "RX7", 4),
                Make(35, "Dodge Charger 1970", 4),
                Make(54, "Leaf Golster 1981", 4),
                Make(28, "Stellar coupe", 4),
                Make(10, "Olympia S7", 4),

                Make(55, "Stellar 911 RS", 5),
                Make(40, "V Model S", 5),
                Make(22, "Delorean CMD-12", 5),
            };
        }
    }
}
