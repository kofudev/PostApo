using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PostApo.District
{
    public sealed class Position
    {
        public float x;
        public float y;
        public float z;

        public Position() { }

        public Position(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }

        public Vector3 ToVector3() { return new Vector3(x, y, z); }
    }

    public sealed class Member
    {
        public string steamId = "";
        public string name = "";

        public int characterId;

        public int gradeId = 1;
        public long joinedAt;
    }

    public sealed class PlayerOverride
    {
        public List<string> permissions = new List<string>();

        public Dictionary<string, Dictionary<string, bool>> terrains =
            new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, bool>> vehicules =
            new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DistrictCraftPoint
    {
        public int id;
        public string specialite = "";
        public Position position = new Position();

        public bool membersOnly = true;
    }

    public sealed class District
    {
        public int id;
        public string name = "";
        public string description = "";

        public List<string> specialites = new List<string>();

        public string ownerSteamId = "";
        public int ownerCharacterId;

        public Position baseSpawn;

        public List<Member> members = new List<Member>();
        public List<Grade> grades = Grade.DefaultSet();
        public List<DistrictCraftPoint> craftPoints = new List<DistrictCraftPoint>();

        public Dictionary<string, PlayerOverride> playerOverrides =
            new Dictionary<string, PlayerOverride>(StringComparer.OrdinalIgnoreCase);

        public int defaultGradeId = 1;

        public bool HasBase { get { return baseSpawn != null; } }

        public Member FindMember(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || members == null) { return null; }
            var wanted = steamId.Trim();
            return members.FirstOrDefault(m => m != null && string.Equals(m.steamId, wanted, StringComparison.OrdinalIgnoreCase));
        }

        public Grade FindGrade(int gradeId)
        {
            return grades == null ? null : grades.FirstOrDefault(g => g != null && g.id == gradeId);
        }

        public Grade GradeOf(string steamId)
        {
            var member = FindMember(steamId);
            if (member == null) { return null; }

            return FindGrade(member.gradeId) ?? LowestGrade();
        }

        public Grade LowestGrade()
        {
            return grades == null || grades.Count == 0 ? null : grades.OrderBy(g => g.rank).First();
        }

        public Grade HighestGrade()
        {
            return grades == null || grades.Count == 0 ? null : grades.OrderByDescending(g => g.rank).First();
        }

        public bool IsOwner(string steamId)
        {
            return !string.IsNullOrWhiteSpace(steamId)
                   && string.Equals(ownerSteamId, steamId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public bool HasSpecialite(string specialite)
        {
            if (string.IsNullOrWhiteSpace(specialite) || specialites == null) { return false; }
            return specialites.Any(s => string.Equals(s, specialite.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public int NextCraftPointId()
        {
            return craftPoints == null || craftPoints.Count == 0 ? 1 : craftPoints.Max(p => p.id) + 1;
        }

        public void Normalize()
        {
            if (specialites == null) { specialites = new List<string>(); }
            if (members == null) { members = new List<Member>(); }
            if (craftPoints == null) { craftPoints = new List<DistrictCraftPoint>(); }
            if (playerOverrides == null)
            {
                playerOverrides = new Dictionary<string, PlayerOverride>(StringComparer.OrdinalIgnoreCase);
            }

            if (grades == null || grades.Count == 0) { grades = Grade.DefaultSet(); }

            foreach (var grade in grades)
            {
                if (grade.permissions == null) { grade.permissions = new List<string>(); }
                if (grade.terrains == null)
                {
                    grade.terrains = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
                }

                if (grade.vehicules == null)
                {
                    grade.vehicules = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
                }
            }

            if (FindGrade(defaultGradeId) == null)
            {
                var lowest = LowestGrade();
                defaultGradeId = lowest != null ? lowest.id : 1;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<Member>();
            foreach (var member in members)
            {
                if (member == null || string.IsNullOrWhiteSpace(member.steamId)) { continue; }
                if (!seen.Add(member.steamId.Trim())) { continue; }

                member.steamId = member.steamId.Trim();
                if (FindGrade(member.gradeId) == null) { member.gradeId = defaultGradeId; }
                cleaned.Add(member);
            }

            members = cleaned;
        }
    }
}
