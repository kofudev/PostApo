using System.Collections.Generic;
using Newtonsoft.Json;
using PostApo.District;

namespace PostApo.Farm
{
    public sealed class FarmDrop
    {
        public string slug = "";
        public int itemId;
        public int minQty = 1;
        public int maxQty = 1;

        public float chancePercent = 100f;

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

    public static class NodeKind
    {
        public const string Gisement = "gisement";
        public const string Epave = "epave";
        public const string Cache = "cache";
    }

    public sealed class FarmNode
    {
        public int id;
        public string name = "Gisement";

        public string kind = NodeKind.Gisement;

        public string resourceSlug = "";
        public int resourceItemId = 0;

        public int minYield = 1;
        public int maxYield = 3;

        public List<FarmDrop> drops = new List<FarmDrop>();

        public float harvestTimeSeconds = 12f;

        public float playerCooldownSeconds = 90f;

        public int maxCharges = 20;

        public float rechargeSeconds = 120f;

        public string requiredToolSlug = "";
        public int requiredToolItemId = 0;

        public string requiredSpecialite = "";

        public Position position = new Position();

        public int charges = 20;
        public long lastRegenUnix;

        [JsonIgnore] public int ResolvedResourceId;
        [JsonIgnore] public int ResolvedToolId;
        [JsonIgnore] public bool Valid;
        [JsonIgnore] public string InvalidReason = "";

        [JsonIgnore] public bool HasMainResource { get { return ResolvedResourceId > 0; } }
    }

    public sealed class FarmData
    {
        public List<FarmNode> nodes = new List<FarmNode>();

        public Dictionary<string, long> cooldowns = new Dictionary<string, long>();
    }
}
