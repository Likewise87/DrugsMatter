using System.Linq;
using RimWorld;
using Verse;

namespace TSA.DrugsMatter
{
    public static class DefsApplier
    {
        public static void ApplyAll(DrugTunerSettings settings)
        {
            foreach (var kv in settings.perDrug)
                ApplyOne(kv.Key, kv.Value);
        }

        public static void ApplyOne(string defName, DrugTunerSettings.DrugRow row)
        {
            var td = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (td == null || !td.IsDrug) return;

            var comp = td.GetCompProperties<CompProperties_Drug>();
            if (comp != null)
            {
                comp.addictiveness = Clamp01(row.addictionChance / 100f);
                comp.minToleranceToAddict = Clamp01(row.minToleranceToAddict / 100f);
                comp.largeOverdoseChance = Clamp01(row.largeOverdoseChance / 100f);
                comp.overdoseSeverityOffset = new FloatRange(row.overdoseMin, row.overdoseMax);
            }

            // Update tolerance outcome doer
            if (td.ingestible?.outcomeDoers != null)
            {
                foreach (var od in td.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>())
                {
                    if (od.toleranceChemical != null)
                    {
                        od.severity = row.toleranceGainPerDose;
                        break;
                    }
                }
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
