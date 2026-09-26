using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void AddEnvironmentFeedback(GameObject gameplay, GameObject environment)
        {
            AddRoomKeyRack(gameplay);
            AddLinenStorage(gameplay);
            AddServiceLayer(gameplay);
            AddRoomNoiseInteractions(gameplay);
            var feedback = gameplay.AddComponent<HotelFeedback>();
            feedback.steamAnchor = GameObject.Find("SteamAnchor").transform;
            feedback.sparkAnchor = GameObject.Find("PanelAnchor").transform;
            feedback.vibratingPipe = GameObject.Find("Upper manifold").transform;
            const string particlesPath = Root + "/Art/Materials/Boiler particles.mat";
            var particles = AssetDatabase.LoadAssetAtPath<Material>(particlesPath);
            if (particles == null)
            {
                particles = new Material(Shader.Find("WorstHotel/World Text"));
                AssetDatabase.CreateAsset(particles, particlesPath);
            }
            feedback.particleMaterialTemplate = particles;
            AddLivingFeedback(gameplay, feedback);
            // Moving doors, updated text meshes and physical luggage must retain their own transforms.
            foreach (var renderer in environment.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.GetComponent<TextMesh>() != null || renderer.GetComponentInParent<DoorInteractable>() != null ||
                    renderer.GetComponentInParent<Rigidbody>() != null || renderer.transform == feedback.vibratingPipe ||
                    renderer.GetComponentInParent<RadiatorHeatFeedback>() != null ||
                    renderer.GetComponentInParent<LinenBedInteraction>() != null ||
                    renderer.GetComponentInParent<RoomLampInteraction>() != null ||
                    renderer.transform.IsChildOf(feedback.vibratingPipe)) continue;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
            }
            AddSubtleAmbientOcclusion();
        }

        static void AddSubtleAmbientOcclusion()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/HotelRenderer.asset");
            ScreenSpaceAmbientOcclusion feature = null;
            foreach (var existing in renderer.rendererFeatures)
                if (existing is ScreenSpaceAmbientOcclusion ambientOcclusion) feature = ambientOcclusion;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                feature.name = "Subtle contact shading";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }
            // Serialized names verified against the installed URP package; half-resolution, four samples.
            var settings = new SerializedObject(feature);
            settings.FindProperty("m_Settings.Intensity").floatValue = .55f;
            settings.FindProperty("m_Settings.Radius").floatValue = .24f;
            settings.FindProperty("m_Settings.Falloff").floatValue = 25;
            settings.FindProperty("m_Settings.DirectLightingStrength").floatValue = .15f;
            settings.FindProperty("m_Settings.Downsample").boolValue = true;
            settings.FindProperty("m_Settings.Source").enumValueIndex = 0;
            settings.FindProperty("m_Settings.NormalSamples").enumValueIndex = 0;
            settings.FindProperty("m_Settings.AOMethod").enumValueIndex = 1;
            settings.FindProperty("m_Settings.Samples").enumValueIndex = 2;
            settings.FindProperty("m_Settings.BlurQuality").enumValueIndex = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            feature.SetActive(true);
            feature.Create();
            renderer.SetDirty();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
        }
    }
}
