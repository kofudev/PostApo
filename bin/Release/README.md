# PostApo — Plugin serveur Nova-Life : Amboise

> **Développé par kof660** — plugin standalone, aucune dépendance externe.

Plugin RP **post-apocalyptique** complet pour Nova-Life : Amboise.  
Pas de Kofu Core, pas de ModKit, pas de Harmony. Persistance **100 % JSON**, zéro base de données.

- **Framework :** .NET Framework 4.7.2 — sortie `Library` (`PostApo.dll`)  
- **API :** classes natives `Assembly-CSharp.dll` uniquement (vérifiées par réflexion)  
- **Version actuelle :** 1.0.0

---

## Sommaire

1. [Installation](#1-installation)
2. [Fichiers de données](#2-fichiers-de-données)
3. [Items — pré-configurés](#3-items--pré-configurés)
4. [Fonctionnalités](#4-fonctionnalités)
   - [Arrivée des nouveaux joueurs](#41-arrivée-des-nouveaux-joueurs)
   - [Districts](#42-districts)
   - [Grades & permissions](#43-grades--permissions)
   - [Partage des propriétés](#44-partage-des-propriétés)
   - [Établi](#45-établi)
   - [Craft](#46-craft)
   - [Gisements & épaves](#47-gisements--épaves)
   - [Filière véhicule](#48-filière-véhicule)
   - [Pied de biche permanent](#49-pied-de-biche-permanent)
   - [Webhook Discord](#410-webhook-discord)
5. [Menu staff `/staffapo`](#5-menu-staff-staffapo)
6. [Toutes les commandes](#6-toutes-les-commandes)
7. [Difficulté](#7-difficulté)
8. [Sécurité & anti-abus](#8-sécurité--anti-abus)
9. [Limites API](#9-limites-api)
10. [Compilation](#10-compilation)

---

## 1. Installation

```
Plugins/
└── PostApo/
    └── PostApo.dll
```

Dépose `PostApo.dll` dans `Plugins/PostApo/` et redémarre le serveur.  
Les fichiers JSON se créent automatiquement au premier démarrage.

> Niveau admin minimum requis : **3** (modifiable via `staffLevelMin` dans `config.json`)

---

## 2. Fichiers de données

| Fichier | Contenu |
|---|---|
| `config.json` | Configuration générale, récompenses, difficulté |
| `arrival_data.json` | Points d'arrivée + joueurs déjà initialisés |
| `welcome_data.json` | Joueurs ayant déjà reçu la récompense |
| `districts.json` | Districts, membres, grades, permissions, bases, ateliers |
| `recipes.json` | Toutes les recettes de craft |
| `etabli_data.json` | Points staff + établis posés par les joueurs |
| `farm_data.json` | Gisements, épaves, caches + cooldowns par joueur |
| `vehicles.json` | Plans de véhicule (20 modèles, 5 paliers) + ateliers |
| `vehicle_projects.json` | Chantiers en cours, paliers débloqués, contributions |

Un fichier corrompu est **mis en quarantaine** (renommé `.corrupt-<date>`) et remplacé par les valeurs par défaut — le serveur ne plante jamais.  
Toutes les écritures sont **atomiques**.

---

## 3. Items — pré-configurés

**Rien à régler.** Toutes les valeurs par défaut utilisent les IDs officiels Nova-Life : Amboise.

| Rôle | Item | ID |
|---|---|---|
| Pied de biche permanent | Pied de biche | `1580` |
| Établi personnel | Établi de maison | `1755` |
| Objet décoratif établi | Établi (meuble) | `1318` |
| Récompense bienvenue | Cuivre ×5 + Pierre ×5 + Petit lingot d'or ×2 | `30` / `29` / `1724` |
| Outil avancé | Boîte à outils | `1213` |

**Ressources brutes** (à placer en gisements) :  
`29` Pierre · `30` Cuivre · `31` Diamant · `33` Bûche · `82` Sable · `1419` Magnétite · `1505` Tomate · `1984` Pomme de terre

**Transformés** :  
`79` Cuivre raffiné · `1722` Lingot de cuivre · `1425` Lingot de magnétite · `1724` Petit lingot d'or · `1081` Planche · `1219` Verre · `1088` Plastique · `1089` Caoutchouc · `1429` Plaque de métal · `1430` Poutre en métal · `1222` Structure métallique

**Pièces véhicule** : `3` Bougie d'allumage · `5` Batterie · `1530` Pneu · `1083` Machine d'assemblage auto

**Composants rares** (épaves uniquement) :  
`95` Calculateur · `1336` Faisceau électronique · `1373` Outillage de précision · `1590` Cellule haute densité

Pour retrouver un ID en jeu :
```
/postapo finditem <texte>
```

---

## 4. Fonctionnalités

### 4.1 Arrivée des nouveaux joueurs

Déclenché **une seule fois par SteamID** sur `OnPlayerSpawnCharacter` :

1. Téléportation sur un point d'arrivée tiré au hasard
2. Introduction paginée au monde post-apo (texte configurable dans `config.json`)
3. Choix du district avec fiche détaillée pour chacun
4. Inscription automatique dans `districts.json`
5. Récompense de bienvenue (5 Cuivre + 5 Pierre + 2 Petit lingot d'or)
6. Proposition de rejoindre la base du district

Recréer un personnage ne rejoue pas le parcours. `/postapo resetjoueur <steamid>` le remet à zéro.

---

### 4.2 Districts

Cinq districts livrés par défaut, chacun avec une spécialité de craft exclusive :

| # | Nom | Spécialité |
|---|---|---|
| 1 | Les Terres Grises | `agriculture` |
| 2 | Le Dispensaire | `medecine` |
| 3 | La Casse | `construction_vehicule` |
| 4 | L'Arsenal | `armes` |
| 5 | La Fonderie | `industrie` |

Chaque district a : un propriétaire, des membres, des grades, une base, des ateliers spécialisés.  
`districts.json` est la **source de vérité unique** — pas de double système.

---

### 4.3 Grades & permissions

Résolution dans l'ordre : **propriétaire → surcharge joueur → grade**.

**Permissions de district :**  
`gererDistrict` · `gererGrades` · `gererPermissions` · `inviterMembre` · `expulserMembre` · `teleportBase` · `craftDistrict`

**Permissions terrain :**  
`accesTerrain` · `ouvrirPorte` · `ouvrirCoffre` · `poserItem` · `prendreItem`

**Permissions véhicule :**  
`utiliserVehicule` · `ouvrirVehicule` · `demarrerVehicule`

Grades par défaut :

| Grade | rang | Droits notables |
|---|---|---|
| Adjoint | 90 | Tout — poser, retirer, coffres, gestion grades |
| Officier | 75 | Tout sauf poser/retirer |
| Membre | 50 | Accès terrain, ouvrir portes, utiliser véhicules |
| Recrue | 10 | Accès terrain, craft district, TP base |

**Règle de fer :** personne ne peut donner un grade supérieur ou égal au sien. Contrôle refait côté serveur à chaque action.

Le menu `/district` → **Gérer les grades** permet au propriétaire de créer, renommer, modifier et attribuer les grades sans toucher au JSON.

---

### 4.4 Partage des propriétés

Les terrains et véhicules du **propriétaire** du district sont partagés avec les membres autorisés via le mécanisme natif (`LifeArea.AddCoOwner` / `LifeVehicle.AddCoOwner`).

Synchronisation automatique à chaque : adhésion, départ, changement de grade, apparition de personnage.

---

### 4.5 Établi

1. Le staff place un **point de fabrication communal** avec `/etabli_point set`
2. Le joueur s'y rend et fabrique son **établi personnel** (ressources brutes uniquement)
3. Il le pose définitivement avec `/etabli pose` — **une seule fois par joueur**
4. L'établi persiste entre les redémarrages et peut être partagé avec le district

L'établi donne accès aux **recettes génériques**. Les recettes avancées nécessitent les ateliers spécialisés de district.

> Les menus de l'atelier communal et de l'établi posé affichent maintenant chaque ressource avec la quantité possédée et la quantité requise. Plus besoin de deviner.

---

### 4.6 Craft

Moteur unique partagé entre l'établi et tous les ateliers de district.

**Ce que voit le joueur dans la liste de recettes :**
- `● Lingot de cuivre → 1 × Lingot de cuivre (30 s)` — vert si ressources disponibles
- `○ Batterie → 1 × Batterie (120 s)` — gris + résumé du manque si insuffisant

**Dans la fiche détaillée :**
- Chaque ressource : `✓ 5 × Cuivre (sur vous : 7/5)`
- Résultat mis en avant, durée et risque d'échec en rouge si > 0 %
- Bouton de lancement grisé si ressources insuffisantes

**Les matériaux sont consommés au démarrage** — aucune duplication possible par déconnexion ou craft parallèle.

Chaîne véhicule (extrait) :

```
ÉTABLI (accessible à tous)
  5 × Cuivre → 1 × Lingot de cuivre            30 s
  3 × Cuivre → 1 × Cuivre raffiné              20 s
  2 × Bûche  → 3 × Planche                     15 s

INDUSTRIE — La Fonderie
  5 × Magnétite     → 1 × Lingot de magnétite   45 s
  2 × L. magnétite  → 1 × Plaque de métal       60 s
  3 × L. magnétite + 1 Plaque → Poutre          80 s
  4 × Poutre + 6 × Plaque → Structure          150 s  ⚠ échec 15 %

CONSTRUCTION VÉHICULE — La Casse
  6 × Caoutchouc + 1 Plaque → Pneu              50 s
  4 × L. cuivre + 3 Plastique + 1 Or → Batterie 120 s ⚠ échec 20 %
  [Structure + Batterie + 4 Pneus + ...] → Machine d'assemblage auto 360 s ⚠ échec 25 %
```

Aucun district ne peut tout faire seul — la Casse dépend des plaques de la Fonderie. Le commerce inter-districts est structurellement obligatoire.

---

### 4.7 Gisements & épaves

Les matières premières se récoltent, elles ne s'achètent pas.

**Gisements** (`/farm_point set <itemId> <nom>`) :
- Stock limité qui se régénère
- Cooldown par joueur, persisté entre les redémarrages
- Outil éventuellement requis
- Peut être réservé à une spécialité de district
- Bouger annule la récolte

**Épaves & caches** (`/farm_point epave <palier>`) :

| Palier | Plan trouvable | Chance plan | Composants rares |
|---|---|---|---|
| 1 | Citadines | 18 % | — |
| 2 | Utilitaires | 12 % | — |
| 3 | Routières | 8 % | Faisceau électronique |
| 4 | Sportives | 5 % | Calculateur + Outillage *(boîte à outils requise)* |
| 5 | Légendes | 3 % | Cellule haute densité *(boîte à outils requise)* |

Les joueurs les localisent avec `/epaves` (cap + distance des plus proches).

---

### 4.8 Filière véhicule

Le résultat final est un **vrai véhicule Nova-Life** — créé via `LifeDB.CreateVehicle`, immatriculé, sauvegardé. Pas un item symbolique.

**4 verrous empêchent le raccourci :**

1. **Il faut un plan** — trouvable uniquement en fouillant des épaves, jamais craftable
2. **C'est un chantier collectif multi-étapes** — pas un craft instantané
3. **Des composants rares ne se fabriquent pas** — faisceau, calculateur, cellule HD : épaves uniquement
4. **Progression par district** — on ne construit pas une Delorean avant d'avoir remonté une 206

| Palier | Nom | Étapes | Exemples |
|---|---|---|---|
| 1 | Épave roulante | 3 | 206, Kart, Renaud Express |
| 2 | Utilitaire | 4 | Berlingo, Mégane IV, Master, C4 Picasso |
| 3 | Routière | 5 | 5008, Range River, Korn Ranger, Dépanneuse |
| 4 | Sportive | 6 | RX7, Dodge Charger 1970, Stellar coupé |
| 5 | Légende | 7 | Stellar 911 RS, V Model S, Delorean CMD-12 |

**Ce que voit le joueur dans le Catalogue :**
- Chaque modèle : nombre d'étapes + total de pièces à réunir
- Fiche détaillée : description de chaque étape, ressources avec `sur vous : X/Y`, risque d'échec par étape, **section TOTAL À RÉUNIR** avec ce qui est déjà en poche

Le chantier est collectif : chacun livre ce qu'il porte, les contributions sont nommées, les travaux avancent même déconnecté.

---

### 4.9 Pied de biche permanent

Remis à chaque apparition, après chaque mort, et re-vérifié périodiquement (configurable).  
Item `1580`, configurable dans `config.json` (`crowbar.itemId`).

---

### 4.10 Webhook Discord

File d'attente dédiée, Discord injoignable = **aucun impact sur le gameplay**.

Événements journalisés : première connexion, choix de district, récompense, craft réussi/raté, récoltes, actions staff, tentatives d'abus, erreurs internes.

Configuration : `webhookUrl` dans `config.json`, actif après `/postapo reload`.

---

## 5. Menu staff `/staffapo`

Alias : `/sa`

Panel in-game centralisé — plus besoin de mémoriser les commandes texte pour la gestion courante.

```
/staffapo
```

### Panneau principal

Vue d'ensemble en temps réel : nombre de districts, membres, établis posés, recettes valides/KO.

| Section | Ce qu'on peut faire |
|---|---|
| **Districts** | Inspecter chaque district, voir membres et grades, forcer un palier véhicule, TP à la base |
| **Joueurs en ligne** | Voir le district/grade de chaque joueur connecté, supprimer son établi, reset son parcours d'arrivée |
| **Établis posés** | Liste complète, clic pour inspecter et supprimer avec confirmation |
| **Ateliers craft** | Vue de tous les points configurés par district + nombre de recettes disponibles |
| **Ateliers véhicule** | État de chaque atelier (palier, chantiers en cours, véhicules terminés) |
| **Recharger la config** | Équivalent `/postapo reload` directement depuis le menu |

### Audit des grades

Dans **Districts → [district] → Vérifier les accès** :  
Affiche tous les grades avec **chaque permission cochée ou non** — vérification visuelle complète en un clic.

```
Adjoint  rang 90 · 1 membre
  ✓ Entrer sur les terrains
  ✓ Ouvrir les coffres
  ✓ Utiliser les véhicules
  ✓ Gérer les grades
  ...

Recrue  rang 10 · 4 membres
  ✓ Entrer sur les terrains
  ✕ Ouvrir les coffres
  ✕ Utiliser les véhicules
  ...
```

---

## 6. Toutes les commandes

### Commandes staff *(niveau ≥ `staffLevelMin`, défaut 3)*

| Commande | Description |
|---|---|
| `/staffapo` | **Menu staff in-game** — gestion complète sans commandes texte |
| `/postapo status` | État complet du plugin |
| `/postapo reload` | Recharge tous les JSON à chaud |
| `/postapo finditem <texte>` | Recherche un item par nom ou slug |
| `/postapo iteminfo` | IDs des items dans votre inventaire |
| `/postapo resetjoueur <steamid>` | Réinitialise le parcours d'arrivée d'un joueur |
| `/postapo etablidel <steamid>` | Supprime l'établi posé d'un joueur (rend la pose) |
| `/spawn_arrivee set [nom]` | Pose un point d'arrivée à votre position |
| `/spawn_arrivee remove <id>` | Supprime un point d'arrivée |
| `/spawn_arrivee list` | Liste les points d'arrivée |
| `/spawn_arrivee tp <id>` | Téléporte au point d'arrivée |
| `/district create <id> <nom>` | Crée un district |
| `/district delete <id>` | Supprime un district |
| `/district owner <id> <steamid>` | Attribue le district à un joueur |
| `/district spec <id> add\|remove <spec>` | Ajoute/retire une spécialité |
| `/district_base set <districtId>` | Définit la base à votre position |
| `/district_base remove <districtId>` | Supprime la base |
| `/district_base teleport <districtId>` | Téléporte à la base |
| `/district_craft set <districtId> <spec>` | Pose un atelier à votre position |
| `/district_craft remove <districtId> <pointId>` | Supprime un atelier |
| `/district_craft list` | Liste tous les ateliers |
| `/etabli_point set [nom]` | Pose un point de fabrication d'établi |
| `/etabli_point remove [id]` | Supprime un point |
| `/etabli_point list` | Liste les points |
| `/farm_point set <itemId> [nom]` | Pose un gisement à votre position |
| `/farm_point epave <palier 1-5> [nom]` | Pose une épave/cache |
| `/farm_point remove <id>` | Supprime un point |
| `/farm_point list` | Liste tous les points |
| `/farm_point tp <id>` | Téléporte à un point |
| `/atelier set <districtId> [nom]` | Pose un atelier de reconstruction véhicule |
| `/atelier remove <id>` | Supprime un atelier |
| `/atelier list` | Liste les ateliers |
| `/atelier palier <districtId> <1-5>` | Force le palier débloqué d'un district |

### Commandes joueurs

| Commande | Description |
|---|---|
| `/district` | Menu du district (infos, membres, base, grades, quitter) |
| `/district list` | Liste tous les districts |
| `/district info [id]` | Fiche détaillée d'un district |
| `/district leave` | Quitter son district |
| `/district setgrade <joueur> <gradeId>` | Attribuer un grade *(permission `gererGrades`)* |
| `/district kick <joueur>` | Expulser un membre *(permission `expulserMembre`)* |
| `/etabli` | Ouvre le menu de l'établi |
| `/etabli pose` | Installe son établi (une seule fois, définitif) |
| `/atelier` | Chantiers du district, palier débloqué, contributeurs |
| `/plans` | Mes plans de véhicule et ce qu'ils permettent de construire |
| `/epaves` | Boussole : épaves et caches proches (cap + distance) |
| `/gisements` | Boussole : gisements proches |

> Les établis, ateliers et gisements s'ouvrent aussi **en marchant dessus** (checkpoints natifs Nova-Life).

---

## 7. Difficulté

Section `difficulty` dans `config.json` — valeurs par défaut volontairement sévères :

| Clé | Défaut | Effet |
|---|---|---|
| `craftTimeMultiplier` | `1.5` | Multiplicateur de durée sur tous les crafts |
| `craftFailureChance` | `0.12` | Probabilité d'échec (consomme quand même les matériaux) |
| `craftCancelRefundRatio` | `0.25` | Part des matériaux rendus si le joueur s'éloigne |
| `farmYieldMultiplier` | `1.0` | Multiplicateur de rendement des gisements |
| `farmTimeMultiplier` | `1.4` | Multiplicateur de durée des récoltes |
| `farmCooldownMultiplier` | `1.6` | Multiplicateur des cooldowns et repousses |
| `farmInjuryChance` | `0.10` | Probabilité de blessure à la récolte |
| `farmInjuryDamage` | `6` | Dégâts d'une blessure |
| `farmInjuryMinHealth` | `12` | Plancher de PV (une récolte ne tue jamais) |

**Trop lent ?** `farmYieldMultiplier` → 2, `farmCooldownMultiplier` → 1  
**Trop facile ?** `craftTimeMultiplier` → 2.5, `craftCancelRefundRatio` → 0

---

## 8. Sécurité & anti-abus

- **Récompense de bienvenue** : marquée *avant* distribution — impossible de la recevoir deux fois
- **Pose d'établi** : SteamID enregistré, item rendu si la sauvegarde échoue
- **Craft** : matériaux consommés *au démarrage*, un seul craft simultané par joueur, retour arrière complet si retrait échoue, rendu si inventaire plein à la livraison
- **Récolte** : une seule à la fois, stock re-vérifié à la fin, cooldown persisté sur disque
- **Grades** : contrôle hiérarchique refait côté serveur à *chaque* action — impossible de se promouvoir via manipulation de menu
- **Commandes staff** : vérification `account.AdminLevel` systématique
- **JSON corrompu** : mis en quarantaine, démarrage normal
- **Callbacks natifs** : encapsulés — une erreur du plugin ne remonte jamais au serveur
- **Webhook** : toutes les tentatives d'abus sont journalisées

---

## 9. Limites API

1. **Pas d'interception granulaire des interactions** — l'API Nova-Life n'expose aucun événement annulable avant l'ouverture d'une porte, d'un coffre ou le démarrage d'un véhicule. Seul le mécanisme de co-propriété natif est disponible. Les permissions `ouvrirCoffre`, `poserItem`, `ouvrirVehicule` sont configurables et persistées, mais leur application est limitée aux actions que le plugin contrôle directement.

2. **Établi = checkpoint + meuble décoratif** — `AreaManager.CreateObject` ne retourne rien : supprimer un point d'établi retire le checkpoint, mais pas le meuble physique (à retirer manuellement). Désactivable via `etabli.spawnPhysicalObject: false`.

3. **Item non verrouillable** — Nova-Life n'expose aucun moyen de verrouiller un slot d'inventaire depuis un plugin. Le pied de biche est donc remis périodiquement, pas bloqué en slot.

---

## 10. Compilation

```bash
dotnet build -c Release
```

Sortie : `bin/Release/PostApo.dll`  
Les références pointent vers `../libs/` en `Private=false` — **seul `PostApo.dll` est à déployer**.
