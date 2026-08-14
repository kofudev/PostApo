using System.Collections.Generic;

namespace PostApo.Core
{
    /// <summary>
    /// Contenu de <c>config.json</c>. Chaque champ possede une valeur par defaut jouable :
    /// le plugin demarre meme si le fichier est vide ou partiel.
    /// </summary>
    public sealed class Config
    {
        /// <summary>URL du webhook Discord. Vide = journalisation Discord desactivee.</summary>
        public string webhookUrl = "";

        /// <summary>Niveau admin minimum requis pour les commandes staff.</summary>
        public int staffLevelMin = 3;

        /// <summary>Prefixe des messages de chat du plugin.</summary>
        public string chatPrefix = "<color=#C96F4A><b>[Amboise]</b></color> ";

        public ArrivalConfig arrival = new ArrivalConfig();
        public List<ItemStack> welcomeRewards = ItemStack.DefaultWelcome();
        public CrowbarConfig crowbar = new CrowbarConfig();
        public EtabliConfig etabli = new EtabliConfig();
        public FarmConfig farm = new FarmConfig();
        public DifficultyConfig difficulty = new DifficultyConfig();
    }

    /// <summary>
    /// Reference a un item. Le slug prime sur l'id quand il est renseigne ; les valeurs par defaut
    /// utilisent les IDs officiels Nova-Life: Amboise, donc elles fonctionnent sans reglage.
    /// </summary>
    public class ItemStack
    {
        public string slug = "";
        public int itemId = 0;
        public int quantity = 1;

        /// <summary>5 Cuivre, 5 Pierre, 2 Or — comme demande au cahier des charges.</summary>
        public static List<ItemStack> DefaultWelcome()
        {
            return new List<ItemStack>
            {
                new ItemStack { itemId = 30,   quantity = 5 },  // Cuivre
                new ItemStack { itemId = 29,   quantity = 5 },  // Pierre
                new ItemStack { itemId = 1724, quantity = 2 },  // Petit lingot d'or
            };
        }
    }

    public sealed class ArrivalConfig
    {
        public bool enabled = true;
        public bool introductionEnabled = true;
        public bool randomArrivalEnabled = true;

        /// <summary>Delai avant de lancer le parcours, laisse le client finir de charger.</summary>
        public float delayBeforeIntroSeconds = 4f;

        /// <summary>Le joueur doit-il obligatoirement choisir un district pour continuer ?</summary>
        public bool districtChoiceMandatory = true;

        public string introTitle = "AMBOISE";

        public List<string> introductionText = new List<string>
        {
            "Bienvenue a Amboise.",
            "",
            "Vous arrivez dans un monde post-apocalyptique.",
            "",
            "Apres l'effondrement de l'ancien monde, Amboise s'est reconstruite autour de cinq districts.",
            "",
            "Ces cinq districts sont completement differents et possedent chacun leur propre organisation,",
            "leurs ressources, leurs specialites et leur fonctionnement.",
            "",
            "Ici, rien n'est donne. L'eau, le metal, le carburant et les munitions se meritent.",
            "Seuls ceux qui travaillent leur district survivent a l'hiver.",
            "",
            "Votre choix de district aura un impact sur votre aventure et votre progression.",
            "",
            "Bienvenue dans la nouvelle Amboise.",
        };

        public string districtChoiceTitle = "Quel district souhaitez-vous rejoindre ?";
        public string baseChoiceTitle = "Souhaitez-vous rejoindre immediatement la base de votre district ?";
        public string baseChoiceYes = "Oui, me teleporter";
        public string baseChoiceNo = "Non, rester ici";
    }

    /// <summary>Pied de biche permanent remis a chaque apparition du personnage.</summary>
    public sealed class CrowbarConfig
    {
        public bool enabled = true;
        public string slug = "";

        /// <summary>1580 = Pied de biche (ID officiel Nova-Life).</summary>
        public int itemId = 1580;

