using System.Collections.Generic;
using Newtonsoft.Json;
using PostApo.District;

namespace PostApo.Farm
{
    /// <summary>
    /// Une ligne de butin. La ressource principale d'un gisement tombe toujours ;
    /// les lignes rares (plans, composants introuvables autrement) sont tirees independamment.
    /// </summary>
    public sealed class FarmDrop
    {
        public string slug = "";
        public int itemId;
        public int minQty = 1;
        public int maxQty = 1;

        /// <summary>Probabilite en pourcentage (0-100). 100 = tombe a chaque fouille.</summary>
        public float chancePercent = 100f;

        /// <summary>Etiquette affichee au joueur (le nom de l'item du jeu est souvent trompeur pour un « plan »).</summary>
        public string label = "";

        [JsonIgnore] public int ResolvedId;

        public FarmDrop() { }

        public FarmDrop(int itemId, int minQty, int maxQty, float chancePercent, string label = "")
        {
            this.itemId = itemId;
            this.minQty = minQty;
            this.maxQty = maxQty;
            this.chancePercent = chancePercent;
            this.label = label;
        }
    }

    /// <summary>Nature d'un point : change uniquement la presentation et les presets de butin.</summary>
    public static class NodeKind
    {
        public const string Gisement = "gisement";
        public const string Epave = "epave";
        public const string Cache = "cache";
    }

    /// <summary>
    /// Point exploitable : gisement de ressource, epave a fouiller ou cache a piller.
    ///
    /// Modele volontairement contraignant :
    ///  - stock limite (<see cref="maxCharges"/>) qui se reconstitue avec le temps ;
    ///  - latence par joueur apres chaque passage ;
    ///  - outil eventuellement exige, specialite de district eventuellement requise ;
    ///  - table de butin (<see cref="drops"/>) permettant les trouvailles rares.
    /// </summary>
    public sealed class FarmNode
    {
        public int id;
        public string name = "Gisement";

        /// <summary>gisement / epave / cache — voir <see cref="NodeKind"/>.</summary>
        public string kind = NodeKind.Gisement;

        /// <summary>Ressource principale. Le slug prime sur l'id. Optionnelle si <see cref="drops"/> est rempli.</summary>
        public string resourceSlug = "";
        public int resourceItemId = 0;

        public int minYield = 1;
        public int maxYield = 3;

        /// <summary>
        /// Butin additionnel tire a chaque fouille. C'est ici que vivent les plans de vehicule
        /// et les composants rares : ils ne se fabriquent pas, ils se trouvent.
        /// </summary>
        public List<FarmDrop> drops = new List<FarmDrop>();

        /// <summary>Duree de base d'une recolte, avant multiplicateur de difficulte.</summary>
        public float harvestTimeSeconds = 12f;

        /// <summary>Latence par joueur apres une recolte, avant multiplicateur.</summary>
        public float playerCooldownSeconds = 90f;

        /// <summary>Stock maximum. 0 = illimite.</summary>
        public int maxCharges = 20;

        /// <summary>Secondes necessaires pour regenerer une unite de stock, avant multiplicateur.</summary>
        public float rechargeSeconds = 120f;

        /// <summary>Outil requis dans l'inventaire (non consomme). Vide = aucun.</summary>
        public string requiredToolSlug = "";
        public int requiredToolItemId = 0;

        /// <summary>Si renseigne, seuls les membres d'un district ayant cette specialite peuvent exploiter le point.</summary>
        public string requiredSpecialite = "";

        public Position position = new Position();

        // ------------------------------------------------------------------ etat persistant

        public int charges = 20;
        public long lastRegenUnix;

        // ------------------------------------------------------------------ resolu au chargement
        // (exclu du JSON : donnees de travail, pas de configuration)

        [JsonIgnore] public int ResolvedResourceId;
        [JsonIgnore] public int ResolvedToolId;
        [JsonIgnore] public bool Valid;
        [JsonIgnore] public string InvalidReason = "";

        /// <summary>Un point sans ressource principale mais avec du butin reste exploitable.</summary>
        [JsonIgnore] public bool HasMainResource { get { return ResolvedResourceId > 0; } }
    }

    /// <summary>Contenu de <c>farm_data.json</c>.</summary>
    public sealed class FarmData
    {
        public List<FarmNode> nodes = new List<FarmNode>();

        /// <summary>Latences par joueur : cle « nodeId|steamId » -> horodatage de fin.</summary>
        public Dictionary<string, long> cooldowns = new Dictionary<string, long>();
    }
}
