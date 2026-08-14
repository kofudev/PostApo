# PostApo

Plugin serveur pour **Nova-Life: Amboise**, pensé pour un RP post-apocalyptique.

Les joueurs arrivent dans une ville reconstruite autour de cinq districts, choisissent leur camp,
récoltent, fouillent des épaves et remontent des véhicules pièce par pièce.

Le plugin est standalone : une seule DLL à déposer, aucune dépendance externe, aucune base de
données. Tout est stocké en JSON.

## Installation

1. Compiler le projet (voir plus bas) ou récupérer `PostApo.dll`
2. Copier la DLL dans le dossier du serveur :

```
Plugins/
└── PostApo/
    └── PostApo.dll
```

3. Redémarrer le serveur

Les fichiers de données sont créés automatiquement au premier lancement dans `Plugins/PostApo/` :
`config.json`, `districts.json`, `recipes.json`, `vehicles.json`, `farm_data.json`,
`etabli_data.json`, `arrival_data.json`, `welcome_data.json`, `vehicle_projects.json`.

## Configuration

Tout se règle dans `config.json`. Les IDs d'items sont déjà renseignés pour Amboise, il n'y a rien
à chercher.

| Clé | Rôle |
|---|---|
| `webhookUrl` | webhook Discord (vide = désactivé) |
| `staffLevelMin` | niveau admin requis pour les commandes staff (3 par défaut) |
| `welcomeRewards` | ce que reçoit un nouveau joueur |
| `crowbar` | pied de biche rendu automatiquement |
| `etabli` | coût de l'établi, rayon d'interaction, objet posé |
| `difficulty` | durées de craft, taux d'échec, rendement et cooldowns de farm |

Après modification : `/postapo reload` en jeu, pas besoin de redémarrer.

## Lancer le projet

```bash
dotnet build -c Release
```

La DLL sort dans `bin/Release/PostApo.dll`.

Les références pointent vers `../libs/` en `Private=false` : seul `PostApo.dll` est à déployer.

## Fonctionnalités

- **Arrivée** — téléportation aléatoire, présentation du monde, choix du district, récompense de
  bienvenue (une seule fois par SteamID)
- **Districts** — 5 districts avec propriétaire, membres, grades et permissions. Les terrains et
  véhicules du propriétaire sont partagés automatiquement avec les membres autorisés
- **Grades** — le propriétaire crée ses grades et coche leurs droits en jeu. Personne ne peut
  toucher un grade supérieur ou égal au sien
- **Établi** — chaque joueur fabrique puis pose son établi personnel, une seule fois
- **Craft** — 37 recettes réparties entre l'établi et les cinq spécialités de district
- **Farm et exploration** — gisements, épaves et caches avec stock limité, cooldowns et butin rare
- **Véhicules** — 20 modèles sur 5 paliers. Il faut trouver un plan en fouillant, puis mener un
  chantier collectif en plusieurs étapes. Le véhicule final est un vrai véhicule immatriculé
- **Webhook Discord** — journal des événements importants, sans jamais bloquer le gameplay

## Utilisation

Joueurs :

```
/district      son district, membres, base, gestion des grades
/etabli        menu de l'établi (/etabli pose pour l'installer)
/atelier       chantiers véhicule en cours
/plans         plans possédés et ce qu'ils débloquent
/epaves        épaves et caches les plus proches
/gisements     gisements les plus proches
```

Staff :

```
/postapo status | reload | finditem <texte>
/spawn_arrivee set | list | remove <id>
/district_base set <id>
/district_craft set <districtId> <specialite>
/etabli_point set
/farm_point set <itemId> [nom] | epave <palier 1-5>
/atelier set <districtId>
```

La procédure d'installation complète est détaillée dans [SETUP.md](SETUP.md).

## Dépendances

Toutes fournies par le serveur, rien à installer :

- `Assembly-CSharp.dll` — API Nova-Life
- `Mirror.dll` — réseau
- `Newtonsoft.Json.dll` — persistance
- `UnityEngine.CoreModule.dll`

Cible : .NET Framework 4.7.2.

## Licence

Aucune licence n'est déclarée. Projet privé, tous droits réservés à l'auteur.
