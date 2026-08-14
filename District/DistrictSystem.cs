using System;
using System.Collections.Generic;
using System.Linq;
using Life;
using Life.AreaSystem;
using Life.Network;
using Life.PermissionSystem;
using Life.VehicleSystem;
using PostApo.Core;
using UnityEngine;

namespace PostApo.District
{
    public sealed class DistrictSystem
    {
        private readonly PostApoPlugin _plugin;
        private readonly JsonStore<DistrictData> _store;
        private DistrictData _data;

        public GradeMenu Grades { get; private set; }

        public DistrictSystem(PostApoPlugin plugin, string root)
        {
            _plugin = plugin;
            _store = new JsonStore<DistrictData>(root, "districts.json");
            Grades = new GradeMenu(plugin, this);
            Reload();
        }

        public IEnumerable<District> All
        {
            get { return _data.districts ?? new List<District>(); }
        }

        public void Reload()
        {
            _data = _store.Load();
            if (_data.districts == null) { _data.districts = new List<District>(); }

            foreach (var district in _data.districts)
            {
                if (district != null) { district.Normalize(); }
            }

            Save();
        }

        public bool Save()
        {
            return _store.Save(_data);
        }

        public District Get(int id)
        {
            return _data.districts.FirstOrDefault(d => d != null && d.id == id);
        }

