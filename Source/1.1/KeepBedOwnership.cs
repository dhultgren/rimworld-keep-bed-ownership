using HarmonyLib;
using System.Reflection;
using Verse;

namespace KeepBedOwnership
{
    [StaticConstructorOnStartup]
    public class KeepBedOwnership
    {
        static KeepBedOwnership()
        {
            new Harmony("KeepBedOwnership").PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
