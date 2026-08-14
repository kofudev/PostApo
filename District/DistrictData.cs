using System.Collections.Generic;

namespace PostApo.District
{
    public sealed class DistrictData
    {
        public List<District> districts = DefaultDistricts();

        public static List<District> DefaultDistricts()
        {
            return new List<District>
            {
                new District
                {
                    id = 1,
                    name = "District 1 — Les Terres Grises",
                    description = "Les dernieres terres encore cultivables. On y mange a sa faim, "
                                  + "a condition de travailler la poussiere du matin au soir.",
                    specialites = new List<string> { "agriculture" },
                },
                new District
                {
                    id = 2,
                    name = "District 2 — Le Dispensaire",
                    description = "Le seul endroit ou l'on soigne encore. Medicaments, protheses et "
                                  + "chirurgie de fortune : leur monnaie, c'est la survie des autres.",
                    specialites = new List<string> { "medecine" },
                },
                new District
                {
                    id = 3,
                    name = "District 3 — La Casse",
                    description = "Specialise dans la construction automobile. Tout ce qui roule encore "
                                  + "a Amboise est sorti de leurs ateliers, ou leur a ete vole.",
                    specialites = new List<string> { "construction_vehicule" },
                },
                new District
                {
                    id = 4,
                    name = "District 4 — L'Arsenal",
                    description = "Armes, munitions, poudre. On n'y entre pas sans raison, "
                                  + "et on n'en sort jamais les mains vides ni impunement.",
                    specialites = new List<string> { "armes" },
                },
                new District
                {
                    id = 5,
                    name = "District 5 — La Fonderie",
                    description = "Le coeur industriel. Fonte, alliages, carburant : sans eux, "
                                  + "les quatre autres districts ne produisent plus rien.",
                    specialites = new List<string> { "industrie" },
                },
            };
        }
    }
}
