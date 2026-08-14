using System.Collections.Generic;
using Newtonsoft.Json;

namespace PostApo.Etabli
{
    /// <summary>Ingredient ou resultat d'une recette. Le slug prime sur l'id (config portable).</summary>
    public sealed class RecipeItem
    {
        public string slug = "";
        public int itemId = 0;
        public int qty = 1;

        /// <summary>Id resolu au chargement. 0 = item inconnu du serveur.</summary>
        [JsonIgnore] public int ResolvedId;

        public RecipeItem() { }

        /// <summary>Par id (IDs officiels du wiki Nova-Life).</summary>
        public RecipeItem(int itemId, int qty)
        {
            this.itemId = itemId;
            this.qty = qty;
        }

        /// <summary>Par slug, si le serveur expose des items personnalises.</summary>
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

        /// <summary>Duree de base en secondes, avant multiplicateur de difficulte.</summary>
        public float craftTime = 5f;

        /// <summary>
        /// Vide = recette d'etabli generique. Sinon, la recette n'apparait qu'aux points de craft
        /// d'un district possedant cette specialite.
        /// </summary>
        public string specialite = "";

        /// <summary>Outil requis dans l'inventaire (non consomme). Vide = aucun.</summary>
        public string toolSlug = "";
        public int toolItemId = 0;

        /// <summary>Probabilite d'echec propre a la recette. Negatif = valeur globale de difficulte.</summary>
        public float failureChance = -1f;

        /// <summary>Resolu au chargement.</summary>
        [JsonIgnore] public int ResolvedToolId;

        /// <summary>false si un item de la recette est introuvable sur ce serveur.</summary>
        [JsonIgnore] public bool Valid;

        /// <summary>Raison de l'invalidite, affichee au demarrage.</summary>
        [JsonIgnore] public string InvalidReason = "";
    }

    /// <summary>
    /// Contenu de <c>recipes.json</c>.
    ///
    /// Tous les identifiants sont les <b>IDs officiels Nova-Life: Amboise</b> (feuille d'IDs du wiki) :
    ///   29 Pierre, 30 Cuivre, 31 Diamant, 33 Buche, 82 Sable, 1419 Magnetite,
    ///   79 Cuivre raffine, 1722 Lingot de cuivre, 1425 Lingot de magnetite, 1724 Petit lingot d'or,
    ///   1081 Planche, 1219 Verre, 1088 Plastique, 1089 Caoutchouc,
    ///   1429 Plaque de metal, 1430 Poutre en metal, 1222 Structure metallique,
    ///   3 Bougie d'allumage, 5 Batterie, 1530 Pneu, 1213 Boite a outils, 1083 Machine d'assemblage auto,
    ///   36 Taser, 136 Bouteille d'eau, 1505 Tomate, 1720 Graine de tomate,
    ///   1984 Pomme de terre, 1993 Huile de friture, 1986 Frites.
    ///
    /// La chaine est volontairement longue (minerai brut, lingot, plaque, poutre, structure, machine)
    /// et <b>repartie entre districts</b> : seule la Fonderie produit plaques et poutres, seule la Casse
    /// assemble les pieces de vehicule. Aucun district ne peut aller au bout tout seul.
    /// </summary>
    public sealed class RecipeData
    {
        public List<Recipe> recipes = DefaultRecipes();

