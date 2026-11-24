# PostApo

Plugin pour **Nova-Life: Amboise** : un mode de jeu de rôle post-apocalyptique organisé autour de cinq districts. Les joueurs explorent, récupèrent des ressources, fabriquent des objets et reconstruisent des véhicules.

Le plugin est autonome. Il enregistre ses données au format JSON et ne demande pas de base de données externe.

## Installation

1. Compile le projet avec `dotnet build -c Release`.
2. Copie `bin/Release/PostApo.dll` dans `Plugins/PostApo/` sur le serveur.
3. Redémarre le serveur.

La configuration et les fichiers de données sont créés au premier lancement. Consulte [SETUP.md](SETUP.md) pour les étapes détaillées.

## Ce que le plugin comprend

- Arrivée des joueurs et choix d’un district
- Gestion des districts, des membres, des grades et des permissions
- Établi personnel, recettes et spécialités
- Exploration de gisements, d’épaves et de caches
- Projets de véhicules à construire en plusieurs étapes
- Notifications d’événements vers Discord, si un webhook est configuré

## Commandes

Les commandes joueurs incluent `/district`, `/etabli`, `/atelier`, `/plans`, `/epaves` et `/gisements`. Les commandes de configuration staff sont détaillées dans [SETUP.md](SETUP.md).

## Dépendances et licence

Cible .NET Framework 4.7.2. Les références Nova-Life et Unity sont fournies par le serveur.

Aucune licence n’est déclarée dans le dépôt.
Kofu
KOFUDEV
