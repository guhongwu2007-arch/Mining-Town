using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MiningTown.Editor
{
    public static class MiningTownSceneBuilder
    {
        [MenuItem("Tools/Mining Town/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (EditorApplication.isPlaying)
            { EditorUtility.DisplayDialog("Mining Town", "Stop Play mode first.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string folder = "Assets/MiningTown/Generated";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            string shaderName = GraphicsSettings.currentRenderPipeline == null
                ? "Unlit/Color" : "Universal Render Pipeline/Unlit";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("Use a Built-In 3D or Universal 3D project for this prototype.");
            string materialPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/WorldMaterial.mat");
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Mining Town Prototype");
            root.AddComponent<SaveManager>();
            var demo = root.AddComponent<MiningTownPrototype>();
            demo.worldMaterial = material;
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/MiningTownDemo.unity");
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            EditorUtility.DisplayDialog("Mining Town", "Demo created. Press Play, then click inside the Game view. The town is generated when Play starts.", "OK");
            Debug.Log("Mining Town scene created at " + scenePath);
        }
    }
}
