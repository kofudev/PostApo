# PostApo — Guide d'installation

> Développé par **kof660** pour Nova-Life : Amboise

Tout est pré-configuré avec les IDs officiels — **aucune recherche d'item n'est nécessaire**.  
Il y a uniquement des points à poser en jeu.

⏱ **Durée estimée : ~30 minutes**  
Les étapes **1 à 6 sont obligatoires**. Les étapes 7 à 10 se font à ton rythme.

---

## Sommaire

- [Étape 0 — Déploiement](#étape-0--déploiement)
- [Étape 1 — Vérifier le chargement](#étape-1--vérifier-le-chargement)
- [Étape 2 — Points d'arrivée](#étape-2--points-darrivée--obligatoire)
- [Étape 3 — Bases des districts](#étape-3--bases-des-5-districts--obligatoire)
- [Étape 4 — Établi communal](#étape-4--établi-communal--obligatoire)
- [Étape 5 — Ateliers spécialisés](#étape-5--ateliers-spécialisés--obligatoire)
- [Étape 6 — Atelier véhicule](#étape-6--atelier-de-reconstruction-véhicule--obligatoire)
- [Étape 7 — Gisements](#étape-7--gisements)
- [Étape 8 — Épaves & caches](#étape-8--épaves--caches)
- [Étape 9 — Webhook Discord](#étape-9--webhook-discord-optionnel)
- [Étape 10 — Propriétaires de districts](#étape-10--propriétaires-de-districts)
- [Menu staff `/staffapo`](#menu-staff-staffapo)
- [Test de bout en bout](#test-de-bout-en-bout)
- [Référence joueur](#référence-joueur)
- [Réglages de difficulté](#réglages-de-difficulté)
- [Dépannage](#dépannage)

---

## Étape 0 — Déploiement

```
Plugins/
└── PostApo/
    └── PostApo.dll
```

1. Copie `bin/Release/PostApo.dll` dans le dossier `Plugins/PostApo/` de ton serveur
2. Redémarre le serveur
3. Les 9 fichiers JSON se créent automatiquement

> **Niveau admin requis : 3 minimum**  
> Modifiable dans `Plugins/PostApo/config.json` → `staffLevelMin`

---

## Étape 1 — Vérifier le chargement

```
/postapo status
```

**Résultat attendu :**
```
Recettes : 37 valides
Plans véhicule : 20 — ateliers 0, chantiers 0
Pied de biche : item 1580
```

> ⚠️ Si tu vois **0 valides** : les items du serveur ne sont pas encore chargés.  
> Attends 30 secondes et retape `/postapo status`. Le plugin se resynchronise seul.

Tu peux aussi ouvrir le menu staff à tout moment :
```
/staffapo
```

---

## Étape 2 — Points d'arrivée *(obligatoire)*

Sans ça, les nouveaux joueurs apparaissent au spawn par défaut du serveur.

Place-toi à 3 à 5 endroits différents en ville et tape :

```
/spawn_arrivee set Place du marché
/spawn_arrivee set Ancienne gare
/spawn_arrivee set Quai de la Loire
```

**Vérification :**
```
/spawn_arrivee list
```
Téléporte-toi sur un point pour confirmer : `/spawn_arrivee tp 1`

---

## Étape 3 — Bases des 5 districts *(obligatoire)*

Place-toi à l'emplacement de chaque base, puis tape :

```
/district_base set 1
/district_base set 2
/district_base set 3
/district_base set 4
/district_base set 5
```

| ID | District | Spécialité |
|---|---|---|
| 1 | Les Terres Grises | agriculture |
| 2 | Le Dispensaire | médecine |
| 3 | La Casse | construction véhicule |
| 4 | L'Arsenal | armes |
| 5 | La Fonderie | industrie |

> Sans base configurée, le nouveau joueur voit un message d'erreur et reste sur place.

---

## Étape 4 — Établi communal *(obligatoire)*

C'est ici que les joueurs fabriquent leur établi personnel. **Un seul suffit**, en zone neutre accessible à tous.

```
/etabli_point set Atelier communal
```

**Ce que le joueur voit en arrivant sur le point :**
- Explication du rôle de l'atelier
- Liste de chaque ressource requise avec `X sur vous / Y nécessaires`
- Bouton **▶ FABRIQUER MON ÉTABLI** dès qu'il a tout

**Coût par défaut :** `20 × Bûche (33)` + `6 × Cuivre (30)` + `40 × Pierre (29)`  
*(matières premières uniquement — aucune ne dépend d'un établi)*

Ensuite le joueur pose son établi avec `/etabli pose` — **définitif, une seule fois par joueur**.

---

## Étape 5 — Ateliers spécialisés *(obligatoire)*

Un atelier par district, **dans la base du district**, sur sa spécialité.

Place-toi dans chaque base puis tape :

```
/district_craft set 1 agriculture
/district_craft set 2 medecine
/district_craft set 3 construction_vehicule
/district_craft set 4 armes
/district_craft set 5 industrie
```

Chaque atelier ne propose que les recettes de sa spécialité.  
C'est le mécanisme qui **force le commerce inter-districts** — la Casse ne peut rien faire sans les plaques de la Fonderie.

**Vérification :**
```
/district_craft list
```

---

## Étape 6 — Atelier de reconstruction véhicule *(obligatoire)*

C'est là que vivent les chantiers véhicule. Place-toi dans la base de La Casse (district 3) :

```
/atelier set 3
```

> Tu peux en donner un à d'autres districts plus tard avec `/atelier set 5`.  
> Commencer par un seul garde la construction automobile identitaire de La Casse.

---

## Étape 7 — Gisements

Les recettes consomment ces ressources. Place chaque gisement à un filon visible sur la carte.

| ID | Ressource | Utilisée pour |
|---|---|---|
| `1419` | Magnétite | **Toute la filière métal** (lingots → plaques → poutres → structure) |
| `30` | Cuivre | Lingots, câblage, bougies d'allumage |
| `29` | Pierre | Coût de fabrication de l'établi |
| `33` | Bûche | Planches |
| `82` | Sable | Verre |
| `1089` | Caoutchouc | Pneus |
| `1088` | Plastique | Batteries |
| `1724` | Petit lingot d'or | Batteries (véhicules), homologation |
| `31` | Diamant | Véhicules palier 5 uniquement |

Place-toi sur un filon et tape :

```
/farm_point set 1419 Filon de magnétite
/farm_point set 30 Veine de cuivre
/farm_point set 1089 Dépôt de caoutchouc
/farm_point set 1088 Décharge plastique
```

> 💡 Si ton serveur a déjà du minage natif pour certaines ressources, ne les repose pas.  
> La **magnétite** est la plus critique — prévois **3 à 5 filons** minimum.

Ajuste rendement, stock et repousse dans `farm_data.json`, puis `/postapo reload`.

---

## Étape 8 — Épaves & caches

C'est **le seul endroit** où tombent les plans de véhicule et les composants rares.  
Sans épaves configurées, aucun véhicule n'est constructible.

> ⚠️ Tout ce que tu tapes après le palier devient le **nom du point**.  
> `/farm_point epave 1 Vieille voiture` crée un point nommé « Vieille voiture ».

```
/farm_point epave 1
/farm_point epave 2
/farm_point epave 3
/farm_point epave 4
/farm_point epave 5
```

**Ce que donne chaque palier :**

| Commande | Plan trouvable | Chance | Composants rares |
|---|---|---|---|
| `epave 1` | Citadines (206, Kart...) | 18 % | — |
| `epave 2` | Utilitaires (Berlingo, Mégane...) | 12 % | — |
| `epave 3` | Routières (5008, Range River...) | 8 % | Faisceau électronique |
| `epave 4` | Sportives (RX7, Dodge...) | 5 % | Calculateur + Outillage *(boîte à outils requise)* |
| `epave 5` | Légendes (Stellar 911, Delorean...) | 3 % | Cellule haute densité *(boîte à outils requise)* |

**Répartition conseillée :**

| Palier | Nombre | Emplacement suggéré |
|---|---|---|
| 1–2 | 8 à 12 | Bords de route, parkings, périphérie |
| 3 | 4 à 6 | Zones isolées, forêts, entrepôts |
| 4 | 2 à 3 | Accès difficile |
| 5 | 1 à 2 | L'endroit le plus reculé de la carte |

Les joueurs les repèrent avec `/epaves` (cap + distance des plus proches).

---

## Étape 9 — Webhook Discord *(optionnel)*

Ouvre `Plugins/PostApo/config.json` et renseigne :

```json
"webhookUrl": "https://discord.com/api/webhooks/VOTRE_URL_ICI"
```

Puis :
```
/postapo reload
```

> Discord injoignable n'a **aucun** effet sur le gameplay.  
> Événements journalisés : connexions, crafts, récoltes, actions staff, tentatives d'abus.

---

## Étape 10 — Propriétaires de districts

Le premier joueur à rejoindre un district vide en devient automatiquement propriétaire.

Pour le définir toi-même (le joueur doit déjà être membre) :

```
/district owner 3 76561198000000000
```

Le propriétaire partage automatiquement ses terrains et véhicules avec les membres qui ont les permissions correspondantes.

---

## Menu staff `/staffapo`

Alias : `/sa`

Une fois tout configuré, utilise ce menu pour gérer le serveur sans taper de commandes :

```
/staffapo
```

**Ce que tu peux faire depuis ce menu :**

| Onglet | Actions |
|---|---|
| 🏘 Districts | Inspecter chaque district, voir membres/grades, forcer un palier véhicule, TP base |
| 👥 Joueurs en ligne | Voir district et grade de chaque connecté, supprimer établi, reset arrivée |
| 🔨 Établis posés | Liste complète, supprimer avec confirmation |
| 🔧 Ateliers craft | Vue de tous les points + recettes disponibles |
| 🚗 Ateliers véhicule | Palier, chantiers en cours, véhicules terminés |
| ↺ Recharger config | Équivalent `/postapo reload` |

**Audit des grades** (Districts → [district] → Vérifier les accès) :  
Affiche toutes les permissions cochées ou non pour chaque grade — vérification visuelle complète.

---

## Test de bout en bout

Avant d'ouvrir le serveur, fais ce parcours complet avec un compte test :

1. **Nouveau joueur** → intro paginée → choix de district → 5 Cuivre + 5 Pierre + 2 Or → proposition TP base
2. `/district` → menu avec icônes, liste des membres, base, **Gérer les grades**
3. **Marche sur un gisement** → menu de récolte avec stock et cooldown affiché
4. `/epaves` → cap et distance → fouille jusqu'à un **Plan griffonné** (palier 1)
5. **Va à l'atelier communal** → vois chaque ressource requise → **▶ FABRIQUER MON ÉTABLI** → `/etabli pose`
6. **Marche sur ton établi** → liste de recettes avec résultat visible sur chaque ligne
7. **Clique une recette** → fiche avec `sur vous : X/Y` pour chaque ressource → lancer la fabrication
8. **Atelier de reconstruction** → **Catalogue** → fiche d'un véhicule → vois toutes les étapes + TOTAL À RÉUNIR
9. **Démarrer un chantier** avec le plan → livrer → lancer les travaux → terminer les 3 étapes d'une 206
10. Le véhicule apparaît immatriculé à ton nom, le palier 2 se débloque

---

## Référence joueur

### Commandes disponibles

```
/district           Menu du district (infos, membres, base, grades)
/etabli             Menu de l'établi
/etabli pose        Installe son établi (une seule fois, définitif)
/atelier            Chantiers du district et palier débloqué
/plans              Mes plans de véhicule
/epaves             Boussole des épaves et caches proches
/gisements          Boussole des gisements proches
```

> Les établis, ateliers et gisements s'ouvrent aussi **en marchant dessus**.

### Qui peut faire quoi dans un district

| Action | Propriétaire | Adjoint | Officier | Membre | Recrue |
|---|---|---|---|---|---|
| Créer/supprimer un grade | ✅ | ❌ | ❌ | ❌ | ❌ |
| Modifier les droits d'un grade | ✅ | ❌ | ❌ | ❌ | ❌ |
| Attribuer un grade | ✅ | ✅ *(inférieurs)* | ❌ | ❌ | ❌ |
| Inviter un membre | ✅ | ✅ | ✅ | ❌ | ❌ |
| Expulser un membre | ✅ | ✅ | ✅ | ❌ | ❌ |
| TP à la base | ✅ | ✅ | ✅ | ✅ | ✅ |
| Utiliser les ateliers | ✅ | ✅ | ✅ | ✅ | ✅ |
| Ouvrir les coffres | ✅ | ✅ | ✅ | ❌ | ❌ |
| Poser / retirer des objets | ✅ | ✅ | ❌ | ❌ | ❌ |
| Utiliser les véhicules | ✅ | ✅ | ✅ | ✅ | ❌ |

### Comment fonctionnent les plans de véhicule

Un plan est un **objet dans ton inventaire**, trouvé en fouillant une épave.  
Il se consomme quand *toi* ouvres un chantier à l'atelier de *ton district*.  
Le véhicule terminé est immatriculé à **ton nom**, même si tout le district a participé.  
`/plans` explique tout ça en jeu.

---

## Réglages de difficulté

Tout est dans `config.json` → section `difficulty`, appliqué à chaud avec `/postapo reload`.

| Clé | Défaut | Effet |
|---|---|---|
| `craftTimeMultiplier` | `1.5` | Durée des crafts |
| `craftFailureChance` | `0.12` | Chance d'échec (perd les matériaux) |
| `craftCancelRefundRatio` | `0.25` | Part rendue si interruption |
| `farmYieldMultiplier` | `1.0` | Rendement des gisements |
| `farmCooldownMultiplier` | `1.6` | Cooldowns et repousses |

**Serveur trop lent ?**
```json
"farmYieldMultiplier": 2.0,
"farmCooldownMultiplier": 1.0
```

**Serveur trop facile ?**
```json
"craftTimeMultiplier": 2.5,
"craftCancelRefundRatio": 0
```

Les quantités de ressources par véhicule se règlent dans `vehicles.json`.  
La rareté des plans et la fréquence des composants rares se règlent dans `farm_data.json`.

---

## Dépannage

### Les points que je viens de poser n'apparaissent pas

Ils apparaissent automatiquement en moins de 10 secondes. Le staff voit **tous** les points, même ceux des districts dont il n'est pas membre.

Si rien n'apparaît, vérifie que le point existe vraiment :
```
/district_craft list
/etabli_point list
/farm_point list
/atelier list
```

### Un joueur ne peut pas fabriquer son établi

Depuis la dernière mise à jour, le menu affiche exactement ce qui manque avec les quantités.  
Le coût par défaut utilise des **matières premières** (Bûche, Cuivre, Pierre) — vérifie que tu n'as pas donné des *planches* ou des *lingots* au joueur, ce sont des items différents.

### Les recettes sont désactivées (0 valides)

Juste après un démarrage, les items du serveur se chargent *après* les plugins.  
Attends 30 secondes et tape `/postapo status`. Le plugin se resynchronise seul.

### Un joueur est bloqué / a besoin d'un reset

```
/postapo resetjoueur <steamid>     remet à zéro le parcours d'arrivée
/postapo etablidel <steamid>       supprime l'établi posé et rend la pose
```
Ou utilise le menu `/staffapo` → Joueurs en ligne → [joueur] pour faire ça en clics.

### Vérifier les droits d'un grade

```
/staffapo → Districts → [district] → Vérifier les accès (grades)
```
Affiche toutes les permissions de tous les grades côte à côte.

### Référence commandes staff complètes

```
/staffapo                          menu staff in-game (recommandé)
/postapo status                    état complet du plugin
/postapo reload                    recharge tous les JSON à chaud
/postapo finditem <texte>          retrouve un ID d'item par nom
/postapo iteminfo                  IDs des items dans ton inventaire
/postapo resetjoueur <steamid>     reset du parcours d'arrivée
/postapo etablidel <steamid>       supprime l'établi posé d'un joueur
/atelier palier <districtId> <1-5> force le palier véhicule d'un district
```
