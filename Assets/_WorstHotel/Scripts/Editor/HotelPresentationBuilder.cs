using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>Called by phase 2 composition after the scene and authoritative session exist.</summary>
        public static void AddRoomPlaques()
        {
            RepairMissingRenderResources();
            for (int id = 101; id <= 110; id++)
            {
                var door = GameObject.Find("Door" + id);
                if (door == null) throw new System.InvalidOperationException("Missing generated door " + id);
                var existing = door.transform.Find("RoomStatusPlaque");
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                var plaque = Group("RoomStatusPlaque", door.transform, new Vector3(1.62f, 1.68f, -.34f)).transform;
                Box("Brass plaque frame", plaque, Vector3.zero, new Vector3(1.10f, .84f, .09f), "Aged brass", true, false);
                Box("Plaque enamel", plaque, new Vector3(0, 0, -.056f), new Vector3(.99f, .73f, .025f), "Ink", true, false);
                var label = Text("Live room status", plaque, id + "\n—", new Vector3(0, 0, -.082f), .108f, new Color(.96f, .88f, .66f));
                label.lineSpacing = 1.06f;
                var status = plaque.gameObject.AddComponent<RoomStatusPlaque>();
                status.roomId = id;
                status.label = label;
            }
        }

        /// <summary>Restore only a missing default resource reference; retain all renderer and pipeline tuning.</summary>
        public static void RepairMissingRenderResources()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/HotelRenderer.asset");
            if (renderer == null || renderer.postProcessData != null) return;
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
            if (renderer.postProcessData == null)
                throw new System.InvalidOperationException("The installed URP package is missing its default post-processing data.");
            EditorUtility.SetDirty(renderer);
        }
    }
}
