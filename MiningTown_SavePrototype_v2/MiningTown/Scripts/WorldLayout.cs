using System;

namespace MiningTown
{
    public sealed class BuildingDefinition
    {
        public readonly string Id, Name;
        public readonly float X, Z;
        public readonly int WoodCost;
        public readonly bool CanRepair;
        public const float Width = 3f, Depth = 2.7f;
        public float DoorX, DoorZ;
        private float rightX = 1, rightZ, forwardX, forwardZ = 1;
        private float halfWidth = Width / 2f + 0.4f, halfDepth = Depth / 2f + 0.4f;

        public void SetFootprint(float rx, float rz, float fx, float fz, float width, float depth)
        {
            rightX = rx; rightZ = rz; forwardX = fx; forwardZ = fz;
            halfWidth = width / 2 + 0.4f; halfDepth = depth / 2 + 0.4f;
            DoorX = X - fx * (depth / 2 + 1.3f);
            DoorZ = Z - fz * (depth / 2 + 1.3f);
        }

        public bool Blocks(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            return Math.Abs(dx * rightX + dz * rightZ) < halfWidth
                && Math.Abs(dx * forwardX + dz * forwardZ) < halfDepth;
        }

        public BuildingDefinition(string id, string name, float x, float z, int cost, bool canRepair = true)
        { Id = id; Name = name; X = x; Z = z; WoodCost = cost; CanRepair = canRepair; SetFootprint(1, 0, 0, 1, Width, Depth); }
    }

    public struct ResourcePoint
    {
        public readonly float X, Z;
        public ResourcePoint(float x, float z) { X = x; Z = z; }
    }

    // One source of truth for map geometry, collision, resource IDs and save validation.
    public static class WorldLayout
    {
        public static float MinX = -23f, MaxX = 23f, MinZ = -18f, MaxZ = 37f;
        public static float RiverSouth = 18f, RiverNorth = 23f;
        public static float BridgeX = 14f, BridgeHalfWidth = 1.5f, BridgeApproachZ = 16.5f;
        public static float MineX = 14f, MineDoorZ = 28f;
        public const int TownResourceCount = 24;

        private static readonly BuildingDefinition[] originalBuildings =
        {
            new BuildingDefinition("miner", "Old Miner's House", 4f, 2.3f, 5),
            new BuildingDefinition("cottage", "West Cottage", -12f, 3f, 6),
            new BuildingDefinition("workshop", "Workshop House", 12f, -8f, 8),
            new BuildingDefinition("lodge", "South Lodge", -11f, -10f, 9),
            new BuildingDefinition("ruins", "Abandoned Ruins", 12f, 12f, 0, false)
        };

        // The first five IDs and positions are preserved from v1.
        private static readonly ResourcePoint[] originalResources =
        {
            new ResourcePoint(-5, -3), new ResourcePoint(-7, 0), new ResourcePoint(-4, 3),
            new ResourcePoint(0, 4.7f), new ResourcePoint(7, -3),
            new ResourcePoint(-18, -14), new ResourcePoint(-18, -6), new ResourcePoint(-20, 2),
            new ResourcePoint(-19, 10), new ResourcePoint(-15, 13), new ResourcePoint(-9, 12),
            new ResourcePoint(-4, 12), new ResourcePoint(1, 11), new ResourcePoint(6, 10),
            new ResourcePoint(17, 6), new ResourcePoint(20, 12), new ResourcePoint(19, -1),
            new ResourcePoint(19, -10), new ResourcePoint(15, -15), new ResourcePoint(7, -14),
            new ResourcePoint(1, -12), new ResourcePoint(-4, -16), new ResourcePoint(-6, -7),
            new ResourcePoint(8, 16),
            new ResourcePoint(-17, 28), new ResourcePoint(-10, 32), new ResourcePoint(-3, 28),
            new ResourcePoint(4, 32), new ResourcePoint(20, 29), new ResourcePoint(8, 26)
        };

        public static BuildingDefinition[] Buildings = (BuildingDefinition[])originalBuildings.Clone();
        public static ResourcePoint[] Resources = (ResourcePoint[])originalResources.Clone();
        public static float SpawnX, SpawnZ = -4f;
        public static float MineHalfWidth = 3.7f, MineBack = 36.6f, MineFront = 29.4f;

        public static void ResetLayout()
        {
            Buildings = (BuildingDefinition[])originalBuildings.Clone();
            Resources = (ResourcePoint[])originalResources.Clone();
            MinX = -23; MaxX = 23; MinZ = -18; MaxZ = 37;
            RiverSouth = 18; RiverNorth = 23; BridgeX = 14; BridgeHalfWidth = 1.5f;
            BridgeApproachZ = 16.5f; MineX = 14; MineDoorZ = 28;
            MineHalfWidth = 3.7f; MineBack = 36.6f; MineFront = 29.4f;
            SpawnX = 0; SpawnZ = -4;
        }

        public static BuildingDefinition FindBuilding(string id)
        {
            foreach (BuildingDefinition building in Buildings)
                if (building.Id == id) return building;
            return null;
        }

        public static bool IsWalkable(float x, float z, bool bridgeRepaired)
        {
            if (!Finite(x) || !Finite(z) || x < MinX || x > MaxX || z < MinZ || z > MaxZ)
                return false;
            // The river spans the entire map. The repaired deck is the only crossing.
            if (z >= RiverSouth && z <= RiverNorth
                && (!bridgeRepaired || Math.Abs(x - BridgeX) > BridgeHalfWidth)) return false;
            foreach (BuildingDefinition building in Buildings)
                if (building.Blocks(x, z)) return false;
            // Solid cliff and sealed mine: the repaired bridge does not unlock the entrance.
            if (Math.Abs(x - MineX) < MineHalfWidth && z > MineFront && z < MineBack) return false;
            return true;
        }

        public static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
