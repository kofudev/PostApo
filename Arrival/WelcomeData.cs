using System.Collections.Generic;

namespace PostApo.Arrival
{
    /// <summary>
    /// Contenu de <c>welcome_data.json</c>.
    ///
    /// Fichier separe de <c>arrival_data.json</c> a dessein : meme si un joueur devait rejouer
    /// l'introduction (fichier efface, migration), la recompense reste distribuee une seule fois.
    /// </summary>
    public sealed class WelcomeData
    {
        public List<string> playersWelcomeRewarded = new List<string>();
    }
}
