using System.Collections.Generic;

namespace PostApo.Core
{
    public sealed class Config
    {
        public string webhookUrl = "";

        public int staffLevelMin = 3;

        public string chatPrefix = "<color=#C96F4A><b>[Amboise]</b></color> ";

        public ArrivalConfig arrival = new ArrivalConfig();
        public List<ItemStack> welcomeRewards = ItemStack.DefaultWelcome();
        public CrowbarConfig crowbar = new CrowbarConfig();
        public EtabliConfig etabli = new EtabliConfig();
        public FarmConfig farm = new FarmConfig();
        public DifficultyConfig difficulty = new DifficultyConfig();
    }

    public class ItemStack
    {
        public string slug = "";
        public int itemId = 0;
        public int quantity = 1;

        public static List<ItemStack> DefaultWelcome()
        {
            return new List<ItemStack>
            {
                new ItemStack { itemId = 30,   quantity = 5 },
                new ItemStack { itemId = 29,   quantity = 5 },
                new ItemStack { itemId = 1724, quantity = 2 },
            };
        }
    }

    public sealed class ArrivalConfig
    {
        public bool enabled = true;
        public bool introductionEnabled = true;
        public bool randomArrivalEnabled = true;

        public float delayBeforeIntroSeconds = 4f;

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

    public sealed class CrowbarConfig
    {
        public bool enabled = true;
        public string slug = "";

        public int itemId = 1580;

        public int quantity = 1;

        public float recheckIntervalSeconds = 60f;
    }

    public sealed class EtabliConfig
    {
        public bool enabled = true;

        public string etabliItemSlug = "";
        public int etabliItemId = 1755;

        public List<ItemStack> etabliCost = new List<ItemStack>
        {
            new ItemStack { itemId = 33, quantity = 20 },
            new ItemStack { itemId = 30, quantity = 6 },
            new ItemStack { itemId = 29, quantity = 40 },
        };

        public bool spawnPhysicalObject = true;

        public int physicalObjectItemId = 1318;

        public float physicalObjectForwardOffset = 1.2f;

        public float interactionRadius = 3f;

        public bool onePlacementPerPlayer = true;

        public float minDistanceBetweenEtablis = 15f;
    }

    public sealed class FarmConfig
    {
        public bool enabled = true;

        public float interactionRadius = 3f;

        public float maxDriftDuringActionMeters = 4f;
    }

    public sealed class DifficultyConfig
    {
        public float craftTimeMultiplier = 1.5f;

        public float craftFailureChance = 0.12f;

        public float craftCancelRefundRatio = 0.25f;

        public float farmYieldMultiplier = 1f;

        public float farmTimeMultiplier = 1.4f;

        public float farmCooldownMultiplier = 1.6f;

        public float farmInjuryChance = 0.10f;

        public int farmInjuryDamage = 6;

        public int farmInjuryMinHealth = 12;
    }
}
