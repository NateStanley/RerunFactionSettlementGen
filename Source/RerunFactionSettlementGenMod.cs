using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RerunFactionSettlementGen
{
    public class RerunFactionSettlementGenSettings : ModSettings
    {
        public int playerBufferRadius = 10;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref playerBufferRadius, "playerBufferRadius", 10);
        }
    }

    public class RerunFactionSettlementGenMod : Mod
    {
        private const float RowHeight = 32f;
        private const float ButtonWidth = 144f;
        private const float CountColWidth = 64f;
        private const float LayerColWidth = 80f;

        private readonly RerunFactionSettlementGenSettings settings;
        private Vector2 scrollPosition;
        private bool showHidden;
        private string bufferEditBuffer;

        public RerunFactionSettlementGenMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<RerunFactionSettlementGenSettings>();
        }

        public override string SettingsCategory() => "MFS_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            if (Current.ProgramState != ProgramState.Playing || Find.World == null)
            {
                Widgets.Label(inRect, "MFS_LoadSaveFirst".Translate());
                return;
            }

            float y = inRect.y;

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            string intro = "MFS_Intro".Translate();
            float introHeight = Text.CalcHeight(intro, inRect.width);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, introHeight), intro);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += introHeight + 8f;

            string showHiddenLabel = "MFS_ShowHiddenFactions".Translate();
            float showHiddenWidth = Text.CalcSize(showHiddenLabel).x + 34f;
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, showHiddenWidth, 26f), showHiddenLabel, ref showHidden);
            y += 30f;

            string bufferLabel = "MFS_BufferLabel".Translate();
            float bufferLabelWidth = Text.CalcSize(bufferLabel).x;
            Widgets.Label(new Rect(inRect.x, y + 3f, bufferLabelWidth, 24f), bufferLabel);
            Widgets.TextFieldNumeric(new Rect(inRect.x + bufferLabelWidth + 10f, y, 64f, 28f), ref settings.playerBufferRadius, ref bufferEditBuffer, 0f, 500f);
            Rect bufferInfoRect = new Rect(inRect.x + bufferLabelWidth + 10f + 64f + 8f, y + 6f, 16f, 16f);
            GUI.DrawTexture(bufferInfoRect, TexButton.Info);
            if (Mouse.IsOver(bufferInfoRect))
            {
                Widgets.DrawHighlight(bufferInfoRect);
                TooltipHandler.TipRegion(bufferInfoRect, "MFS_BufferTooltip".Translate());
            }
            y += 34f;

            GUI.color = Color.gray;
            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            GUI.color = Color.white;
            y += 8f;

            Rect headerRow = new Rect(inRect.x, y, inRect.width - 20f, 20f);
            ColumnPositions(headerRow, out _, out float hCountX, out float hLayerX);
            float headerHeight = Text.LineHeight + 2f;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(headerRow.x + 34f, y, hLayerX - headerRow.x - 38f, headerHeight), "<b>" + "MFS_ColFaction".Translate() + "</b>");
            Widgets.Label(new Rect(hLayerX, y, LayerColWidth, headerHeight), "<b>" + "MFS_ColLayer".Translate() + "</b>");
            string basesLabel = "<b>" + "MFS_ColBases".Translate() + "</b>";
            Vector2 basesSize = Text.CalcSize(basesLabel);
            float basesRight = hCountX + CountColWidth;
            Rect basesLabelRect = new Rect(basesRight - basesSize.x, y, basesSize.x, headerHeight);
            Widgets.Label(basesLabelRect, basesLabel);
            GUI.color = Color.white;
            Rect infoRect = new Rect(basesLabelRect.x - 20f, y + (headerHeight - 16f) / 2f, 16f, 16f);
            GUI.DrawTexture(infoRect, TexButton.Info);
            Rect basesTipRect = new Rect(infoRect.x, y, basesRight - infoRect.x, headerHeight);
            if (Mouse.IsOver(basesTipRect))
            {
                Widgets.DrawHighlight(basesTipRect);
                TooltipHandler.TipRegion(basesTipRect, "MFS_BasesTooltip".Translate());
            }
            y += headerHeight + 4f;

            List<FactionRow> rows = BuildRows();
            List<FactionDef> missingDefs = FindMissingFactionDefs();
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);

            if (rows.Count == 0 && missingDefs.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(outRect, showHidden ? "MFS_NoFactions".Translate() : "MFS_NoFactionsTryHidden".Translate());
                GUI.color = Color.white;
                return;
            }

            float sectionHeaderHeight = Text.LineHeight + 18f;
            float viewHeight = rows.Count * RowHeight;
            if (missingDefs.Count > 0)
            {
                viewHeight += sectionHeaderHeight + missingDefs.Count * RowHeight;
            }
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                DrawFactionRow(rows[i], new Rect(0f, rowY, viewRect.width, RowHeight), i);
                rowY += RowHeight;
            }
            if (missingDefs.Count > 0)
            {
                GUI.color = Color.gray;
                Widgets.DrawLineHorizontal(0f, rowY + 6f, viewRect.width);
                Widgets.Label(new Rect(4f, rowY + 12f, viewRect.width - 8f, Text.LineHeight + 2f), "<b>" + "MFS_AbsentSection".Translate() + "</b>");
                GUI.color = Color.white;
                rowY += sectionHeaderHeight;
                for (int i = 0; i < missingDefs.Count; i++)
                {
                    DrawMissingDefRow(missingDefs[i], new Rect(0f, rowY, viewRect.width, RowHeight));
                    rowY += RowHeight;
                }
            }
            Widgets.EndScrollView();
        }

        private struct FactionRow
        {
            public Faction faction;
            public PlanetLayer layer;
            public int owned;
            public int target;
        }

        private List<FactionRow> BuildRows()
        {
            List<FactionRow> rows = new List<FactionRow>();
            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                if (faction.def.isPlayer || faction.temporary)
                {
                    continue;
                }
                if (faction.def.settlementGenerationWeight <= 0f)
                {
                    continue;
                }
                if (!showHidden && faction.Hidden)
                {
                    continue;
                }
                foreach (PlanetLayer layer in FactionBaseSpawner.EligibleLayers(faction.def))
                {
                    rows.Add(new FactionRow
                    {
                        faction = faction,
                        layer = layer,
                        owned = FactionBaseSpawner.OwnedOnLayer(layer, faction),
                        target = FactionBaseSpawner.FactionTargetOnLayer(layer, faction),
                    });
                }
            }
            return rows;
        }

        private static void ColumnPositions(Rect rowRect, out float buttonX, out float countX, out float layerX)
        {
            buttonX = rowRect.xMax - ButtonWidth - 6f;
            countX = buttonX - CountColWidth - 8f;
            layerX = countX - LayerColWidth - 8f;
        }

        private void DrawFactionRow(FactionRow row, Rect rect, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }
            Widgets.DrawHighlightIfMouseover(rect);

            ColumnPositions(rect, out float buttonX, out float countX, out float layerX);

            Rect iconRect = new Rect(rect.x + 4f, rect.y + 4f, 24f, 24f);
            FactionUIUtility.DrawFactionIconWithTooltip(iconRect, row.faction);

            float nameX = rect.x + 34f;
            Rect nameRect = new Rect(nameX, rect.y + 6f, layerX - nameX - 4f, 24f);
            Widgets.Label(nameRect, row.faction.Name.Truncate(nameRect.width));

            GUI.color = Color.gray;
            Widgets.Label(new Rect(layerX, rect.y + 6f, LayerColWidth, 24f), row.layer.Def.label);
            GUI.color = Color.white;

            Rect countRect = new Rect(countX, rect.y, CountColWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(countRect, row.owned + " / " + row.target);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(countRect))
            {
                TooltipHandler.TipRegion(countRect, "MFS_CountTooltip".Translate(
                    row.faction.Name, row.owned, row.layer.Def.label, row.target));
            }

            Rect buttonRect = new Rect(buttonX, rect.y + 3f, ButtonWidth, 26f);
            int missing = row.target - row.owned;
            if (missing <= 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(buttonRect, "MFS_NothingToAdd".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (Widgets.ButtonText(buttonRect, missing == 1 ? "MFS_SpawnOneBase".Translate() : "MFS_SpawnBases".Translate(missing)))
            {
                SpawnFor(row.faction, row.layer);
            }
        }

        private void SpawnFor(Faction faction, PlanetLayer layer)
        {
            try
            {
                SpawnReport report = FactionBaseSpawner.SpawnMissingBases(faction, layer, settings.playerBufferRadius);
                if (report.placed > 0 && report.shortfall == 0)
                {
                    Messages.Message("MFS_PlacedBases".Translate(report.placed, faction.Name, layer.Def.label),
                        MessageTypeDefOf.PositiveEvent, historical: false);
                }
                else
                {
                    string hint = settings.playerBufferRadius > 0 ? " " + "MFS_BufferHint".Translate() : "";
                    if (report.placed > 0)
                    {
                        Messages.Message("MFS_PlacedPartial".Translate(report.placed, faction.Name, report.shortfall) + hint,
                            MessageTypeDefOf.CautionInput, historical: false);
                    }
                    else
                    {
                        Messages.Message("MFS_PlacedNone".Translate(faction.Name) + hint,
                            MessageTypeDefOf.RejectInput, historical: false);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[Rerun Faction Settlement Gen] Failed spawning bases for " + faction + ": " + e);
                Messages.Message("MFS_SpawnError".Translate(),
                    MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        // defs that should have a live faction in this save (requiredCountAtGameStart > 0
        // and they generate settlements) but dont. usually some other mod removed it
        private List<FactionDef> FindMissingFactionDefs()
        {
            List<FactionDef> result = new List<FactionDef>();
            foreach (FactionDef def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (def.isPlayer || (def.hidden && !showHidden) || def.requiredCountAtGameStart <= 0 || def.settlementGenerationWeight <= 0f)
                {
                    continue;
                }
                bool anyLive = false;
                List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < factions.Count; i++)
                {
                    if (factions[i].def == def)
                    {
                        anyLive = true;
                        break;
                    }
                }
                if (!anyLive)
                {
                    result.Add(def);
                }
            }
            return result;
        }

        private void DrawMissingDefRow(FactionDef def, Rect rect)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            Rect iconRect = new Rect(rect.x + 4f, rect.y + 4f, 24f, 24f);
            if (def.FactionIcon != null)
            {
                GUI.DrawTexture(iconRect, def.FactionIcon);
            }
            float buttonX = rect.xMax - ButtonWidth - 6f;
            Rect nameRect = new Rect(rect.x + 34f, rect.y + 6f, buttonX - rect.x - 42f, 24f);
            Widgets.Label(nameRect, def.LabelCap.ToString().Truncate(nameRect.width));
            Rect buttonRect = new Rect(buttonX, rect.y + 3f, ButtonWidth, 26f);
            if (Widgets.ButtonText(buttonRect, "MFS_AddFaction".Translate()))
            {
                try
                {
                    PlanetLayer layer = Find.WorldGrid.Surface;
                    foreach (PlanetLayer eligible in FactionBaseSpawner.EligibleLayers(def))
                    {
                        layer = eligible;
                        break;
                    }
                    // basically FactionGenerator.CreateFactionAndAddToManager but generated
                    // hidden, because NewGeneratedFaction plants a seed settlement for visible
                    // factions and we dont want that. unhide after adding so the spawn button
                    // places every base itself
                    IdeoGenerationParms ideoParms = new IdeoGenerationParms(def, forceNoExpansionIdeo: false, null, null,
                        name: def.ideoName, styles: def.styles, deities: def.deityPresets, hidden: def.hiddenIdeo,
                        description: def.ideoDescription, forcedMemes: def.forcedMemes, classicExtra: false,
                        forceNoWeaponPreference: false, forNewFluidIdeo: false, fixedIdeo: def.fixedIdeo,
                        requiredPreceptsOnly: def.requiredPreceptsOnly);
                    Faction faction = FactionGenerator.NewGeneratedFaction(layer, new FactionGeneratorParms(def, ideoParms, true));
                    Find.FactionManager.Add(faction);
                    faction.hidden = false;
                    Messages.Message("MFS_FactionAdded".Translate(def.LabelCap),
                        MessageTypeDefOf.PositiveEvent, historical: false);
                }
                catch (Exception e)
                {
                    Log.Error("[Rerun Faction Settlement Gen] Failed adding faction " + def + ": " + e);
                    Messages.Message("MFS_AddError".Translate(),
                        MessageTypeDefOf.RejectInput, historical: false);
                }
            }
        }
    }
}
