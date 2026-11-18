using System;
using System.Collections.Generic;
using System.IO;

namespace MiningTown
{
    public static class GameRules
    {
        public const string GameId = "mining-town-save-prototype";
        public const int SaveVersion = 2;
        public const int WoodPerNode = 2;
        public const int HouseHonorReward = 20;
        public const int BridgeWoodCost = 12;
        public const int BridgeHonorReward = 20;
        public const int RepairableHouseCount = 4;
        public const string MineMessage = "Mine unavailable for now.";

        public static bool TryCollect(SaveData state, int nodeId, out string message)
        {
            if (nodeId < 0 || nodeId >= WorldLayout.Resources.Length)
            { message = "Unknown wood bundle."; return false; }
            if (state.collectedWoodIds.Contains(nodeId))
            { message = "This bundle has already been collected."; return false; }
            if (!state.bridgeRepaired && WorldLayout.Resources[nodeId].Z > WorldLayout.RiverNorth)
            { message = "Repair the bridge to reach these supplies."; return false; }
            state.collectedWoodIds.Add(nodeId);
            state.wood += WoodPerNode;
            message = "+2 wood.";
            return true;
        }

        public static bool TryRepairBuilding(SaveData state, string id, out string message)
        {
            BuildingDefinition building = WorldLayout.FindBuilding(id);
            if (building == null) { message = "Unknown building."; return false; }
            if (!building.CanRepair)
            { message = "These ruins cannot be repaired yet."; return false; }
            if (state.repairedBuildingIds.Contains(id))
            { message = building.Name + " is already repaired."; return false; }
            if (state.wood < building.WoodCost)
            { message = "You need " + building.WoodCost + " wood to repair " + building.Name + "."; return false; }
            state.wood -= building.WoodCost;
            state.honor += HouseHonorReward;
            state.repairedBuildingIds.Add(id);
            state.houseRepaired = state.repairedBuildingIds.Contains("miner");
            message = building.Name + " repaired! +20 Honor.";
            return true;
        }

        public static bool TryRepairBridge(SaveData state, out string message)
        {
            if (state.bridgeRepaired)
            { message = "The bridge is open. The mine beyond it is still sealed."; return false; }
            if (state.wood < BridgeWoodCost)
            { message = "You need 12 wood to repair the bridge."; return false; }
            state.wood -= BridgeWoodCost;
            state.honor += BridgeHonorReward;
            state.bridgeRepaired = true;
            message = "Bridge repaired! +20 Honor. You can now cross the river.";
            return true;
        }

        public static bool TryMove(SaveData state, float dx, float dz)
        {
            if (!WorldLayout.Finite(dx) || !WorldLayout.Finite(dz)) return false;
            // Bound each step so callers cannot skip across a collision barrier.
            dx = Math.Max(-0.3f, Math.Min(0.3f, dx));
            dz = Math.Max(-0.3f, Math.Min(0.3f, dz));
            float x = state.playerX, z = state.playerZ;
            if (WorldLayout.IsWalkable(x + dx, z, state.bridgeRepaired)) x += dx;
            if (WorldLayout.IsWalkable(x, z + dz, state.bridgeRepaired)) z += dz;
            bool moved = x != state.playerX || z != state.playerZ;
            state.playerX = x; state.playerY = 1f; state.playerZ = z;
            return moved;
        }

        public static bool Validate(SaveData data, out string error)
        {
            error = null;
            if (data == null) error = "The save is empty.";
            else if (data.gameId != GameId) error = "This is not a Mining Town save.";
            else if (data.version != SaveVersion) error = "Unsupported save version: " + data.version + ".";
            else if (data.collectedWoodIds == null || data.repairedBuildingIds == null)
                error = "A required progress list is missing.";
            else
            {
                var nodes = new HashSet<int>();
                foreach (int id in data.collectedWoodIds)
                {
                    if (id < 0 || id >= WorldLayout.Resources.Length || !nodes.Add(id))
                    { error = "Invalid or repeated resource ID."; return false; }
                    if (!data.bridgeRepaired && WorldLayout.Resources[id].Z > WorldLayout.RiverNorth)
                    { error = "Supplies beyond the river require a repaired bridge."; return false; }
                }
                var repairs = new HashSet<string>();
                int spent = data.bridgeRepaired ? BridgeWoodCost : 0;
                foreach (string id in data.repairedBuildingIds)
                {
                    BuildingDefinition building = WorldLayout.FindBuilding(id);
                    if (building == null || !building.CanRepair || !repairs.Add(id))
                    { error = "Invalid or repeated building repair."; return false; }
                    spent += building.WoodCost;
                }
                int expectedHonor = repairs.Count * HouseHonorReward + (data.bridgeRepaired ? BridgeHonorReward : 0);
                if (data.wood < 0 || data.wood != nodes.Count * WoodPerNode - spent || data.honor != expectedHonor
                    || data.houseRepaired != repairs.Contains("miner"))
                    error = "Resources, Honor and repairs do not match.";
                else if (!WorldLayout.Finite(data.playerY) || Math.Abs(data.playerY - 1f) > 0.01f
                    || !WorldLayout.IsWalkable(data.playerX, data.playerZ, data.bridgeRepaired)
                    || (!data.bridgeRepaired && data.playerZ >= WorldLayout.RiverSouth))
                    error = "The saved player position is not accessible.";
            }
            return error == null;
        }

        public static SaveData UpgradeIfNeeded(SaveData input)
        {
            if (input == null || input.version != 1) return input;
            // Validate the old schema before adding new fields. Old files stay untouched.
            if (input.gameId != GameId || input.collectedWoodIds == null)
                throw new InvalidDataException("Invalid v1 save.");
            var ids = new HashSet<int>();
            foreach (int id in input.collectedWoodIds)
                if (id < 0 || id > 4 || !ids.Add(id)) throw new InvalidDataException("Invalid v1 resource ID.");
            int wood = ids.Count * WoodPerNode - (input.houseRepaired ? 5 : 0);
            if (wood < 0 || input.wood != wood || input.honor != (input.houseRepaired ? 20 : 0)
                || !WorldLayout.Finite(input.playerX) || !WorldLayout.Finite(input.playerZ)
                || input.playerX < -9 || input.playerX > 9 || input.playerZ < -6 || input.playerZ > 6)
                throw new InvalidDataException("Invalid v1 progress.");
            SaveData upgraded = input.Copy();
            upgraded.version = SaveVersion;
            upgraded.repairedBuildingIds = new List<string>();
            if (upgraded.houseRepaired) upgraded.repairedBuildingIds.Add("miner");
            upgraded.bridgeRepaired = false;
            string error;
            if (!Validate(upgraded, out error)) throw new InvalidDataException(error);
            return upgraded;
        }
    }
}
