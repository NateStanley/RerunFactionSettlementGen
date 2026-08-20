using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RerunFactionSettlementGen
{
    public struct SpawnReport
    {
        public int placed;
        public int shortfall;
    }

    public static class FactionBaseSpawner
    {
        // copy of FactionGenerator.CanExistOnLayer since that one's private. whitelist
        // wins outright if set, otherwise only the root surface layer counts
        public static bool CanExistOnLayer(PlanetLayer layer, FactionDef f)
        {
            if (!f.layerBlacklist.NullOrEmpty() && f.layerBlacklist.Contains(layer.Def))
            {
                return false;
            }
            if (!f.layerWhitelist.NullOrEmpty())
            {
                return f.layerWhitelist.Contains(layer.Def);
            }
            return layer.IsRootSurface;
        }

        public static IEnumerable<PlanetLayer> EligibleLayers(FactionDef f)
        {
            foreach (KeyValuePair<int, PlanetLayer> kv in Find.WorldGrid.PlanetLayers)
            {
                if (CanExistOnLayer(kv.Value, f))
                {
                    yield return kv.Value;
                }
            }
        }

        // who gets into the vanilla weighted settlement pool - see the Validator in
        // FactionGenerator.GenerateFactionsIntoWorldLayer
        public static bool IsVanillaSettlementFaction(Faction f)
        {
            return !f.def.isPlayer && !f.Hidden && !f.temporary;
        }

        // total settlements the layer wants. same formula as
        // FactionGenerator.GenerateFactionsIntoWorldLayer except vanilla rolls
        // settlementsPer100kTiles.RandomInRange, we take the average instead so the
        // number in the ui doesnt jump around every redraw
        public static int LayerTargetSettlementCount(PlanetLayer layer)
        {
            float viewFactor = layer.Def.viewAngleSettlementsFactorCurve.Evaluate(Mathf.Clamp01(layer.ViewAngle / 180f));
            float per100kTiles = layer.Def.settlementsPer100kTiles.Average;
            float popScale = Find.World.info.overallPopulation.GetScaleFactor();
            return Mathf.RoundToInt((float)layer.TilesCount / 100000f * per100kTiles * popScale * viewFactor);
        }

        // the factions cut of the layer target. vanilla hands out each settlement by
        // weighted random over settlementGenerationWeight so expected count is just
        // target * weight / totalWeight
        public static int FactionTargetOnLayer(PlanetLayer layer, Faction faction)
        {
            if (faction.def.settlementGenerationWeight <= 0f)
            {
                return 0;
            }
            float totalWeight = 0f;
            bool factionInPool = false;
            List<Faction> allFactions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < allFactions.Count; i++)
            {
                Faction other = allFactions[i];
                if (IsVanillaSettlementFaction(other) && CanExistOnLayer(layer, other.def))
                {
                    totalWeight += other.def.settlementGenerationWeight;
                    if (other == faction)
                    {
                        factionInPool = true;
                    }
                }
            }
            // hidden factions etc arent in the vanilla pool, but if we're force spawning
            // one it still has to share the layer target with everyone else
            if (!factionInPool)
            {
                totalWeight += faction.def.settlementGenerationWeight;
            }
            if (totalWeight <= 0f)
            {
                return 0;
            }
            float share = faction.def.settlementGenerationWeight / totalWeight;
            return Mathf.RoundToInt(LayerTargetSettlementCount(layer) * share);
        }

        public static int OwnedOnLayer(PlanetLayer layer, Faction faction)
        {
            int count = 0;
            List<WorldObject> settlements = Find.WorldObjects.AllSettlementsOnLayer(layer);
            for (int i = 0; i < settlements.Count; i++)
            {
                if (settlements[i].Faction == faction)
                {
                    count++;
                }
            }
            return count;
        }

        public static int MissingOnLayer(PlanetLayer layer, Faction faction)
        {
            return FactionTargetOnLayer(layer, faction) - OwnedOnLayer(layer, faction);
        }

        // throws out tiles within bufferRadius of a player settlement. radius is in
        // root-surface world tiles. tile centers get normalized to directions from planet
        // center before comparing, a surface base blocks the orbit tiles right above it too
        public static Predicate<PlanetTile> PlayerBufferValidator(PlanetLayer layer, int bufferRadius)
        {
            if (bufferRadius <= 0)
            {
                return null;
            }
            List<Vector3> playerBaseDirs = new List<Vector3>();
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (settlement.Faction != null && settlement.Faction.IsPlayer && settlement.Tile.Valid)
                {
                    playerBaseDirs.Add(settlement.Tile.Layer.GetTileCenter(settlement.Tile).normalized);
                }
            }
            if (playerBaseDirs.Count == 0)
            {
                return null;
            }
            PlanetLayer surface = Find.WorldGrid.Surface;
            return delegate(PlanetTile tile)
            {
                Vector3 dir = layer.GetTileCenter(tile).normalized;
                for (int i = 0; i < playerBaseDirs.Count; i++)
                {
                    if (surface.ApproxDistanceInTiles(GenMath.SphericalDistance(dir, playerBaseDirs[i])) < bufferRadius)
                    {
                        return false;
                    }
                }
                return true;
            };
        }

        // same per-settlement steps as the loop body in
        // FactionGenerator.GenerateFactionsIntoWorldLayer ... the layers own settlement
        // worldobject def (SpaceSettlement on orbit) plus the layer aware tile finder
        public static SpawnReport SpawnMissingBases(Faction faction, PlanetLayer layer, int playerBufferRadius)
        {
            SpawnReport report = default;
            Predicate<PlanetTile> bufferValidator = PlayerBufferValidator(layer, playerBufferRadius);
            int missing = MissingOnLayer(layer, faction);
            for (int i = 0; i < missing; i++)
            {
                PlanetTile tile = TileFinder.RandomSettlementTileFor(layer, faction, mustBeAutoChoosable: false, bufferValidator);
                // ! when the finder runs out of candidates it logs an error and just returns tile 0 of the layer WITHOUT running the validators.
                // check everything again ourselves before trusting it
                if (!tile.Valid || !TileFinder.IsValidTileForNewSettlement(tile)
                    || (bufferValidator != null && !bufferValidator(tile)))
                {
                    report.shortfall = missing - report.placed;
                    break;
                }
                WorldObject worldObject = WorldObjectMaker.MakeWorldObject(layer.Def.SettlementWorldObjectDef);
                worldObject.SetFaction(faction);
                worldObject.Tile = tile;
                if (worldObject is INameableWorldObject nameable)
                {
                    nameable.Name = SettlementNameGenerator.GenerateSettlementName(worldObject);
                }
                Find.WorldObjects.Add(worldObject);
                report.placed++;
            }
            return report;
        }
    }
}
