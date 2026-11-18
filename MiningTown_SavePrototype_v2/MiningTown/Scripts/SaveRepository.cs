using System;
using System.IO;
using System.Text;

namespace MiningTown
{
    // Kept separate from Unity so file handling and game rules can be tested.
    public interface ISaveJsonCodec
    {
        string Encode(SaveData data);
        SaveData Decode(string json);
    }

    public sealed class SaveRepository
    {
        public const int MaximumSaveBytes = 64 * 1024;
        public string FilePath { get; private set; }
        public string BackupPath { get { return FilePath + ".bak"; } }
        private readonly ISaveJsonCodec codec;

        public SaveRepository(string filePath, ISaveJsonCodec jsonCodec)
        {
            FilePath = Path.GetFullPath(filePath);
            codec = jsonCodec ?? throw new ArgumentNullException("jsonCodec");
        }

        public void Save(SaveData state)
        {
            RequireValid(state);
            SaveData snapshot = state.Copy();
            snapshot.savedAtUtc = DateTime.UtcNow.ToString("o");
            string json = codec.Encode(snapshot);
            if (Encoding.UTF8.GetByteCount(json) > MaximumSaveBytes)
                throw new InvalidDataException("Save file is too large.");
            // Verify the serialized result before touching the existing save.
            RequireValid(codec.Decode(json));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    // Never replace a good backup with a corrupt primary file.
                    SaveData previous;
                    string ignored;
                    if (TryRead(FilePath, out previous, out ignored))
                        File.Copy(FilePath, BackupPath, true);
                    // Desktop filesystems: replace the completed file, not its contents.
                    File.Replace(temporary, FilePath, null);
                }
                else File.Move(temporary, FilePath);
            }
            finally
            {
                // Cleanup must not hide the result of the actual save operation.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public bool TryLoad(out SaveData state, out string message)
        {
            string primaryError;
            if (TryRead(FilePath, out state, out primaryError))
            { message = "Loaded local save."; return true; }
            string backupError;
            if (TryRead(BackupPath, out state, out backupError))
            {
                message = "Recovered the previous backup. Click Save to restore the main file.";
                return true;
            }
            state = null;
            message = "Could not load: " + primaryError;
            return false;
        }

        // Imports read exactly the chosen JSON. No silent fallback to another file.
        public SaveData Import(string path)
        {
            SaveData result;
            string error;
            if (!TryRead(Path.GetFullPath(path), out result, out error))
                throw new InvalidDataException(error);
            return result;
        }

        public void Export(SaveData state, string path)
        {
            string destination = Path.GetFullPath(path);
            if (String.Equals(destination, FilePath, StringComparison.OrdinalIgnoreCase)
                || String.Equals(destination, BackupPath, StringComparison.OrdinalIgnoreCase)
                || String.Equals(destination, FilePath + ".tmp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a separate export filename.");
            new SaveRepository(destination, codec).Save(state);
        }

        private bool TryRead(string path, out SaveData data, out string error)
        {
            data = null;
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("No save file exists yet.");
                if (new FileInfo(path).Length > MaximumSaveBytes)
                    throw new InvalidDataException("Save file is too large.");
                string json = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}"))
                    throw new InvalidDataException("The file is not a complete JSON object.");
                SaveData candidate = GameRules.UpgradeIfNeeded(codec.Decode(json));
                RequireValid(candidate);
                data = candidate;
                error = null;
                return true;
            }
            catch (Exception ex) when (IsExpectedFileError(ex))
            { error = ex.Message; return false; }
        }

        private static void RequireValid(SaveData state)
        {
            string error;
            if (!GameRules.Validate(state, out error)) throw new InvalidDataException(error);
        }

        public static bool IsExpectedFileError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException
                || ex is ArgumentException || ex is NotSupportedException
                || ex is System.Security.SecurityException;
        }
    }
}
