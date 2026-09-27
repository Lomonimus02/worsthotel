using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorstHotel.Editor
{
    public static class BuildAutomation
    {
        public static void Configure()
        {
            PlayerSettings.companyName = "Independent Prototype";
            PlayerSettings.productName = "The Worst Hotel Ever";
            PlayerSettings.bundleVersion = "0.5.5";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) input.intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorSettings.serializationMode = SerializationMode.ForceText;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 2;
            AssetDatabase.SaveAssets();
            Debug.Log("WORST HOTEL: project configured (Input System, Linear, Windows).");
        }

        [MenuItem("Tools/Worst Hotel/Build Windows Player")]
        public static void BuildWindows()
        {
            Configure();
            var scene = "Assets/_WorstHotel/Scenes/PrototypeHotel.unity";
            if (!File.Exists(scene)) throw new InvalidOperationException("Build Prototype Scene first.");
            string output = Environment.GetEnvironmentVariable("WORST_HOTEL_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "Builds/Windows";
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(output, "TheWorstHotelEver.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("WORST HOTEL: Windows build succeeded.");
        }
    }
}
