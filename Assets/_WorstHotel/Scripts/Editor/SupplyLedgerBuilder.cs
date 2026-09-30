using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void BuildSupplyLedger(Transform root)
        {
            if (DiegeticBookInteraction.Find(HotelBook.Supplies)) return;
            // Existing folding table, approached from the linen room's clear central aisle.
            BuildHotelBook(root, HotelBook.Supplies, "SUPPLIES & LAUNDRY",
                new Vector3(10.82f, 1.20f, 4.30f), new Vector3(65, -90, 0), "Teal upholstery");
            var note = Group("Service delivery note", root, new Vector3(13.10f, 2.07f, 6.58f));
            Box("Delivery notice backing", note.transform, Vector3.zero, new Vector3(1.15f, .92f, .032f), "Gauge ivory", true, false);
            var delivery = note.AddComponent<SupplyDeliveryNotice>();
            delivery.label = Text("Printed delivery note", note.transform, "SERVICE DELIVERY\nOrder at the ledger\nStock returns to shelves",
                new Vector3(0, 0, -.023f), .059f, Mat("Ink").color);
        }

        [MenuItem("Tools/Worst Hotel/Add Supply Ledger To Existing Scene")]
        public static void AddSupplyLedgerToExistingScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save existing scene edits before adding the ledger.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("Hotel books and clocks");
            if (!root || !GameObject.Find("Laundry folding table")) throw new InvalidOperationException("Expected existing hotel and linen room.");
            LoadExisting();
            BuildSupplyLedger(root.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save the supply ledger.");
            Debug.Log("WORST HOTEL 0.6.5: supply ledger and delivery note added; existing scene preserved.");
        }
    }
}