        public static List<Recipe> DefaultRecipes()
        {
            return new List<Recipe>
            {
                // ================================================ ETABLI (recettes generiques)
                // Accessibles a tout joueur ayant pose son etabli. Premiere transformation uniquement.
                new Recipe
                {
                    id = "lingot_cuivre",
                    name = "Lingot de cuivre",
                    description = "Cinq morceaux de cuivre fondus en un lingot transportable.",
                    inputs = new List<RecipeItem> { new RecipeItem(30, 5) },    // Cuivre
                    output = new RecipeItem(1722, 1),                           // Lingot de cuivre
                    craftTime = 30f,
                },
                new Recipe
                {
                    id = "cuivre_raffine",
                    name = "Cuivre raffine",
                    description = "Cuivre debarrasse de ses scories. Indispensable a l'electricite.",
                    inputs = new List<RecipeItem> { new RecipeItem(30, 3) },    // Cuivre
                    output = new RecipeItem(79, 1),                             // Cuivre raffine
                    craftTime = 20f,
                },
                new Recipe
                {
                    id = "planche",
                    name = "Planches",
                    description = "Une buche debitee a la scie.",
                    inputs = new List<RecipeItem> { new RecipeItem(33, 2) },    // Buche
                    output = new RecipeItem(1081, 3),                           // Planche
                    craftTime = 15f,
                },
                new Recipe
                {
                    id = "verre",
                    name = "Verre",
                    description = "Du sable, beaucoup de chaleur, un peu de chance.",
                    inputs = new List<RecipeItem> { new RecipeItem(82, 4) },    // Sable
                    output = new RecipeItem(1219, 1),                           // Verre
                    craftTime = 25f,
                },

                // ================================================ INDUSTRIE (District 5, La Fonderie)
                // Tout le metal du serveur passe par la Fonderie.
                new Recipe
                {
                    id = "lingot_magnetite",
                    name = "Lingot de magnetite",
                    description = "Le haut-fourneau tourne jour et nuit.",
                    inputs = new List<RecipeItem> { new RecipeItem(1419, 5) },  // Magnetite
                    output = new RecipeItem(1425, 1),                           // Lingot de magnetite
                    craftTime = 45f,
                    specialite = "industrie",
                },
                new Recipe
                {
                    id = "plaque_metal",
                    name = "Plaque de metal",
                    description = "Lingots lamines. La matiere premiere de tout le reste.",
                    inputs = new List<RecipeItem> { new RecipeItem(1425, 2) },  // Lingot de magnetite
                    output = new RecipeItem(1429, 1),                           // Plaque de metal
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
                        new RecipeItem(1425, 3),                                // Lingot de magnetite
                        new RecipeItem(1429, 1),                                // Plaque de metal
                    },
                    output = new RecipeItem(1430, 1),                           // Poutre en metal
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
                        new RecipeItem(1430, 4),                                // Poutre en metal
                        new RecipeItem(1429, 6),                                // Plaque de metal
                    },
                    output = new RecipeItem(1222, 1),                           // Structure metallique
                    craftTime = 150f,
                    specialite = "industrie",
                    toolItemId = 1213,                                          // Boite a outils
                    failureChance = 0.15f,
                },

