using System.Collections.Generic;
using Newtonsoft.Json;

namespace PostApo.Etabli
{
    public sealed class RecipeItem
    {
        public string slug = "";
        public int itemId = 0;
        public int qty = 1;

        [JsonIgnore] public int ResolvedId;

        public RecipeItem() { }

        public RecipeItem(int itemId, int qty)
        {
            this.itemId = itemId;
            this.qty = qty;
        }

        public RecipeItem(string slug, int qty)
        {
            this.slug = slug;
            this.qty = qty;
        }
    }

    public sealed class Recipe
    {
        public string id = "";
        public string name = "";
        public string description = "";

        public List<RecipeItem> inputs = new List<RecipeItem>();
        public RecipeItem output = new RecipeItem();

        public float craftTime = 5f;

        public string specialite = "";

        public string toolSlug = "";
        public int toolItemId = 0;

        public float failureChance = -1f;

        [JsonIgnore] public int ResolvedToolId;

        [JsonIgnore] public bool Valid;

        [JsonIgnore] public string InvalidReason = "";
    }

    public sealed class RecipeData
    {
        public List<Recipe> recipes = DefaultRecipes();

        public static List<Recipe> DefaultRecipes()
        {
            return new List<Recipe>
            {
                new Recipe
                {
                    id = "lingot_cuivre",
                    name = "Lingot de cuivre",
                    description = "Cinq morceaux de cuivre fondus en un lingot transportable.",
                    inputs = new List<RecipeItem> { new RecipeItem(30, 5) },
                    output = new RecipeItem(1722, 1),
                    craftTime = 30f,
                },
                new Recipe
                {
                    id = "cuivre_raffine",
                    name = "Cuivre raffine",
                    description = "Cuivre debarrasse de ses scories. Indispensable a l'electricite.",
                    inputs = new List<RecipeItem> { new RecipeItem(30, 3) },
                    output = new RecipeItem(79, 1),
                    craftTime = 20f,
                },
                new Recipe
                {
                    id = "planche",
                    name = "Planches",
                    description = "Une buche debitee a la scie.",
                    inputs = new List<RecipeItem> { new RecipeItem(33, 2) },
                    output = new RecipeItem(1081, 3),
                    craftTime = 15f,
                },
                new Recipe
                {
                    id = "verre",
                    name = "Verre",
                    description = "Du sable, beaucoup de chaleur, un peu de chance.",
                    inputs = new List<RecipeItem> { new RecipeItem(82, 4) },
                    output = new RecipeItem(1219, 1),
                    craftTime = 25f,
                },

                new Recipe
                {
                    id = "lingot_magnetite",
                    name = "Lingot de magnetite",
                    description = "Le haut-fourneau tourne jour et nuit.",
                    inputs = new List<RecipeItem> { new RecipeItem(1419, 5) },
                    output = new RecipeItem(1425, 1),
                    craftTime = 45f,
                    specialite = "industrie",
                },
                new Recipe
                {
                    id = "plaque_metal",
                    name = "Plaque de metal",
                    description = "Lingots lamines. La matiere premiere de tout le reste.",
                    inputs = new List<RecipeItem> { new RecipeItem(1425, 2) },
                    output = new RecipeItem(1429, 1),
                    craftTime = 60f,
                    specialite = "industrie",
                },
                new Recipe
                {
                    id = "poutre_metal",
                    name = "Poutre en metal",
                    description = "Profile lourd. Il faut etre deux pour la deplacer.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1425, 3),
                        new RecipeItem(1429, 1),
                    },
                    output = new RecipeItem(1430, 1),
                    craftTime = 80f,
                    specialite = "industrie",
                },
                new Recipe
                {
                    id = "structure_metallique",
                    name = "Structure metallique",
                    description = "Le chassis de base de toute machine lourde.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1430, 4),
                        new RecipeItem(1429, 6),
                    },
                    output = new RecipeItem(1222, 1),
                    craftTime = 150f,
                    specialite = "industrie",
                    toolItemId = 1213,
                    failureChance = 0.15f,
                },

