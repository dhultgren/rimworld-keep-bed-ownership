using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace KeepBedOwnership.Patch
{
    static class Helpers
    {
        public static List<Building_Bed> PawnBedsOnMap(Pawn ___pawn, Map map)
        {
            if (map == null) return new List<Building_Bed>();
            return map.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                .Select(t => t as Building_Bed)
                .Where(b => b.OwnersForReading.Contains(___pawn))
                .ToList();
        }

        public static void UnclaimBeds(Pawn pawn, IEnumerable<Building_Bed> beds, ref Building_Bed ___intOwnedBed)
        {
            foreach (var bed in beds)
            {
                bed.CompAssignableToPawn.ForceRemovePawn(pawn);
                if (pawn.ownership.OwnedBed == bed)
                {
                    ___intOwnedBed = null;
                }
            }
        }

        public static bool ShouldRunForPawn(Pawn pawn)
        {
            return pawn.IsColonistPlayerControlled || (pawn.IsColonist && pawn.Map == null && pawn.MapHeld == null);
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn), "AssigningCandidates", MethodType.Getter)]
    class PatchCompAssignableToPawn
    {
        static bool Prefix(ref IEnumerable<Pawn> __result, CompAssignableToPawn __instance)
        {
            var bed = __instance.parent as Building_Bed;
            if (bed == null || !bed.Spawned || bed.ForPrisoners) return true;

            // Allow selecting any colonist on permanent bases
            if (bed.Map.IsPlayerHome)
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
            if (!Helpers.ShouldRunForPawn(pawn)) return true;
            // This is only used to display pawn list, so use the pawn ownership on the current map instead of their current bed
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
            if (newBed.Medical
                || !Helpers.ShouldRunForPawn(___pawn)
                || (newBed.OwnersForReading.Contains(___pawn) && ___pawn.ownership.OwnedBed == newBed))
            {
                return true;
            }

            // Remove other pawn to make room in bed
            var pawn = ___pawn;
            if (newBed.OwnersForReading.Count == newBed.SleepingSlotsCount && !newBed.OwnersForReading.Any(p => p == pawn))
            {
                var pawnToRemove = newBed.OwnersForReading.Last();
                pawnToRemove.ownership.UnclaimBed();
            }

            // Unclaim bed if pawn already has one on the map of the new bed
            var pawnBeds = Helpers.PawnBedsOnMap(___pawn, newBed.Map);
            if (pawnBeds.Any())
            {
                Helpers.UnclaimBeds(___pawn, pawnBeds, ref ___intOwnedBed);
            }

            // Claim new bed
            newBed.CompAssignableToPawn.ForceAddPawn(___pawn);
            // ... but only assign it if the pawn is on the same map
            if (___pawn.Map == newBed.Map)
            {
                ___intOwnedBed = newBed;
                ThoughtUtility.RemovePositiveBedroomThoughts(___pawn);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_Ownership), "UnclaimBed")]
    class PatchUnclaimBed
    {
        static bool Prefix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            if (!Helpers.ShouldRunForPawn(___pawn)) return true;

            // Temporarily replace pawns owned bed on their map with the bed owned on the current map
            ClaimBedOnMapIfExists(___pawn, Find.CurrentMap, ref ___intOwnedBed);
            return true;
        }

        static void Postfix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            // Return pawn owned bed to their current map
            if (Helpers.ShouldRunForPawn(___pawn) && ___pawn.Map != null)
            {
                ClaimBedOnMapIfExists(___pawn, ___pawn.Map, ref ___intOwnedBed);
            }
        }

        private static void ClaimBedOnMapIfExists(Pawn ___pawn, Map map, ref Building_Bed ___intOwnedBed)
        {
            var pawnBedsOnMap = Helpers.PawnBedsOnMap(___pawn, map);
            if (pawnBedsOnMap.Any())
            {
                var bed = pawnBedsOnMap.First();
                ___intOwnedBed = bed;
                if (!bed.CompAssignableToPawn.AssignedPawnsForReading.Contains(___pawn))
                {
                    bed.CompAssignableToPawn.ForceAddPawn(___pawn);
                }
                ThoughtUtility.RemovePositiveBedroomThoughts(___pawn);
            }
        }
    }
}