                // ================================================ CONSTRUCTION VEHICULE (District 3, La Casse)
                new Recipe
                {
                    id = "pneu",
                    name = "Pneu",
                    description = "Gomme refondue sur une jante recuperee.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1089, 6),                                // Caoutchouc
                        new RecipeItem(1429, 1),                                // Plaque de metal
                    },
                    output = new RecipeItem(1530, 1),                           // Pneu
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
                        new RecipeItem(79, 2),                                  // Cuivre raffine
                        new RecipeItem(1429, 1),                                // Plaque de metal
                    },
                    output = new RecipeItem(3, 1),                              // Bougie d'allumage
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
                        new RecipeItem(1722, 4),                                // Lingot de cuivre
                        new RecipeItem(1088, 3),                                // Plastique
                        new RecipeItem(1724, 1),                                // Petit lingot d'or
                    },
                    output = new RecipeItem(5, 1),                              // Batterie
                    craftTime = 120f,
                    specialite = "construction_vehicule",
                    toolItemId = 1213,                                          // Boite a outils
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
                        new RecipeItem(1222, 1),                                // Structure metallique
                        new RecipeItem(5, 1),                                   // Batterie
                        new RecipeItem(1530, 4),                                // Pneu
                        new RecipeItem(3, 2),                                   // Bougie d'allumage
                        new RecipeItem(1429, 6),                                // Plaque de metal
                    },
                    output = new RecipeItem(1083, 1),                           // Machine d'assemblage auto
                    craftTime = 360f,
                    specialite = "construction_vehicule",
                    toolItemId = 1213,                                          // Boite a outils
                    failureChance = 0.25f,
                },

                // ================================================ ARMES (District 4, L'Arsenal)
                new Recipe
                {
                    id = "taser",
                    name = "Taser",
                    description = "Deux electrodes, une batterie, beaucoup de regrets.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 3),                                // Plaque de metal
                        new RecipeItem(79, 2),                                  // Cuivre raffine
                        new RecipeItem(5, 1),                                   // Batterie
                    },
                    output = new RecipeItem(36, 1),                             // Taser
                    craftTime = 120f,
                    specialite = "armes",
                    toolItemId = 1213,                                          // Boite a outils
                    failureChance = 0.2f,
                },

                // ================================================ MEDECINE (District 2, Le Dispensaire)
                new Recipe
                {
                    id = "eau_potable",
                    name = "Eau potable",
                    description = "Filtree, bouillie, mise en bouteille. Elle vaut cher.",
                    inputs = new List<RecipeItem> { new RecipeItem(1219, 2) },  // Verre
                    output = new RecipeItem(136, 3),                            // Bouteille d'eau
                    craftTime = 35f,
                    specialite = "medecine",
                },

                // ================================================ AGRICULTURE (District 1, Les Terres Grises)
                new Recipe
                {
                    id = "graines_tomate",
                    name = "Graines de tomate",
                    description = "Selectionner, secher, conserver. C'est ainsi qu'on tient l'hiver.",
                    inputs = new List<RecipeItem> { new RecipeItem(1505, 1) },  // Tomate
                    output = new RecipeItem(1720, 2),                           // Graine de tomate
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
                        new RecipeItem(1984, 3),                                // Pomme de terre
                        new RecipeItem(1993, 1),                                // Huile de friture
                    },
                    output = new RecipeItem(1986, 4),                           // Frites
                    craftTime = 40f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "pomme_de_terre_epluchee",
                    name = "Pommes de terre epluchees",
                    description = "Long, ingrat, indispensable.",
                    inputs = new List<RecipeItem> { new RecipeItem(1984, 3) },  // Pomme de terre
                    output = new RecipeItem(1985, 3),                           // Pomme de terre epluchee
                    craftTime = 25f,
                    specialite = "agriculture",
                    toolItemId = 152,                                           // Couteau
                },
                new Recipe
                {
                    id = "graine_fraisier",
                    name = "Graines de fraisier",
                    description = "Ce qui reste d'un fruit quand on pense a demain.",
                    inputs = new List<RecipeItem> { new RecipeItem(1439, 2) },  // Fraise
                    output = new RecipeItem(1440, 3),                           // Graine de fraisier
                    craftTime = 20f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "beurre",
                    name = "Beurre",
                    description = "Baratter jusqu'a ce que le bras lache.",
                    inputs = new List<RecipeItem> { new RecipeItem(1451, 4) },  // Lait
                    output = new RecipeItem(1566, 1),                           // Beurre
                    craftTime = 45f,
                    specialite = "agriculture",
                },
                new Recipe
                {
                    id = "fromage",
                    name = "Fromage",
                    description = "Caille, presse, affine. Une monnaie d'echange a lui seul.",
                    inputs = new List<RecipeItem> { new RecipeItem(1451, 3) },  // Lait
                    output = new RecipeItem(137, 1),                            // Fromage
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
                        new RecipeItem(1447, 2),                                // Baguette
                        new RecipeItem(33, 1),                                  // Buche (le feu)
                    },
                    output = new RecipeItem(1448, 2),                           // Baguette cuite
                    craftTime = 30f,
                    specialite = "agriculture",
                },

                // ================================================ MEDECINE (suite)
                new Recipe
                {
                    id = "masque_homme",
                    name = "Masque chirurgical (H)",
                    description = "Decoupe dans du plastique fondu. Protege un peu, rassure beaucoup.",
                    inputs = new List<RecipeItem> { new RecipeItem(1088, 3) },  // Plastique
                    output = new RecipeItem(125, 1),                            // Masque Chirurgical H
                    craftTime = 30f,
                    specialite = "medecine",
                },
                new Recipe
                {
                    id = "masque_femme",
                    name = "Masque chirurgical (F)",
                    description = "Decoupe dans du plastique fondu. Protege un peu, rassure beaucoup.",
                    inputs = new List<RecipeItem> { new RecipeItem(1088, 3) },  // Plastique
                    output = new RecipeItem(126, 1),                            // Masque Chirurgical F
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
                        new RecipeItem(1454, 1),                                // Chocolat
                        new RecipeItem(1451, 2),                                // Lait
                    },
                    output = new RecipeItem(6030, 2),                           // Chocolat chaud
                    craftTime = 25f,
                    specialite = "medecine",
                },

                // ================================================ INDUSTRIE (suite)
                new Recipe
                {
                    id = "beton",
                    name = "Beton",
                    description = "Calcaire, sable, eau. La base de toute reconstruction.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(80, 4),                                  // Calcaire
                        new RecipeItem(82, 4),                                  // Sable
                        new RecipeItem(136, 1),                                 // Bouteille d'eau
                    },
                    output = new RecipeItem(83, 3),                             // Beton
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
                        new RecipeItem(1429, 4),                                // Plaque de metal
                        new RecipeItem(1081, 2),                                // Planche
                        new RecipeItem(1722, 1),                                // Lingot de cuivre
                    },
                    output = new RecipeItem(1213, 1),                           // Boite a outils
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
                        new RecipeItem(1088, 6),                                // Plastique
                        new RecipeItem(1089, 2),                                // Caoutchouc
                        new RecipeItem(83, 2),                                  // Beton (filtration)
                    },
                    output = new RecipeItem(1564, 1),                           // Bidon d'essence plein
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
                        new RecipeItem(5, 1),                                   // Batterie
                        new RecipeItem(79, 4),                                  // Cuivre raffine
                        new RecipeItem(1088, 2),                                // Plastique
                    },
                    output = new RecipeItem(1590, 1),                           // Batterie portable
                    craftTime = 150f,
                    specialite = "industrie",
                },

                // ================================================ CONSTRUCTION VEHICULE (suite)
                new Recipe
                {
                    id = "courroie_distribution",
                    name = "Courroie de distribution",
                    description = "Elle casse toujours au pire moment. Mieux vaut en avoir d'avance.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1089, 4),                                // Caoutchouc
                        new RecipeItem(1088, 2),                                // Plastique
                    },
                    output = new RecipeItem(4, 1),                              // Courroie de distribution
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
                        new RecipeItem(1088, 3),                                // Plastique
                        new RecipeItem(79, 2),                                  // Cuivre raffine
                        new RecipeItem(1219, 1),                                // Verre
                    },
                    output = new RecipeItem(1982, 1),                           // Lampe torche
                    craftTime = 45f,
                    specialite = "construction_vehicule",
                },

                // ================================================ ARMES (suite)
                new Recipe
                {
                    id = "couteau",
                    name = "Couteau",
                    description = "Une lame, un manche. Le premier argument de l'Arsenal.",
                    inputs = new List<RecipeItem>
                    {
                        new RecipeItem(1429, 2),                                // Plaque de metal
                        new RecipeItem(1081, 1),                                // Planche
                    },
                    output = new RecipeItem(152, 1),                            // Couteau
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
                        new RecipeItem(1081, 3),                                // Planche
                        new RecipeItem(1089, 2),                                // Caoutchouc
                    },
                    output = new RecipeItem(1981, 1),                           // Tonfa
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
                        new RecipeItem(1429, 2),                                // Plaque de metal
                        new RecipeItem(1722, 1),                                // Lingot de cuivre
                    },
                    output = new RecipeItem(1975, 1),                           // Menottes
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
                        new RecipeItem(30, 8),                                  // Cuivre
                        new RecipeItem(1088, 1),                                // Plastique
                    },
                    output = new RecipeItem(7, 12),                             // .357 SIG
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
                        new RecipeItem(1722, 2),                                // Lingot de cuivre
                        new RecipeItem(1429, 1),                                // Plaque de metal
                    },
                    output = new RecipeItem(1623, 15),                          // 5.56mm
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
                        new RecipeItem(1429, 8),                                // Plaque de metal
                        new RecipeItem(1430, 2),                                // Poutre en metal
                        new RecipeItem(79, 6),                                  // Cuivre raffine
                        new RecipeItem(1213, 1),                                // Boite a outils
                    },
                    output = new RecipeItem(6, 1),                              // SP 2022
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
                        new RecipeItem(1222, 2),                                // Structure metallique
                        new RecipeItem(1429, 12),                               // Plaque de metal
                        new RecipeItem(1724, 3),                                // Petit lingot d'or
                        new RecipeItem(1213, 1),                                // Boite a outils
                    },
                    output = new RecipeItem(1622, 1),                           // Famas
                    craftTime = 600f,
                    specialite = "armes",
                    failureChance = 0.30f,
                },
            };
        }
    }
}
