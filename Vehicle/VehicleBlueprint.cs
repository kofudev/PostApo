using System.Collections.Generic;
using Newtonsoft.Json;
using PostApo.Etabli;

namespace PostApo.Vehicle
{
    /// <summary>
    /// Identifiants d'items utilises par la filiere vehicule.
    /// Tous proviennent de la feuille d'IDs officielle Nova-Life: Amboise.
    /// </summary>
    public static class Mat
    {
        // Bruts
        public const int Pierre = 29;
        public const int Cuivre = 30;
        public const int Diamant = 31;
        public const int Buche = 33;
        public const int Sable = 82;
        public const int Magnetite = 1419;

        // Transformes
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

        // Pieces
        public const int Bougie = 3;
        public const int Batterie = 5;
        public const int Pneu = 1530;
        public const int BoiteAOutils = 1213;

        // Rares — introuvables au craft, uniquement en exploration
        public const int Calculateur = 95;      // Ordinateur portable
        public const int FaisceauElec = 1336;   // Radio emetteur
        public const int OutilPrecision = 1373; // Machine a sertir
        public const int CelluleHD = 1590;      // Batterie portable

        // Plans (un par palier)
        public const int PlanT1 = 1202;  // Feuille de papier
        public const int PlanT2 = 1302;  // Pile de documents
        public const int PlanT3 = 1181;  // Livre
        public const int PlanT4 = 1321;  // Pile de magazine
        public const int PlanT5 = 41;    // Carte Kisa
    }

    /// <summary>Une etape de chantier : des materiaux a livrer, puis du temps de travail.</summary>
    public sealed class VehicleStage
    {
        public string name = "";
        public string description = "";
        public List<RecipeItem> inputs = new List<RecipeItem>();

        /// <summary>Duree des travaux une fois tous les materiaux livres (avant multiplicateur).</summary>
        public float workSeconds = 60f;

        /// <summary>Probabilite d'echec de l'etape. Negatif = valeur globale. Un echec fait perdre l'etape, pas le chantier.</summary>
        public float failureChance = -1f;
    }

    /// <summary>
    /// Definition complete d'un vehicule constructible : le modele du jeu, son palier,
    /// le plan a trouver pour le debloquer, et la suite d'etapes de reconstruction.
    /// </summary>
    public sealed class VehicleBlueprint
    {
        /// <summary>ID de modele Nova-Life (feuille d'IDs officielle).</summary>
        public int modelId;

        public string name = "";

        /// <summary>1 = epave roulante ... 5 = legendaire. Determine le deblocage et la rarete du plan.</summary>
        public int tier = 1;

        /// <summary>Nom RP du plan, affiche a la place du nom d'item du jeu.</summary>
        public string planLabel = "";

        /// <summary>Item consomme au demarrage du chantier.</summary>
        public int planItemId;

        public List<VehicleStage> stages = new List<VehicleStage>();

        [JsonIgnore] public bool Valid;
        [JsonIgnore] public string InvalidReason = "";
    }

    /// <summary>Atelier de reconstruction : le point ou vivent les chantiers d'un district.</summary>
    public sealed class VehicleWorkshop
    {
        public int id;
        public int districtId;
        public string name = "Atelier de reconstruction";
        public PostApo.District.Position position = new PostApo.District.Position();
    }

    /// <summary>
    /// Contenu de <c>vehicles.json</c> : les plans de construction et les ateliers.
    ///
    /// Cinq paliers, deverrouilles l'un apres l'autre par district : on ne construit pas une
    /// sportive avant d'avoir remonte une citadine. Chaque palier ajoute une etape, des materiaux
    /// plus rares, et un plan plus difficile a trouver.
    /// </summary>
    public sealed class VehicleData
    {
        public List<VehicleBlueprint> blueprints = DefaultBlueprints();
        public List<VehicleWorkshop> workshops = new List<VehicleWorkshop>();

        // ------------------------------------------------------------------ generation par defaut

        private static RecipeItem I(int itemId, int qty) { return new RecipeItem(itemId, qty); }

        /// <summary>
        /// Les etapes sont construites par palier : chaque palier reprend les etapes du precedent
        /// avec des quantites superieures, et en ajoute une nouvelle. Ecrire les 22 vehicules a la
        /// main serait illisible et source d'incoherences.
        /// </summary>
        private static List<VehicleStage> StagesForTier(int tier)
        {
            var s = tier;               // facteur d'echelle
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

        /// <summary>Modeles reels du jeu, repartis en cinq paliers de progression.</summary>
        public static List<VehicleBlueprint> DefaultBlueprints()
        {
            return new List<VehicleBlueprint>
            {
                // ---- Palier 1 : ce qui roule encore, a peu pres
                Make(44, "206", 1),
                Make(53, "Kart", 1),
                Make(16, "Renaud Express", 1),

                // ---- Palier 2 : utilitaires et familiales
                Make(8,  "Berlingo civil", 2),
                Make(13, "Megane IV civil", 2),
                Make(1,  "Renaud Master", 2),
                Make(15, "C4 Grand Picasso", 2),

                // ---- Palier 3 : routieres et tout-terrain
                Make(41, "5008 civil", 3),
                Make(24, "Range River", 3),
                Make(56, "Korn Ranger 2021", 3),
                Make(0,  "Premier", 3),
                Make(12, "Depanneuse", 3),

                // ---- Palier 4 : sportives
                Make(14, "RX7", 4),
                Make(35, "Dodge Charger 1970", 4),
                Make(54, "Leaf Golster 1981", 4),
                Make(28, "Stellar coupe", 4),
                Make(10, "Olympia S7", 4),

                // ---- Palier 5 : legendes de l'ancien monde
                Make(55, "Stellar 911 RS", 5),
                Make(40, "V Model S", 5),
                Make(22, "Delorean CMD-12", 5),
            };
        }
    }
}
