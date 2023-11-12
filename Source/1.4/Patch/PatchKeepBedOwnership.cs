﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Verse;

namespace KeepBedOwnership.Patch
{
    static class Helpers
    {
        public static List<Building_Bed> PawnBedsOnMap(Pawn ___pawn, Map map)
        {
            return map?.listerThings?.ThingsInGroup(ThingRequestGroup.Bed)
                .Select(t => t as Building_Bed)
                .Where(b => b?.OwnersForReading != null && b.OwnersForReading.Contains(___pawn))
                .ToList() ?? new List<Building_Bed>();
        }

        public static void UnclaimBeds(Pawn pawn, IEnumerable<Building_Bed> beds, ref Building_Bed ___intOwnedBed)
        {
            foreach (var bed in beds)
            {
                bed?.CompAssignableToPawn?.ForceRemovePawn(pawn);
                if (pawn.ownership?.OwnedBed == bed)
                {
                    ___intOwnedBed = null;
                }
            }
        }

        public static bool ShouldRunForPawn(Pawn pawn)
        {
            return pawn != null && pawn.IsFreeColonist && !pawn.Dead;
        }

        public static bool ShouldRunForBed(Building_Bed bed)
        {
            if (bed == null || !bed.Spawned || bed.ForPrisoners || bed.Map == null || bed.Medical) return false;
            if (bed.GetType().ToString().Contains("WhatTheHack")) return false;
            return true;
        }
    }