        public int quantity = 1;

        /// <summary>Verification periodique de l'inventaire (0 = uniquement au spawn).</summary>
        public float recheckIntervalSeconds = 60f;
    }

    public sealed class EtabliConfig
    {
        public bool enabled = true;

        /// <summary>Item representant l'etabli non pose. 1755 = Etabli de maison (ID officiel).</summary>
        public string etabliItemSlug = "";
        public int etabliItemId = 1755;

        /// <summary>
        /// Cout de fabrication d'un etabli au point de craft staff.
        /// Uniquement des matieres premieres : c'est le tout premier craft du joueur, il doit etre
        /// atteignable sans avoir deja un etabli (sinon la progression se mord la queue).
        /// </summary>
        public List<ItemStack> etabliCost = new List<ItemStack>
        {
            new ItemStack { itemId = 33, quantity = 20 },  // Buche
            new ItemStack { itemId = 30, quantity = 6 },   // Cuivre
            new ItemStack { itemId = 29, quantity = 40 },  // Pierre
        };

        /// <summary>
        /// Pose un vrai objet visible en meme temps que le point d'interaction, pour que
        /// l'etabli se voie sur la carte au lieu d'etre un simple marqueur au sol.
        /// </summary>
        public bool spawnPhysicalObject = true;

        /// <summary>1318 = « Etablit » (ID officiel Nova-Life).</summary>
        public int physicalObjectItemId = 1318;

        /// <summary>Distance devant le joueur ou l'objet est pose (0 = exactement sur le point).</summary>
        public float physicalObjectForwardOffset = 1.2f;

        /// <summary>Distance d'interaction avec un etabli pose ou un point de craft.</summary>
        public float interactionRadius = 3f;

        /// <summary>Un joueur ne peut poser qu'un seul etabli sur toute sa vie de personnage.</summary>
        public bool onePlacementPerPlayer = true;

        /// <summary>Distance minimale entre deux etablis poses (evite les grappes d'etablis).</summary>
        public float minDistanceBetweenEtablis = 15f;
    }

    public sealed class FarmConfig
    {
        public bool enabled = true;

        /// <summary>Distance d'interaction avec un gisement.</summary>
        public float interactionRadius = 3f;

        /// <summary>Le joueur doit rester dans ce rayon pendant toute la recolte.</summary>
        public float maxDriftDuringActionMeters = 4f;
    }

    /// <summary>
    /// Reglages de durete du serveur. Les valeurs par defaut sont volontairement severes :
    /// recoltes lentes, rendements faibles, crafts longs et faillibles.
    /// </summary>
    public sealed class DifficultyConfig
    {
        /// <summary>Multiplicateur applique a la duree de tous les crafts.</summary>
        public float craftTimeMultiplier = 1.5f;

        /// <summary>Probabilite (0-1) qu'un craft echoue et consomme quand meme les materiaux.</summary>
        public float craftFailureChance = 0.12f;

        /// <summary>Part des materiaux rendus si le joueur interrompt son craft (0 = tout est perdu).</summary>
        public float craftCancelRefundRatio = 0.25f;

        /// <summary>Multiplicateur applique au rendement de tous les gisements.</summary>
        public float farmYieldMultiplier = 1f;

        /// <summary>Multiplicateur applique aux temps de recolte.</summary>
        public float farmTimeMultiplier = 1.4f;

        /// <summary>Multiplicateur applique aux temps de repousse et aux cooldowns joueur.</summary>
        public float farmCooldownMultiplier = 1.6f;

        /// <summary>Probabilite (0-1) de se blesser en recoltant.</summary>
        public float farmInjuryChance = 0.10f;

        /// <summary>Degats infliges par une blessure de recolte.</summary>
        public int farmInjuryDamage = 6;

        /// <summary>Points de vie minimum sous lesquels une blessure de recolte ne descend jamais.</summary>
        public int farmInjuryMinHealth = 12;
    }
}
