using System.Collections.Generic;

namespace PostApo.Arrival
{
    /// <summary>Point d'apparition possible pour un nouveau joueur.</summary>
    public sealed class ArrivalPoint
    {
        public int id;
        public string name = "";
        public float x;
        public float y;
        public float z;
    }

    /// <summary>
    /// Contenu de <c>arrival_data.json</c>.
    ///
    /// <see cref="playersInitialized"/> est indexe par SteamID : le parcours d'introduction ne peut
    /// donc pas etre rejoue, ni apres un redemarrage du serveur, ni en recreant un personnage.
    /// </summary>
    public sealed class ArrivalData
    {
        public List<ArrivalPoint> arrivalPoints = new List<ArrivalPoint>();
        public List<string> playersInitialized = new List<string>();
    }
}
