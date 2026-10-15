using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.DrugsMatter
{
    public class DrugTunerSettings : ModSettings
    {
        // --- layout constants (single source of truth) ---
        private const float RowBoxHeight = 130f;   // height of the bordered row box
        private const float RowGap = 10f;    // gap under each row
        private const float RowTotal = RowBoxHeight + RowGap; // total vertical step per row

        // Live, user-editable values shown in the UI
        public Dictionary<string, DrugRow> perDrug = new Dictionary<string, DrugRow>();

        // Baseline snapshot captured on first bootstrap (used for "true reset")
        public Dictionary<string, DrugRow> bootDefaults = new Dictionary<string, DrugRow>();

        private Vector2 scroll;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref perDrug, "perDrug", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref bootDefaults, "bootDefaults", LookMode.Value, LookMode.Deep);
        }

        public void TryBootstrapFromDefs()
        {
            // robust drug filter (avoids edge cases with ThingDef.IsDrug)
            bool IsDrugThing(ThingDef td) =>
                td?.ingestible != null && td.ingestible.drugCategory != DrugCategory.None;

            var drugDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsDrugThing)
                .ToList();

            // Build snapshot once (if empty)
            if (bootDefaults == null) bootDefaults = new Dictionary<string, DrugRow>();
            if (bootDefaults.Count == 0)
            {
                foreach (var td in drugDefs)
                    bootDefaults[td.defName] = DrugRow.FromThingDef(td);
            }

            // Initialize perDrug from snapshot if missing; otherwise keep user edits
            foreach (var td in drugDefs)
            {
                if (!perDrug.ContainsKey(td.defName))
                {
                    var row = bootDefaults.TryGetValue(td.defName, out var snap)
                        ? snap.Clone()
                        : DrugRow.FromThingDef(td);
                    perDrug[td.defName] = row;
                }
            }

            // Clean up entries for removed defs
            var live = new HashSet<string>(drugDefs.Select(d => d.defName));
            perDrug.Keys.Where(k => !live.Contains(k)).ToList().ForEach(k => perDrug.Remove(k));
            bootDefaults.Keys.Where(k => !live.Contains(k)).ToList().ForEach(k => bootDefaults.Remove(k));
        }

        public void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Single reset button (snapshot)
            if (listing.ButtonText("Reset to Default"))
            {
                ResetToBootDefaults();
                DefsApplier.ApplyAll(this);
                Messages.Message("DrugsMatter: Restored values to first-seen defaults.", MessageTypeDefOf.TaskCompletion, false);
            }

            listing.Gap();

            // Scroll container
            Rect outRect = listing.GetRect(inRect.height - 100f);

            // Total inner height uses a single constant per row
            Rect viewRect = new Rect(
                0, 0,
                outRect.width - 16f,
                perDrug.Count * RowTotal + 20f // tail padding so last row isn't cramped
            );

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            var inner = new Listing_Standard();
            inner.Begin(viewRect);

            foreach (var kv in perDrug.OrderBy(k => DefDatabase<ThingDef>.GetNamedSilentFail(k.Key)?.label ?? k.Key))
            {
                var defName = kv.Key;
                var row = kv.Value;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);

                // fixed-height row box
                var rowRect = inner.GetRect(RowBoxHeight);
                Widgets.DrawBox(rowRect);
                var left = rowRect.ContractedBy(6f);

                // header (show defName only if different from label)
                string header = def != null
                    ? (def.LabelCap != def.defName ? $"{def.LabelCap} ({def.defName})" : def.LabelCap)
                    : defName;
                Widgets.Label(new Rect(left.x, left.y, left.width, 22f), header);

                // column layout inside the row
                float y = left.y + 28f;
                float colW = left.width / 5f;

                // === Addiction chance (%)
                Rect addBlock = new Rect(left.x + colW * 0, y, colW - 6f, 60f);
                Widgets.Label(new Rect(addBlock.x, addBlock.y, addBlock.width, 24f),
                    $"Addiction risk: {row.addictionChance:0.00}%");
                Rect addSlider = new Rect(addBlock.x, addBlock.y + 26f, addBlock.width, 34f);
                row.addictionChance = Widgets.HorizontalSlider(addSlider, row.addictionChance, 0f, 20f, true);
                TooltipHandler.TipRegion(addBlock,
                    "Chance per dose to trigger an addiction.\nRolled every time the pawn consumes the drug.\nExample: Yayo = 1%, Flake = 5%.");

                // === Min tolerance (%)
                Rect minBlock = new Rect(left.x + colW * 1, y, colW - 6f, 60f);
                Widgets.Label(new Rect(minBlock.x, minBlock.y, minBlock.width, 24f),
                    $"Min tolerance: {row.minToleranceToAddict:0}%");
                Rect minSlider = new Rect(minBlock.x, minBlock.y + 26f, minBlock.width, 34f);
                row.minToleranceToAddict = Widgets.HorizontalSlider(minSlider, row.minToleranceToAddict, 0f, 75f, true);
                TooltipHandler.TipRegion(minBlock,
                    "Minimum tolerance before addiction rolls can occur.\nBeer = 25%, Ambrosia = 20%, most others = 0%.");

                // === Large overdose chance (%)
                Rect lodBlock = new Rect(left.x + colW * 2, y, colW - 6f, 60f);
                Widgets.Label(new Rect(lodBlock.x, lodBlock.y, lodBlock.width, 24f),
                    $"OD risk: {row.largeOverdoseChance:0.00}%");
                Rect lodSlider = new Rect(lodBlock.x, lodBlock.y + 26f, lodBlock.width, 34f);
                row.largeOverdoseChance = Widgets.HorizontalSlider(lodSlider, row.largeOverdoseChance, 0f, 5f, true);
                TooltipHandler.TipRegion(lodBlock,
                    "Chance per dose to cause a large overdose.\nLarge ODs jump severity to Major/Extreme and can kill instantly.");

                // === Overdose severity offset (dual-handle range slider)
                FloatRange range = new FloatRange(row.overdoseMin, row.overdoseMax);
                Rect odBlock = new Rect(left.x + colW * 3, y, colW - 6f, 60f);
                Rect odLabel = new Rect(odBlock.x, odBlock.y, odBlock.width, 24f);
                Widgets.Label(odLabel, $"OD Severity: {row.overdoseMin:0.00} – {row.overdoseMax:0.00}");
                Rect odSlider = new Rect(odBlock.x, odBlock.y + 26f, odBlock.width, 34f);
                Widgets.FloatRange(odSlider, (int)defName.GetHashCode(), ref range, 0f, 1f);
                row.overdoseMin = range.min;
                row.overdoseMax = range.max;
                TooltipHandler.TipRegion(odBlock,
                    "Amount of overdose severity added per dose.\nMinor OD ~0.15, Major 0.6, Extreme 1.0.\nExample: Yayo adds 0.18–0.35.");

                // === Tolerance gain per dose (0–1 universal)
                Rect tolBlock = new Rect(left.x + colW * 4, y, colW - 6f, 60f);
                Widgets.Label(new Rect(tolBlock.x, tolBlock.y, tolBlock.width, 24f),
                    $"Tolerance/Dose: {row.toleranceGainPerDose:0.000}");
                Rect tolSlider = new Rect(tolBlock.x, tolBlock.y + 26f, tolBlock.width, 34f);
                row.toleranceGainPerDose = Widgets.HorizontalSlider(tolSlider, row.toleranceGainPerDose, 0f, 1f, true);
                TooltipHandler.TipRegion(tolBlock,
                    "Tolerance gain per dose.\nPsychite ~0.04, Beer ~0.12, Ambrosia 0.5.\nHigher values increase tolerance faster and reduce drug effects.");

                // Auto-apply immediately
                DefsApplier.ApplyOne(defName, row);

                // fixed gap under the row
                inner.Gap(RowGap);
            }

            inner.End();
            Widgets.EndScrollView();
            listing.End();

            Write();
        }

        private void ResetToBootDefaults()
        {
            foreach (var kv in bootDefaults)
                perDrug[kv.Key] = kv.Value.Clone();
        }

        // ==== Data containers ====
        public class DrugRow : IExposable
        {
            public string defName = "";

            public float addictionChance;       // % for UI
            public float minToleranceToAddict;  // % for UI
            public float largeOverdoseChance;   // % for UI
            public float overdoseMin;           // FloatRange.min
            public float overdoseMax;           // FloatRange.max
            public float toleranceGainPerDose;  // from outcome doer

            public void ExposeData()
            {
                Scribe_Values.Look(ref defName, "defName");
                Scribe_Values.Look(ref addictionChance, "addictionChance");
                Scribe_Values.Look(ref minToleranceToAddict, "minToleranceToAddict");
                Scribe_Values.Look(ref largeOverdoseChance, "largeOverdoseChance");
                Scribe_Values.Look(ref overdoseMin, "overdoseMin");
                Scribe_Values.Look(ref overdoseMax, "overdoseMax");
                Scribe_Values.Look(ref toleranceGainPerDose, "toleranceGainPerDose");
            }

            public DrugRow Clone() => new DrugRow
            {
                defName = defName,
                addictionChance = addictionChance,
                minToleranceToAddict = minToleranceToAddict,
                largeOverdoseChance = largeOverdoseChance,
                overdoseMin = overdoseMin,
                overdoseMax = overdoseMax,
                toleranceGainPerDose = toleranceGainPerDose
            };

            public static DrugRow FromThingDef(ThingDef td)
            {
                var comp = td.GetCompProperties<CompProperties_Drug>();
                FloatRange odRange = comp?.overdoseSeverityOffset ?? new FloatRange(0f, 0f);

                float tolGain = 0f;
                if (td.ingestible?.outcomeDoers != null)
                {
                    foreach (var od in td.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_GiveHediff>())
                    {
                        if (od.toleranceChemical != null)
                        {
                            tolGain = od.severity;
                            break;
                        }
                    }
                }

                return new DrugRow
                {
                    defName = td.defName,
                    addictionChance = (comp?.addictiveness ?? 0f) * 100f,
                    minToleranceToAddict = (comp?.minToleranceToAddict ?? 0f) * 100f,
                    largeOverdoseChance = (comp?.largeOverdoseChance ?? 0f) * 100f,
                    overdoseMin = odRange.min,
                    overdoseMax = odRange.max,
                    toleranceGainPerDose = tolGain
                };
            }
        }
    }
}
