using System;
using System.IO;
using UnityEngine;

namespace MiningTown
{
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        public SaveData State { get; private set; }
        public string Status { get; private set; }
        public bool HasUnsavedChanges { get; private set; }
        public string SavePath { get { return repository.FilePath; } }
        public string ExportPath { get; private set; }
        public event Action StateChanged;
        private SaveRepository repository;

        private void Awake()
        {
            string folder = Path.Combine(Application.persistentDataPath, "MiningTownSavePrototype");
            repository = new SaveRepository(Path.Combine(folder, "save.json"), new UnitySaveJsonCodec());
            ExportPath = Path.Combine(folder, "Exports", "MiningTownExport.json");
            State = SaveData.NewGame();
            Status = File.Exists(SavePath) || File.Exists(repository.BackupPath)
                ? "A save exists. Press M, then Load to continue."
                : "Collect wood to restore the town. The bridge lies to the north-east.";
            Debug.Log("Mining Town save file: " + SavePath);
        }

        public void Collect(int nodeId)
        {
            string message;
            if (GameRules.TryCollect(State, nodeId, out message))
            { HasUnsavedChanges = true; Notify(); }
            Status = message;
        }

        public void RepairBuilding(string buildingId)
        {
            string message;
            if (GameRules.TryRepairBuilding(State, buildingId, out message))
            { HasUnsavedChanges = true; Notify(); }
            Status = message;
        }

        public void RepairBridge()
        {
            string message;
            if (GameRules.TryRepairBridge(State, out message))
            { HasUnsavedChanges = true; Notify(); }
            Status = message;
        }

        public void MovePlayer(float dx, float dz)
        { if (GameRules.TryMove(State, dx, dz)) HasUnsavedChanges = true; }

        public void ShowMineMessage() { Status = GameRules.MineMessage; }

        public void SaveLocal()
        {
            try
            {
                repository.Save(State);
                HasUnsavedChanges = false;
                Status = "Game saved.";
            }
            catch (Exception ex) when (SaveRepository.IsExpectedFileError(ex))
            { Status = "Save failed: " + ex.Message; Debug.LogWarning(Status); }
        }

        public void LoadLocal()
        {
            SaveData loaded;
            string message;
            if (repository.TryLoad(out loaded, out message))
            {
                State = loaded;
                HasUnsavedChanges = false;
                Notify();
            }
            Status = message;
        }

        public void ExportJson()
        {
            try
            {
                repository.Export(State, ExportPath);
                Status = "Saved game exported. Open Folder to find or copy it.";
            }
            catch (Exception ex) when (SaveRepository.IsExpectedFileError(ex))
            { Status = "Export failed: " + ex.Message; }
        }

        public void ImportJson()
        {
            try
            {
                SaveData imported = repository.Import(ExportPath);
                State = imported;
                HasUnsavedChanges = true;
                Notify();
                Status = "Saved game imported. Click Save to keep it as your local save.";
            }
            catch (Exception ex) when (SaveRepository.IsExpectedFileError(ex))
            { Status = "Import failed; current progress kept. " + ex.Message; }
        }

        public void NewSession()
        {
            State = SaveData.NewGame();
            HasUnsavedChanges = true;
            Status = "Fresh session. Your saved file is unchanged until you click Save.";
            Notify();
        }

        public void OpenSaveFolder()
        {
            try
            {
                string directory = Path.GetDirectoryName(ExportPath);
                Directory.CreateDirectory(directory);
                Application.OpenURL(new Uri(directory + Path.DirectorySeparatorChar).AbsoluteUri);
                Status = "Opened the Exports folder. The main save.json is one folder above.";
            }
            catch (Exception ex) when (SaveRepository.IsExpectedFileError(ex))
            { Status = "Could not open folder: " + ex.Message; }
        }

        private void Notify() { if (StateChanged != null) StateChanged(); }
    }
}
