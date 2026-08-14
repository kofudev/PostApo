using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using PostApo.District;

namespace PostApo.Vehicle
{
    public sealed class VehicleProject
    {
        public int id;
        public int districtId;
        public int workshopId;

        public int modelId;
        public string modelName = "";
        public int tier = 1;

        public string ownerSteamId = "";
        public string ownerName = "";

        public int stageIndex;

        public Dictionary<string, int> delivered = new Dictionary<string, int>();

        public Dictionary<string, int> contributions = new Dictionary<string, int>();

        public Dictionary<string, string> contributorNames = new Dictionary<string, string>();

        public long startedAt;
        public long lastActivityAt;

        public Position position = new Position();

        [JsonIgnore] public bool Working;

        public int DeliveredOf(int itemId)
        {
            int value;
            return delivered.TryGetValue(itemId.ToString(), out value) ? value : 0;
        }

        public void AddDelivered(int itemId, int qty)
        {
            var key = itemId.ToString();
            int current;
            delivered[key] = (delivered.TryGetValue(key, out current) ? current : 0) + qty;
        }

        public void AddContribution(string steamId, string name, int qty)
        {
            if (string.IsNullOrEmpty(steamId)) { return; }

            int current;
            contributions[steamId] = (contributions.TryGetValue(steamId, out current) ? current : 0) + qty;
            contributorNames[steamId] = name ?? "?";
        }

        public void ResetStageDeliveries()
        {
            delivered.Clear();
        }

        public void Normalize()
        {
            if (delivered == null) { delivered = new Dictionary<string, int>(); }
            if (contributions == null) { contributions = new Dictionary<string, int>(); }
            if (contributorNames == null) { contributorNames = new Dictionary<string, string>(); }
            if (position == null) { position = new Position(); }
            if (stageIndex < 0) { stageIndex = 0; }
        }
    }

    public sealed class VehicleProjectData
    {
        public List<VehicleProject> projects = new List<VehicleProject>();
        public int nextId = 1;

        public Dictionary<string, int> unlockedTier = new Dictionary<string, int>();

        public Dictionary<string, int> completed = new Dictionary<string, int>();

        public int TierOf(int districtId)
        {
            int tier;
            return unlockedTier.TryGetValue(districtId.ToString(), out tier) ? Math.Max(1, tier) : 1;
        }

        public void SetTier(int districtId, int tier)
        {
            unlockedTier[districtId.ToString()] = Math.Max(1, tier);
        }

        public int CompletedOf(int districtId)
        {
            int count;
            return completed.TryGetValue(districtId.ToString(), out count) ? count : 0;
        }

        public void AddCompleted(int districtId)
        {
            var key = districtId.ToString();
            int current;
            completed[key] = (completed.TryGetValue(key, out current) ? current : 0) + 1;
        }
    }
}
