using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiningTown.Editor
{
    [CustomEditor(typeof(MiningTownPrototype))]
    public sealed class MiningTownSceneInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var demo = (MiningTownPrototype)target;
            if (demo.IsEditableScene)
            {
                EditorGUILayout.HelpBox("Edit the town in the Hierarchy and save the scene.", MessageType.Info);
                return;
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode
                || EditorUtility.IsPersistent(demo) || !demo.gameObject.scene.IsValid()))
            {
                if (GUILayout.Button("Create Editable Scene Copy")) MakeCopy(demo);
            }
        }

        private static void MakeCopy(MiningTownPrototype original)
        {
            var scene = original.gameObject.scene;
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Mining Town", "Save the current scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string directory = Path.GetDirectoryName(scene.path).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/MiningTownEditable.unity");
            if (!EditorSceneManager.SaveScene(scene, path, true)) return;

            var copy = EditorSceneManager.OpenScene(path);
            MiningTownPrototype demo = null;
            foreach (GameObject root in copy.GetRootGameObjects())
            {
                demo = root.GetComponentInChildren<MiningTownPrototype>(true);
                if (demo != null) break;
            }
            if (demo == null) throw new InvalidOperationException("The scene copy is missing its town.");
            string folderName = Path.GetFileNameWithoutExtension(path) + "_Materials";
            string folder = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + folderName);
            string guid = AssetDatabase.CreateFolder(directory, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create the town materials folder.");
            int index = 0;
            demo.BakeForScene(material =>
            {
                string asset = folder + "/TownColor_" + (++index).ToString("00") + ".mat";
                AssetDatabase.CreateAsset(material, asset);
            });
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(copy);
            if (!EditorSceneManager.SaveScene(copy)) throw new IOException("Could not save the editable scene.");
            Selection.activeGameObject = demo.gameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
            GUIUtility.ExitGUI();
        }
    }
}
