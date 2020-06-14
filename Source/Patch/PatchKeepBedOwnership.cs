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
            return map.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                .Select(t => t as Building_Bed)
                .Where(b => b.OwnersForReading.Contains(___pawn))
                .ToList();
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
            if (newBed.OwnersForReading.Count == newBed.SleepingSlotsCount)
            {
                var pawnToRemove = newBed.OwnersForReading[newBed.OwnersForReading.Count - 1];
                pawnToRemove.ownership.UnclaimBed();
            }

            // Unclaim bed if pawn already has one on the current map
            var pawnBeds = Helpers.PawnBedsOnMap(___pawn, Find.CurrentMap);
            if (pawnBeds.Any())
            {
                __instance.UnclaimBed();
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

    [HarmonyPatch(typeof(MapPawns), "RegisterPawn")]
    class PatchRegisterPawn
    {
        static void Postfix(ref Pawn p)
        {
            // Ignore calls during game load
            if (p.Map != Find.CurrentMap) return;

            // If a pawn enters a map where they own a bed, claim it
            var pawnBeds = Helpers.PawnBedsOnMap(p, Find.CurrentMap);
            if (pawnBeds.Any())
            {
                p.ownership.ClaimBedIfNonMedical(pawnBeds.First());
            }
        }
    }
}
