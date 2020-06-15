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
                if (pawn.ownership.OwnedBed != null && pawn.ownership.OwnedBed == bed)
                {
                    ___intOwnedBed = null;
                    ThoughtUtility.RemovePositiveBedroomThoughts(pawn);
                }
            }
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
            if (!pawn.IsColonistPlayerControlled) return true;
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
            if (newBed.Medical
                || !___pawn.IsColonistPlayerControlled
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

            // Unclaim bed if pawn already has one on the current map
            var pawnBeds = Helpers.PawnBedsOnMap(___pawn, Find.CurrentMap);
            if (pawnBeds.Any())
            {
                Helpers.UnclaimBeds(___pawn, pawnBeds, ref ___intOwnedBed);
            }

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
        static bool Prefix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            if (!___pawn.IsColonistPlayerControlled) return true;
            // Temporarily replace pawns owned bed on their map with the bed owned on the current map
            var pawnBedsOnMap = Helpers.PawnBedsOnMap(___pawn, Find.CurrentMap);
            if (pawnBedsOnMap.Any())
            {
                ___intOwnedBed = pawnBedsOnMap.First();
            }
            return false;
        }

        static void Postfix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            // Return pawn owned bed to their current map
            if (___pawn.IsColonistPlayerControlled && ___pawn.Map != null)
            {
                var pawnBedsOnMap = Helpers.PawnBedsOnMap(___pawn, ___pawn.Map);
                if (pawnBedsOnMap.Any())
                {
                    ___intOwnedBed = pawnBedsOnMap.First();
                }
            }
        }
    }
}
