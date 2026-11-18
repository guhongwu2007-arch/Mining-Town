using UnityEngine;

namespace MiningTown
{
    public sealed class UnitySaveJsonCodec : ISaveJsonCodec
    {
        public string Encode(SaveData data) { return JsonUtility.ToJson(data, true); }
        public SaveData Decode(string json) { return JsonUtility.FromJson<SaveData>(json); }
    }
}