        public District GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return null; }
            var wanted = name.Trim();
            return _data.districts.FirstOrDefault(
                d => d != null && string.Equals(d.name, wanted, StringComparison.OrdinalIgnoreCase));
        }

        public District DistrictOf(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) { return null; }
            return _data.districts.FirstOrDefault(d => d != null && d.FindMember(steamId) != null);
        }

        public District DistrictOf(Player player)
        {
            return DistrictOf(Utils.SteamId(player));
        }

        public IEnumerable<District> WithSpecialite(string specialite)
        {
            return _data.districts.Where(d => d != null && d.HasSpecialite(specialite));
        }

        public bool Join(Player player, District district, out string error)
        {
            error = null;

            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId)) { error = "SteamID indisponible."; return false; }
            if (district == null) { error = "Ce district n'existe pas."; return false; }

            var current = DistrictOf(steamId);
            if (current != null)
            {
                if (current.id == district.id) { error = "Vous etes deja membre de ce district."; return false; }
                Leave(player, current, true);
            }

            var grade = district.FindGrade(district.defaultGradeId) ?? district.LowestGrade();

            district.members.Add(new Member
            {
                steamId = steamId,
                name = Utils.Name(player),
                characterId = Utils.CharacterId(player),
                gradeId = grade != null ? grade.id : 1,
                joinedAt = Utils.NowUnix(),
            });

            if (string.IsNullOrWhiteSpace(district.ownerSteamId))
            {
                district.ownerSteamId = steamId;
                district.ownerCharacterId = Utils.CharacterId(player);
                var top = district.HighestGrade();
                if (top != null)
                {
                    var member = district.FindMember(steamId);
                    if (member != null) { member.gradeId = top.id; }
                }
            }

            Save();
            SyncSharedProperties(district);

            _plugin.Webhook.LogDistrictJoin(Utils.Name(player), steamId, district.name,
                grade != null ? grade.name : "?");

            return true;
        }

        public bool Leave(Player player, District district, bool silent)
        {
            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId) || district == null) { return false; }

            var member = district.FindMember(steamId);
            if (member == null) { return false; }

            RevokeSharedProperties(district, member.characterId);

            district.members.Remove(member);
            district.playerOverrides.Remove(steamId);

            if (district.IsOwner(steamId))
            {
                var successor = district.members
                    .OrderByDescending(m => { var g = district.FindGrade(m.gradeId); return g != null ? g.rank : 0; })
                    .FirstOrDefault();

                district.ownerSteamId = successor != null ? successor.steamId : string.Empty;
                district.ownerCharacterId = successor != null ? successor.characterId : 0;
            }

            Save();

            if (!silent)
            {
                _plugin.Webhook.LogStaffAction(Utils.Name(player), steamId, "a quitte " + district.name);
            }

            return true;
        }

        public void RefreshMemberIdentity(Player player)
        {
            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId)) { return; }

            var district = DistrictOf(steamId);
            if (district == null) { return; }

            var member = district.FindMember(steamId);
            if (member == null) { return; }

            var characterId = Utils.CharacterId(player);
            var name = Utils.Name(player);
            var changed = false;

            if (characterId > 0 && member.characterId != characterId) { member.characterId = characterId; changed = true; }
            if (!string.Equals(member.name, name, StringComparison.Ordinal)) { member.name = name; changed = true; }

            if (district.IsOwner(steamId) && characterId > 0 && district.ownerCharacterId != characterId)
            {
                district.ownerCharacterId = characterId;
                changed = true;
            }

            if (changed)
            {
                Save();
                SyncSharedProperties(district);
            }
        }

        public bool HasPermission(Player player, string permission, string scopeKind = null, string scope = null)
        {
            var steamId = Utils.SteamId(player);
            if (string.IsNullOrEmpty(steamId) || string.IsNullOrEmpty(permission)) { return false; }

            var district = DistrictOf(steamId);
            if (district == null) { return false; }

            return HasPermission(district, steamId, permission, scopeKind, scope);
        }

        public bool HasPermission(District district, string steamId, string permission,
                                  string scopeKind = null, string scope = null)
        {
            if (district == null || string.IsNullOrEmpty(steamId) || string.IsNullOrEmpty(permission)) { return false; }

            if (district.IsOwner(steamId)) { return true; }

            var member = district.FindMember(steamId);
            if (member == null) { return false; }

            var isTerrain = string.Equals(scopeKind, "terrain", StringComparison.OrdinalIgnoreCase);
            var isVehicle = string.Equals(scopeKind, "vehicule", StringComparison.OrdinalIgnoreCase);

            PlayerOverride overrides;
            if (district.playerOverrides.TryGetValue(steamId, out overrides) && overrides != null)
            {
                if (!string.IsNullOrEmpty(scope))
                {
                    var table = isTerrain ? overrides.terrains : isVehicle ? overrides.vehicules : null;
                    var scoped = Lookup(table, scope, permission);
                    if (scoped.HasValue) { return scoped.Value; }
                }

                if (overrides.permissions != null
                    && overrides.permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            var grade = district.FindGrade(member.gradeId);
            if (grade == null) { return false; }

            if (!string.IsNullOrEmpty(scope))
            {
                var table = isTerrain ? grade.terrains : isVehicle ? grade.vehicules : null;
                var scoped = Lookup(table, scope, permission);
                if (scoped.HasValue) { return scoped.Value; }
            }

            return grade.Has(permission);
        }

        private static bool? Lookup(Dictionary<string, Dictionary<string, bool>> table, string scope, string permission)
        {
            if (table == null || string.IsNullOrEmpty(scope)) { return null; }

            Dictionary<string, bool> perScope;
            if (!table.TryGetValue(scope, out perScope) || perScope == null) { return null; }

            bool value;
            return perScope.TryGetValue(permission, out value) ? (bool?)value : null;
        }

        public void SyncSharedProperties(District district)
        {
            if (district == null) { return; }
            if (district.ownerCharacterId <= 0) { return; }

            try
            {
                SyncTerrains(district);
                SyncVehicles(district);
            }
            catch (Exception ex)
            {
                Utils.Warn("partage des proprietes du district " + district.id + " : " + ex.Message);
                _plugin.Webhook.LogError("SyncSharedProperties district " + district.id, ex);
            }
        }

        private void SyncTerrains(District district)
        {
            var manager = Nova.a;
            if (manager == null) { return; }

            var owned = manager.GetOwnedTerrains(district.ownerCharacterId);
            if (owned.terrains == null) { return; }

            foreach (var terrain in owned.terrains)
            {
                var area = manager.GetAreaById(terrain.id);
                if (area == null) { continue; }

                var scope = terrain.id.ToString();
                var dirty = false;

                foreach (var member in district.members.ToArray())
                {
                    if (member == null || member.characterId <= 0) { continue; }
                    if (member.characterId == district.ownerCharacterId) { continue; }

                    var allowed = HasPermission(district, member.steamId, Perm.AccesTerrain, "terrain", scope);
                    var already = area.permissions != null && area.permissions.HasPermission(member.characterId);

                    if (allowed && !already)
                    {
                        if (area.AddCoOwner(new Entity { characterId = member.characterId })) { dirty = true; }
                    }
                    else if (!allowed && already)
                    {
                        if (area.DeleteCoOwner(new Entity { characterId = member.characterId })) { dirty = true; }
                    }
                }

                if (dirty)
                {
                    try { area.Save(); }
                    catch (Exception ex) { Utils.Warn("sauvegarde du terrain " + scope + " : " + ex.Message); }
                }
            }
        }

        private void SyncVehicles(District district)
        {
            var manager = Nova.v;
            if (manager == null) { return; }

            var owned = manager.GetOwnedVehicles(district.ownerCharacterId);
            if (owned.vehicles == null) { return; }

            foreach (var vehicle in owned.vehicles)
            {
                if (vehicle == null) { continue; }

                var scope = vehicle.vehicleId.ToString();
                var dirty = false;

                foreach (var member in district.members.ToArray())
                {
                    if (member == null || member.characterId <= 0) { continue; }
                    if (member.characterId == district.ownerCharacterId) { continue; }

                    var allowed = HasPermission(district, member.steamId, Perm.UtiliserVehicule, "vehicule", scope);
                    var already = vehicle.permissions != null && vehicle.permissions.HasPermission(member.characterId);

                    if (allowed && !already)
                    {
                        if (vehicle.AddCoOwner(new Entity { characterId = member.characterId })) { dirty = true; }
                    }
                    else if (!allowed && already)
                    {
                        if (vehicle.DeleteCoOwner(new Entity { characterId = member.characterId })) { dirty = true; }
                    }
                }

                if (dirty)
                {
                    try { vehicle.Save(); }
                    catch (Exception ex) { Utils.Warn("sauvegarde du vehicule " + scope + " : " + ex.Message); }
                }
            }
        }

        private void RevokeSharedProperties(District district, int characterId)
        {
            if (district == null || characterId <= 0 || district.ownerCharacterId <= 0) { return; }

            try
            {
                var areaManager = Nova.a;
                if (areaManager != null)
                {
                    var owned = areaManager.GetOwnedTerrains(district.ownerCharacterId);
                    if (owned.terrains != null)
                    {
                        foreach (var terrain in owned.terrains)
                        {
                            var area = areaManager.GetAreaById(terrain.id);
                            if (area == null) { continue; }
                            if (area.DeleteCoOwner(new Entity { characterId = characterId })) { area.Save(); }
                        }
                    }
                }

                var vehicleManager = Nova.v;
                if (vehicleManager != null)
                {
                    var owned = vehicleManager.GetOwnedVehicles(district.ownerCharacterId);
                    if (owned.vehicles != null)
                    {
                        foreach (var vehicle in owned.vehicles)
                        {
                            if (vehicle == null) { continue; }
                            if (vehicle.DeleteCoOwner(new Entity { characterId = characterId })) { vehicle.Save(); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Warn("retrait des co-proprietes : " + ex.Message);
            }
        }

        public bool SetBase(District district, Vector3 position)
        {
            if (district == null) { return false; }
            district.baseSpawn = new Position(position);
            return Save();
        }

        public bool RemoveBase(District district)
        {
            if (district == null) { return false; }
            district.baseSpawn = null;
            return Save();
        }

        public void TeleportToBase(Player player, District district, bool checkPermission)
        {
            if (player == null || district == null) { return; }

            if (!district.HasBase)
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Ce district ne possede actuellement aucune base configuree."));
                return;
            }

            if (checkPermission && !HasPermission(player, Perm.TeleportBase))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player),
                    "teleportation refusee vers la base de " + district.name);
                return;
            }

            var destination = district.baseSpawn.ToVector3() + Vector3.up * 0.5f;
            if (Utils.Teleport(player, destination))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Ok("✓ Teleportation vers la base du " + district.name + "..."));
                _plugin.Webhook.LogTeleport(Utils.Name(player), Utils.SteamId(player), "Base du " + district.name);
            }
            else
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Teleportation impossible pour le moment."));
            }
        }

        public IEnumerable<InteractionPoint> CraftPoints()
        {
            foreach (var district in _data.districts)
            {
                if (district == null || district.craftPoints == null) { continue; }

                foreach (var point in district.craftPoints)
                {
                    if (point == null || point.position == null) { continue; }

                    var capturedDistrict = district;
                    var capturedPoint = point;

                    yield return new InteractionPoint
                    {
                        Key = "district-craft-" + district.id + "-" + point.id,
                        Position = point.position.ToVector3(),

                        VisibleTo = p => !capturedPoint.membersOnly
                                         || IsMemberOf(p, capturedDistrict)
                                         || Utils.IsStaff(p, _plugin.Config.staffLevelMin),
                        OnEnter = p => OpenCraftPoint(p, capturedDistrict, capturedPoint),
                    };
                }
            }
        }

        private bool IsMemberOf(Player player, District district)
        {
            var steamId = Utils.SteamId(player);
            return district != null && !string.IsNullOrEmpty(steamId) && district.FindMember(steamId) != null;
        }

        private void OpenCraftPoint(Player player, District district, DistrictCraftPoint point)
        {
            if (player == null || district == null || point == null) { return; }

            var isStaff = Utils.IsStaff(player, _plugin.Config.staffLevelMin);

            if (point.membersOnly && !IsMemberOf(player, district) && !isStaff)
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Cet atelier appartient au " + district.name + "."));
                return;
            }

            if (!isStaff && !HasPermission(player, Perm.CraftDistrict))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Vous n'avez pas la permission d'effectuer cette action."));
                _plugin.Webhook.LogAbuse(Utils.Name(player), Utils.SteamId(player),
                    "craft refuse a l'atelier " + point.specialite + " du " + district.name);
                return;
            }

            if (!district.HasSpecialite(point.specialite))
            {
                Utils.Send(player, _plugin.Prefix + Ui.Bad("✕ Ce district ne maitrise plus cette specialite."));
                return;
            }

            var recipes = _plugin.Craft.RecipesForSpecialite(point.specialite).ToList();
            var member = IsMemberOf(player, district);

            var title = Utils.Sanitize(district.name, 40);

            var header = Ui.Accent("Atelier « " + point.specialite + " »") + "\n"
                         + Ui.Dim(district.name + " — " + recipes.Count + " recette(s) exclusives a cette specialite") + "\n"
                         + (member
                             ? Ui.Ok("Membre du district.")
                             : Ui.Bad("Vous n'etes pas de ce district — acces staff."));

            _plugin.Craft.OpenMenu(player, title, header, recipes, point.position.ToVector3());
        }

        public bool AddCraftPoint(District district, string specialite, Vector3 position, out DistrictCraftPoint created)
        {
            created = null;
            if (district == null || string.IsNullOrWhiteSpace(specialite)) { return false; }

            var normalized = specialite.Trim().ToLowerInvariant();
            if (!district.HasSpecialite(normalized))
            {
                district.specialites.Add(normalized);
            }

            created = new DistrictCraftPoint
            {
                id = district.NextCraftPointId(),
                specialite = normalized,
                position = new Position(position),
            };

            district.craftPoints.Add(created);
            return Save();
        }

        public bool RemoveCraftPoint(District district, int pointId)
        {
            if (district == null || district.craftPoints == null) { return false; }

            var point = district.craftPoints.FirstOrDefault(p => p != null && p.id == pointId);
            if (point == null) { return false; }

            district.craftPoints.Remove(point);
            return Save();
        }

        public bool Create(int id, string name, out string error)
        {
            error = null;

            if (id <= 0) { error = "L'identifiant doit etre un entier positif."; return false; }
            if (Get(id) != null) { error = "Un district porte deja l'identifiant " + id + "."; return false; }
            if (string.IsNullOrWhiteSpace(name)) { error = "Le nom est obligatoire."; return false; }

            _data.districts.Add(new District
            {
                id = id,
                name = Utils.Sanitize(name, 48),
                description = "",
            });

            _data.districts = _data.districts.OrderBy(d => d.id).ToList();
            return Save();
        }

        public bool Delete(int id, out string error)
        {
            error = null;

            var district = Get(id);
            if (district == null) { error = "Ce district n'existe pas."; return false; }

            foreach (var member in district.members.ToArray())
            {
                RevokeSharedProperties(district, member.characterId);
            }

            _data.districts.Remove(district);
            return Save();
        }
    }
}
