using System.Collections.Generic;

namespace PostApo.Arrival
{
    public sealed class ArrivalPoint
    {
        public int id;
        public string name = "";
        public float x;
        public float y;
        public float z;
    }

    public sealed class ArrivalData
    {
        public List<ArrivalPoint> arrivalPoints = new List<ArrivalPoint>();
        public List<string> playersInitialized = new List<string>();
    }
}
