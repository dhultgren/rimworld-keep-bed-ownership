using HarmonyLib;
using System.Reflection;
using Verse;

namespace KeepBedOwnership
{
    /// <summary>

    /// ##### Notes on which variables are used and for what #####
    /// Building_Bed->OwnersForReading (data actually from Building_Bed->CompAssignableToPawn->assignedPawns):
    ///     Bed text showing owners
    ///     Whether the bed is occupied or not to see if it's valid
    ///     Find pawns that need to lose ownership when a new pawn claims it
    /// Pawn->ownership->OwnedBed:
    ///     To see which bed the pawn owns for going to bed and various thoughts

    /// ThoughtUtility->RemovePositiveBedroomThoughts:
    ///     Resets bedroom related thoughts
    /// Toils_LayDown->ApplyBedThoughts:
    ///     Same as above but also applies new thoughts. Assumes the pawn is currently in bed.

    /// ##### Variable behavior with mod #####
    /// Building_Bed->OwnersForReading:
    ///     Contains the pawns that own the bed
    /// Pawn->ownership->OwnedBed:
    ///     The bed this pawn owns on their current map (or last map if they are in the world)
    /// The problem is that the game might expect pawns in OwnersForReading to have that bed as OwnedBed,
    /// but that isn't always the case with this mod.

    /// </summary>
    [StaticConstructorOnStartup]
    public class KeepBedOwnership
    {
        static KeepBedOwnership()
        {
            new Harmony("KeepBedOwnership").PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
