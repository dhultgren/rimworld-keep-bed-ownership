using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace KeepBedOwnership.Patch
{
    static class Helpers
    {
        public static List<Building_Bed> PawnBedsOnMap(Pawn pawn, Map map)
        {
            var mapsOnThisTile = Find.Maps.Where(m => m.Tile == map.Tile);
            return mapsOnThisTile.SelectMany(m => m.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                .Select(t => t as Building_Bed)
                .Where(b => b.OwnersForReading.Contains(pawn)))
                .ToList();
        }

        public static void UnclaimBeds(Pawn pawn, IEnumerable<Building_Bed> beds, ref Building_Bed ___intOwnedBed)
        {
            foreach(var bed in beds)
            {
                bed.CompAssignableToPawn.ForceRemovePawn(pawn);
                if (pawn.ownership.OwnedBed != null && pawn.ownership.OwnedBed == bed)
                {
                    ___intOwnedBed = null;
                    ThoughtUtility.RemovePositiveBedroomThoughts(pawn);
                }
            }
        }

        // Compatibility for Z-Levels mod
        public static bool IsZLevel(Map map)
        {
            return !map.IsPlayerHome
                && !map.IsTempIncidentMap
                && map?.ParentFaction == null;
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn), "AssigningCandidates", MethodType.Getter)]
    class PatchCompAssignableToPawn
    {
        static bool Prefix(ref IEnumerable<Pawn> __result, CompAssignableToPawn __instance)
        {
            var bed = __instance.parent as Building_Bed;
            if (bed == null || !bed.Spawned || bed.ForPrisoners) return true;

            // Allow selecting any colonist on permanent bases, including Z-levels
            if (bed.Map.IsPlayerHome || Helpers.IsZLevel(bed.Map))
            {
                __result = Find.ColonistBar.GetColonistsInOrder(); // Doesn't feel like the correct way to do this
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), "AssignedAnything")]
    class PatchCompAssignableToPawn_Bed
    {
        static bool Prefix(ref bool __result, CompAssignableToPawn_Bed __instance, Pawn pawn)
        {
            // Check if the pawn has any bed on this map instead of only checking their current bed
            var pawnBeds = Helpers.PawnBedsOnMap(pawn, Find.CurrentMap);
            __result = pawnBeds.Any();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_Ownership), "ClaimBedIfNonMedical")]
    class PatchClaimBedIfNonMedical
    {
        static bool Prefix(Building_Bed newBed, Pawn_Ownership __instance, ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            if (newBed.Medical || (newBed.OwnersForReading.Contains(___pawn) && ___pawn.ownership.OwnedBed == newBed))
            {
                return true;
            }

            // Remove other pawn to make room in bed
            var pawn = ___pawn;
            if (newBed.OwnersForReading.Count == newBed.SleepingSlotsCount && !newBed.OwnersForReading.Any(p => p == pawn))
            {
                var pawnToRemove = newBed.OwnersForReading[newBed.OwnersForReading.Count - 1];
                pawnToRemove.ownership.UnclaimBed();
            }

            // Unclaim beds if pawn already has any on the current map, or Z-levels of it
            var pawnBeds = Helpers.PawnBedsOnMap(___pawn, Find.CurrentMap);
            Helpers.UnclaimBeds(___pawn, pawnBeds, ref ___intOwnedBed);

            // Claim new bed
            newBed.CompAssignableToPawn.ForceAddPawn(___pawn);
            ___intOwnedBed = newBed;
            ThoughtUtility.RemovePositiveBedroomThoughts(___pawn);

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_Ownership), "UnclaimBed")]
    class PatchUnclaimBed
    {
        static void Prefix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            // Temporarily replace pawns owned bed on their map with the bed owned on the current map
            var pawnBedsOnMap = Helpers.PawnBedsOnMap(___pawn, Find.CurrentMap);
            if (pawnBedsOnMap.Any())
            {
                ___intOwnedBed = pawnBedsOnMap.First();
            }
        }

        static void Postfix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            // Return pawn owned bed to their current map
            if (___pawn.Map != null)
            {
                var pawnBedsOnMap = Helpers.PawnBedsOnMap(___pawn, ___pawn.Map);
                if (pawnBedsOnMap.Any())
                {
                    ___intOwnedBed = pawnBedsOnMap.First();
                }
            }
        }
    }

    /*[HarmonyPatch(typeof(MapPawns), "RegisterPawn")]
    class PatchRegisterPawn
    {
        static void Postfix(ref Pawn p)
        {
            // If a pawn enters a map where they own a bed, claim it
            var pawnBeds = Helpers.PawnBedsOnMap(p, p.Map);
            if (pawnBeds.Count() == 1)
            {
                var bed = pawnBeds[0];
                if (!bed.OwnersForReading.Contains(p) || p.ownership.OwnedBed != bed || !bed.CompAssignableToPawn.AssignedPawns.Contains(p))
                {
                    p.ownership.ClaimBedIfNonMedical(bed);
                }
            }
        }
    }*/
}
