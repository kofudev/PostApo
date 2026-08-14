using System.Collections.Generic;
using PostApo.District;

namespace PostApo.Etabli
{
    public sealed class StaffCraftPoint
    {
        public int id;
        public string name = "Atelier communal";
        public Position position = new Position();
    }

    public sealed class PlacedEtabli
    {
        public string ownerSteamId = "";
        public string ownerName = "";
        public Position position = new Position();
        public long placedAt;

        public bool sharedWithDistrict = true;
    }

    public sealed class EtabliData
    {
        public List<StaffCraftPoint> staffPoints = new List<StaffCraftPoint>();
        public List<PlacedEtabli> placed = new List<PlacedEtabli>();

        public List<string> playersWhoPlaced = new List<string>();
    }
}