    // Normally the game removes ownership of beds if pawn.ownership doesn't reflect the ownership. This patch stops that.
    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), "PostExposeData")]
    class PatchCompAssignableToPawn_Bed_PostExposeData
    {
        static bool Prefix(CompAssignableToPawn_Bed __instance, ref List<Pawn> ___assignedPawns, ThingWithComps ___parent)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return true;

            var unreciprocatedOwners = ___assignedPawns
                .Where(p => p?.ownership?.OwnedBed != ___parent)
                .ToList();
            if (unreciprocatedOwners.Count > 0)
            {
                if (unreciprocatedOwners.Any(p => p.IsFreeColonist))
                {
                    return false;
                }
                ___assignedPawns.RemoveAll(p => p?.IsFreeColonist == false && unreciprocatedOwners.Contains(p));
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn), "AssigningCandidates", MethodType.Getter)]
    class PatchCompAssignableToPawn
    {
        static bool Prefix(ref IEnumerable<Pawn> __result, CompAssignableToPawn __instance)
        {
            var bed = __instance.parent as Building_Bed;
            if (!Helpers.ShouldRunForBed(bed)) return true;

            // Allow selecting any colonist on permanent bases
            if (bed.Map.IsPlayerHome)
            {
                __result = Find.ColonistBar.GetColonistsInOrder();
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
            if (newBed == null
                || !Helpers.ShouldRunForPawn(___pawn)
                || !Helpers.ShouldRunForBed(newBed)
                || (newBed.OwnersForReading != null && newBed.OwnersForReading.Contains(___pawn) && ___pawn.ownership?.OwnedBed == newBed))
            {
                return true;
            }

            // Remove other pawn to make room in bed
            var pawn = ___pawn;
            if (newBed.OwnersForReading?.Count == newBed.SleepingSlotsCount
                && !newBed.OwnersForReading.Any(p => p == pawn)
                && newBed.OwnersForReading.Count > 0)
            {
                var pawnToRemove = newBed.OwnersForReading.LastOrDefault();
                pawnToRemove?.ownership?.UnclaimBed();
            }

            // Unclaim bed if pawn already has one on the map of the new bed
            var pawnBeds = Helpers.PawnBedsOnMap(___pawn, newBed.Map);
            pawnBeds.RemoveAll(p => p.def.defName == "DeathrestCasket");
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

    // Make sure pawns pick their current beds when looking for a normal bed to sleep
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.FindBedFor))]
    [HarmonyPatch(new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(bool), typeof(GuestStatus?) })]
    class PatchFindBedFor
    {
        static void Postfix(Pawn sleeper, Pawn traveler, bool checkSocialProperness, bool ignoreOtherReservations, GuestStatus? guestStatus, ref Building_Bed __result)
        {
            if (__result == null || __result.Medical || !Helpers.ShouldRunForPawn(sleeper)) return;
            var currentBeds = Helpers.PawnBedsOnMap(sleeper, sleeper.Map);
            //Log.Message("Pre-check bed count: " + currentBeds.Count.ToString());
            currentBeds = currentBeds
                .Where(bed =>
                    sleeper.Map.reachability.CanReach(sleeper.Position, new LocalTargetInfo(bed), Verse.AI.PathEndMode.OnCell, TraverseParms.For(sleeper))
                    && ForbidUtility.InAllowedArea(bed.Position, sleeper))
                .ToList();
            //Log.Message("Post-check bed count: " + currentBeds.Count.ToString());
            if (currentBeds.Count > 0)
            {
                __result = currentBeds[0];
            }
        }
    }

    // Temporarily replace pawns owned bed on their map with the bed owned on the current map in order to
    // unclaim the correct bed
    [HarmonyPatch(typeof(Pawn_Ownership), "UnclaimBed")]
    class PatchUnclaimBed
    {
        static bool Prefix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            UnassignBedsForOldPawns(___pawn);
            if (!Helpers.ShouldRunForPawn(___pawn)) return true;

            var isTeleporting = ___pawn.teleporting;
            var isInShuttle = !___pawn.Spawned && ___pawn.SpawnedParentOrMe?.Label?.Contains("shuttle") == true;
            if (isInShuttle || isTeleporting)
            {
                ___intOwnedBed = null;
                return true;
            }

            // NOTE: If the bed is unclaimed (typically deconstructed/replaced) on another map this will cause the pawn
            // to unclaim the bed on CurrentMap. Since UnclaimBed doesn't specify bed we have to guess, and since it's
            // called from a bunch of places in vanilla (plus whatever from mods) I'd rather just take the occasional
            // unwanted unclaim instead of trying to patch everywhere.

            // Temporarily replace pawns owned bed on their map with the bed owned on the current map
            ClaimBedOnMapIfExists(___pawn, Find.CurrentMap, ref ___intOwnedBed);
            return true;
        }

        static void Postfix(ref Pawn ___pawn, ref Building_Bed ___intOwnedBed)
        {
            var pawnMap = ___pawn.Map ?? ___pawn.MapHeld ?? ___pawn.SpawnedParentOrMe?.Map;
            // Return pawn owned bed to their current map
            if (Helpers.ShouldRunForPawn(___pawn) && pawnMap != null)
            {
                ClaimBedOnMapIfExists(___pawn, pawnMap, ref ___intOwnedBed);
            }
        }

        private static void ClaimBedOnMapIfExists(Pawn ___pawn, Map map, ref Building_Bed ___intOwnedBed)
        {
            var pawnBedsOnMap = Helpers.PawnBedsOnMap(___pawn, map);
            var bed = pawnBedsOnMap.FirstOrDefault();
            if (bed != null)
            {
                ___intOwnedBed = bed;
                if (bed.CompAssignableToPawn != null && !bed.CompAssignableToPawn.AssignedPawnsForReading.Contains(___pawn))
                {
                    bed.CompAssignableToPawn.ForceAddPawn(___pawn);
                }
            }
        }

        private static void UnassignBedsForOldPawns(Pawn pawn)
        {
            if (pawn == null) return;
            var isDead = pawn.Dead;
            var isDespawnedNonColonist = !pawn.IsFreeColonist && !pawn.Spawned;
            var isImprisonedColonist = pawn.IsColonist && pawn.IsPrisoner;
            if (isDead || isDespawnedNonColonist || isImprisonedColonist)
            {
                var pawnBeds = Find.Maps.SelectMany(map => Helpers.PawnBedsOnMap(pawn, map));
                Building_Bed noBed = null;
                Helpers.UnclaimBeds(pawn, pawnBeds, ref noBed);
            }
        }
    }
}