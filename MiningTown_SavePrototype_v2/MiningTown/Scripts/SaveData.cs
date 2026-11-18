using System;
using System.Collections.Generic;

namespace MiningTown
{
    // Plain data only. Unity's JsonUtility serializes these public fields.
    [Serializable]
    public sealed class SaveData
    {
        public string gameId;
        public int version;
        public int wood;
        public int honor;
        public bool houseRepaired;
        public List<string> repairedBuildingIds;
        public bool bridgeRepaired;
        public List<int> collectedWoodIds;
        public float playerX;
        public float playerY;
        public float playerZ;
        public string savedAtUtc;

        public static SaveData NewGame()
        {
            return new SaveData
            {
                gameId = GameRules.GameId,
                version = GameRules.SaveVersion,
                collectedWoodIds = new List<int>(),
                repairedBuildingIds = new List<string>(),
                playerX = WorldLayout.SpawnX,
                playerY = 1f,
                playerZ = WorldLayout.SpawnZ,
                savedAtUtc = ""
            };
        }

        public SaveData Copy()
        {
            return new SaveData
            {
                gameId = gameId, version = version, wood = wood, honor = honor,
                houseRepaired = houseRepaired,
                repairedBuildingIds = repairedBuildingIds == null ? null : new List<string>(repairedBuildingIds),
                bridgeRepaired = bridgeRepaired,
                collectedWoodIds = collectedWoodIds == null ? null : new List<int>(collectedWoodIds),
                playerX = playerX, playerY = playerY, playerZ = playerZ,
                savedAtUtc = savedAtUtc
            };
        }
    }
}
