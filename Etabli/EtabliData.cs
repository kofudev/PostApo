using System.Collections.Generic;
using PostApo.District;

namespace PostApo.Etabli
{
    /// <summary>Point de craft staff : c'est la qu'un joueur peut fabriquer son propre etabli.</summary>
    public sealed class StaffCraftPoint
    {
        public int id;
        public string name = "Atelier communal";
        public Position position = new Position();
    }

    /// <summary>Etabli pose par un joueur, persistant entre les redemarrages.</summary>
    public sealed class PlacedEtabli
    {
        public string ownerSteamId = "";
        public string ownerName = "";
        public Position position = new Position();
        public long placedAt;

        /// <summary>Si true, les membres du district du proprietaire peuvent l'utiliser.</summary>
        public bool sharedWithDistrict = true;
    }

    /// <summary>Contenu de <c>etabli_data.json</c>.</summary>
    public sealed class EtabliData
    {
        public List<StaffCraftPoint> staffPoints = new List<StaffCraftPoint>();
        public List<PlacedEtabli> placed = new List<PlacedEtabli>();

        /// <summary>SteamID des joueurs ayant deja consomme leur unique pose.</summary>
        public List<string> playersWhoPlaced = new List<string>();
    }
}
