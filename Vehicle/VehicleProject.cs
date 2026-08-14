using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using PostApo.District;

namespace PostApo.Vehicle
{
    /// <summary>
    /// Chantier de reconstruction en cours.
    ///
    /// Un chantier est <b>collectif et persistant</b> : les materiaux livres restent acquis, chaque
    /// contributeur est enregistre, et un redemarrage du serveur ne fait rien perdre. C'est ce qui
    /// permet a un district entier de travailler des jours sur une meme voiture.
    /// </summary>
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

        /// <summary>Index de l'etape courante dans <see cref="VehicleBlueprint.stages"/>.</summary>
        public int stageIndex;

        /// <summary>Materiaux deja livres pour l'etape courante : « itemId » -> quantite.</summary>
        public Dictionary<string, int> delivered = new Dictionary<string, int>();

        /// <summary>Total d'objets livres par joueur, tous stades confondus : steamId -> quantite.</summary>
        public Dictionary<string, int> contributions = new Dictionary<string, int>();

        /// <summary>Noms des contributeurs, pour l'affichage.</summary>
        public Dictionary<string, string> contributorNames = new Dictionary<string, string>();

        public long startedAt;
        public long lastActivityAt;

        public Position position = new Position();

        /// <summary>true pendant les travaux d'une etape : empeche toute action concurrente.</summary>
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

    /// <summary>Contenu de <c>vehicle_projects.json</c>.</summary>
    public sealed class VehicleProjectData
    {
        public List<VehicleProject> projects = new List<VehicleProject>();
        public int nextId = 1;

        /// <summary>Palier debloque par district : districtId -> palier maximum constructible.</summary>
        public Dictionary<string, int> unlockedTier = new Dictionary<string, int>();

        /// <summary>Vehicules termines par district, pour l'historique et l'affichage.</summary>
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
