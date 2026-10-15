using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA.DrugsMatter
{
    public class Mod_DrugsMatter : Mod
    {
        public static DrugTunerSettings Settings = null!;
        public static Harmony Harmony = null!;

        public Mod_DrugsMatter(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DrugTunerSettings>();
            Harmony = new Harmony("TSA.DrugsMatter");
            Harmony.PatchAll();
        }

        public override string SettingsCategory() => "Drugs Matter";

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }

    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup()
        {
            Mod_DrugsMatter.Settings.TryBootstrapFromDefs();
            DefsApplier.ApplyAll(Mod_DrugsMatter.Settings);
        }
    }
}