                new Recipe
                {
                    id = "pneu",
                    name = "Pneu",
                    description = "Gomme refondue sur une jante recuperee.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1089, 6),
                        new RecipeItem(1429, 1),
                    },
                    output = new RecipeItem(1530, 1),
                    craftTime = 50f,
                    specialite = "construction_vehicule",
                },
                new Recipe
                {
                    id = "bougie_allumage",
                    name = "Bougie d'allumage",
                    description = "Sans elle, aucun moteur ne demarre.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(79, 2),
                        new RecipeItem(1429, 1),
                    },
                    output = new RecipeItem(3, 1),
                    craftTime = 45f,
                    specialite = "construction_vehicule",
                },
                new Recipe
                {
                    id = "batterie",
                    name = "Batterie",
                    description = "Cuivre, plastique et un contact en or. Le nerf de la guerre.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1722, 4),
                        new RecipeItem(1088, 3),
                        new RecipeItem(1724, 1),
                    },
                    output = new RecipeItem(5, 1),
                    craftTime = 120f,
                    specialite = "construction_vehicule",
                    toolItemId = 1213,
                    failureChance = 0.2f,
                },
                new Recipe
                {
                    id = "machine_assemblage_auto",
                    name = "Machine d'assemblage auto",
                    description = "L'aboutissement : de quoi remonter une voiture entiere. "
                                  + "Des semaines de travail pour toute une communaute.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1222, 1),
                        new RecipeItem(5, 1),
                        new RecipeItem(1530, 4),
                        new RecipeItem(3, 2),
                        new RecipeItem(1429, 6),
                    },
                    output = new RecipeItem(1083, 1),
                    craftTime = 360f,
                    specialite = "construction_vehicule",
                    toolItemId = 1213,
                    failureChance = 0.25f,
                },

                new Recipe
                {
                    id = "taser",
                    name = "Taser",
                    description = "Deux electrodes, une batterie, beaucoup de regrets.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 3),
                        new RecipeItem(79, 2),
                        new RecipeItem(5, 1),
                    },
                    output = new RecipeItem(36, 1),
                    craftTime = 120f,
                    specialite = "armes",
                    toolItemId = 1213,
                    failureChance = 0.2f,
                },

                new Recipe
                {
                    id = "eau_potable",
                    name = "Eau potable",
                    description = "Filtree, bouillie, mise en bouteille. Elle vaut cher.",
                    inputs = new List<RecipeItem> { new RecipeItem(1219, 2) },
                    output = new RecipeItem(136, 3),
                    craftTime = 35f,
                    specialite = "medecine",
                },

                new Recipe
                {
                    id = "graines_tomate",
                    name = "Graines de tomate",
                    description = "Selectionner, secher, conserver. C'est ainsi qu'on tient l'hiver.",
                    inputs = new List<RecipeItem> { new RecipeItem(1505, 1) },
                    output = new RecipeItem(1720, 2),
                    craftTime = 20f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "frites",
                    name = "Frites",
                    description = "Un vrai repas chaud. Rare.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1984, 3),
                        new RecipeItem(1993, 1),
                    },
                    output = new RecipeItem(1986, 4),
                    craftTime = 40f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "pomme_de_terre_epluchee",
                    name = "Pommes de terre epluchees",
                    description = "Long, ingrat, indispensable.",
                    inputs = new List<RecipeItem> { new RecipeItem(1984, 3) },
                    output = new RecipeItem(1985, 3),
                    craftTime = 25f,
                    specialite = "agriculture",
                    toolItemId = 152,
                },
                new Recipe
                {
                    id = "graine_fraisier",
                    name = "Graines de fraisier",
                    description = "Ce qui reste d'un fruit quand on pense a demain.",
                    inputs = new List<RecipeItem> { new RecipeItem(1439, 2) },
                    output = new RecipeItem(1440, 3),
                    craftTime = 20f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "beurre",
                    name = "Beurre",
                    description = "Baratter jusqu'a ce que le bras lache.",
                    inputs = new List<RecipeItem> { new RecipeItem(1451, 4) },
                    output = new RecipeItem(1566, 1),
                    craftTime = 45f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "fromage",
                    name = "Fromage",
                    description = "Caille, presse, affine. Une monnaie d'echange a lui seul.",
                    inputs = new List<RecipeItem> { new RecipeItem(1451, 3) },
                    output = new RecipeItem(137, 1),
                    craftTime = 60f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "baguette_cuite",
                    name = "Baguette cuite",
                    description = "Le four tourne encore. C'est deja un luxe.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1447, 2),
                        new RecipeItem(33, 1),
                    },
                    output = new RecipeItem(1448, 2),
                    craftTime = 30f,
                    specialite = "agriculture",
                },

                new Recipe
                {
                    id = "masque_homme",
                    name = "Masque chirurgical (H)",
                    description = "Decoupe dans du plastique fondu. Protege un peu, rassure beaucoup.",
                    inputs = new List<RecipeItem> { new RecipeItem(1088, 3) },
                    output = new RecipeItem(125, 1),
                    craftTime = 30f,
                    specialite = "medecine",
                },
                new Recipe
                {
                    id = "masque_femme",
                    name = "Masque chirurgical (F)",
                    description = "Decoupe dans du plastique fondu. Protege un peu, rassure beaucoup.",
                    inputs = new List<RecipeItem> { new RecipeItem(1088, 3) },
                    output = new RecipeItem(126, 1),
                    craftTime = 30f,
                    specialite = "medecine",
                },
                new Recipe
                {
                    id = "repas_chaud",
                    name = "Repas chaud",
                    description = "De quoi remettre un blesse sur pied. Ou presque.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1454, 1),
                        new RecipeItem(1451, 2),
                    },
                    output = new RecipeItem(6030, 2),
                    craftTime = 25f,
                    specialite = "medecine",
                },

                new Recipe
                {
                    id = "beton",
                    name = "Beton",
                    description = "Calcaire, sable, eau. La base de toute reconstruction.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(80, 4),
                        new RecipeItem(82, 4),
                        new RecipeItem(136, 1),
                    },
                    output = new RecipeItem(83, 3),
                    craftTime = 50f,
                    specialite = "industrie",
                },
                new Recipe
                {
                    id = "boite_a_outils",
                    name = "Boite a outils",
                    description = "Sans elle, pas de cache scellee ni de moteur. La Fonderie le sait.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 4),
                        new RecipeItem(1081, 2),
                        new RecipeItem(1722, 1),
                    },
                    output = new RecipeItem(1213, 1),
                    craftTime = 90f,
                    specialite = "industrie",
                },
                new Recipe
                {
                    id = "bidon_essence",
                    name = "Bidon d'essence",
                    description = "Le carburant ne se fabrique pas : il se raffine, lentement.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1088, 6),
                        new RecipeItem(1089, 2),
                        new RecipeItem(83, 2),
                    },
                    output = new RecipeItem(1564, 1),
                    craftTime = 120f,
                    specialite = "industrie",
                    failureChance = 0.15f,
                },
                new Recipe
                {
                    id = "batterie_portable",
                    name = "Batterie portable",
                    description = "Une cellule de secours. Les caches les plus scellees en reclament.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(5, 1),
                        new RecipeItem(79, 4),
                        new RecipeItem(1088, 2),
                    },
                    output = new RecipeItem(1590, 1),
                    craftTime = 150f,
                    specialite = "industrie",
                },

                new Recipe
                {
                    id = "courroie_distribution",
                    name = "Courroie de distribution",
                    description = "Elle casse toujours au pire moment. Mieux vaut en avoir d'avance.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1089, 4),
                        new RecipeItem(1088, 2),
                    },
                    output = new RecipeItem(4, 1),
                    craftTime = 60f,
                    specialite = "construction_vehicule",
                },
                new Recipe
                {
                    id = "lampe_torche",
                    name = "Lampe torche",
                    description = "Indispensable pour fouiller une epave au fond d'un hangar.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1088, 3),
                        new RecipeItem(79, 2),
                        new RecipeItem(1219, 1),
                    },
                    output = new RecipeItem(1982, 1),
                    craftTime = 45f,
                    specialite = "construction_vehicule",
                },

                new Recipe
                {
                    id = "couteau",
                    name = "Couteau",
                    description = "Une lame, un manche. Le premier argument de l'Arsenal.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 2),
                        new RecipeItem(1081, 1),
                    },
                    output = new RecipeItem(152, 1),
                    craftTime = 40f,
                    specialite = "armes",
                },
                new Recipe
                {
                    id = "tonfa",
                    name = "Tonfa",
                    description = "Pour convaincre sans tuer.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1081, 3),
                        new RecipeItem(1089, 2),
                    },
                    output = new RecipeItem(1981, 1),
                    craftTime = 50f,
                    specialite = "armes",
                },
                new Recipe
                {
                    id = "menottes",
                    name = "Menottes",
                    description = "Livrees avec leurs cles. Ne les perdez pas.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 2),
                        new RecipeItem(1722, 1),
                    },
                    output = new RecipeItem(1975, 1),
                    craftTime = 70f,
                    specialite = "armes",
                },
                new Recipe
                {
                    id = "munition_357",
                    name = "Munitions .357",
                    description = "Douilles rechargees a la main. Instables, mais elles partent.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(30, 8),
                        new RecipeItem(1088, 1),
                    },
                    output = new RecipeItem(7, 12),
                    craftTime = 60f,
                    specialite = "armes",
                    failureChance = 0.15f,
                },
                new Recipe
                {
                    id = "munition_556",
                    name = "Munitions 5.56mm",
                    description = "Calibre militaire. Peu savent encore en produire.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1722, 2),
                        new RecipeItem(1429, 1),
                    },
                    output = new RecipeItem(1623, 15),
                    craftTime = 90f,
                    specialite = "armes",
                    failureChance = 0.18f,
                },
                new Recipe
                {
                    id = "sp2022",
                    name = "SP 2022",
                    description = "Une arme de poing remontee piece par piece. L'Arsenal ne les vend pas a n'importe qui.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 8),
                        new RecipeItem(1430, 2),
                        new RecipeItem(79, 6),
                        new RecipeItem(1213, 1),
                    },
                    output = new RecipeItem(6, 1),
                    craftTime = 300f,
                    specialite = "armes",
                    failureChance = 0.25f,
                },
                new Recipe
                {
                    id = "famas",
                    name = "Famas",
                    description = "Le sommet de ce que l'Arsenal sait encore produire. Rien ne garantit qu'il tirera.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1222, 2),
                        new RecipeItem(1429, 12),
                        new RecipeItem(1724, 3),
                        new RecipeItem(1213, 1),
                    },
                    output = new RecipeItem(1622, 1),
                    craftTime = 600f,
                    specialite = "armes",
                    failureChance = 0.30f,
                },
            };
        }
    }
}
