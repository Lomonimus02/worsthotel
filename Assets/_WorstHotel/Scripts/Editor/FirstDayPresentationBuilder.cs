using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void UpgradeFirstDayPresentation()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            LoadExisting(); AddFirstDayPresentation(GameObject.Find("Gameplay")); CorrectFirstDayClueLayout();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void AddFirstDayPresentation(GameObject gameplay)
        {
            foreach (var bundle in Object.FindObjectsByType<LinenBundleItem>(FindObjectsSortMode.None).Where(b => b.sourceRoomId != 0))
            {
                var label = bundle.GetComponentsInChildren<TextMesh>(true).First();
                label.text = "USED\n" + bundle.sourceRoomId; label.characterSize = .044f * 10 / label.fontSize;
            }
            if (GameObject.Find("Room status board")) return;
            RemoveObject("Sign THE WORST HOTEL EVER");
            var board = Group("Room status board", gameplay.transform, new Vector3(-5.4f, 2.57f, 5.55f)).transform;
            Box("Board brass frame", board, Vector3.zero, new Vector3(5.3f, 1.8f, .10f), "Aged brass", false, false);
            Box("Room state enamel", board, new Vector3(0, 0, -.065f), new Vector3(5.16f, 1.66f, .045f), "Boiler enamel", false, false);
            Text("Room status heading", board, "ROOM STATUS", new Vector3(0, .67f, -.096f), .19f, Lettering);
            var display = board.gameObject.AddComponent<RoomStatusBoard>();
            display.roomIds = Enumerable.Range(101, HotelLayout.RoomCount).ToArray();
            display.labels = display.roomIds.Select((id, i) => Text("Status " + id, board, id + "   READY",
                new Vector3(i < 5 ? -1.29f : 1.29f, .35f - i % 5 * .245f, -.096f), .13f, Lettering)).ToArray();
            Sign(gameplay.transform, "THE WORST HOTEL EVER", new Vector3(-5.4f, 3.68f, 5.55f), 5.3f, .36f, .18f);
            Sign(gameplay.transform, "STAFF ONLY  >\nLAUNDRY / LINEN", new Vector3(3.2f, 2.45f, 5.57f), 2.35f, .7f, .13f);

            var roomTag = Group("101 room state plaque", gameplay.transform, new Vector3(-1.96f, 1.76f, 8.25f), new Vector3(0, -90, 0));
            Box("Room state plaque backing", roomTag.transform, Vector3.zero, new Vector3(1.18f, .32f, .025f), "Boiler enamel", false, false);
            var tag = roomTag.AddComponent<RoomStatusBoard>(); tag.roomIds = new[] { 101 };
            tag.labels = new[] { Text("101 readiness", roomTag.transform, "101  NOT READY", new Vector3(0, 0, -.018f), .073f, Lettering) };
            Sign(gameplay.transform, "LINEN SERVICE\nUsed sets: LAUNDRY hamper\nFresh sets: LAUNDRY shelf\nFit a fresh set to the bed", new Vector3(-2.36f, 1.72f, 8.25f), 1.6f, .80f, .077f, 90);
            Sign(gameplay.transform, "USED LINEN", new Vector3(15.22f, 1.1f, .86f), .82f, .24f, .085f);
            Sign(gameplay.transform, "USED SETS  >  HAMPER\nCLEAN SET  >  BED", new Vector3(11.65f, 2.25f, 6.56f), 2.0f, .58f, .09f);

            foreach (var bundle in Object.FindObjectsByType<LinenBundleItem>(FindObjectsSortMode.None).Where(b => b.sourceRoomId != 0))
            {
                var middle = bundle.transform.Find("Folded linen middle");
                var top = bundle.transform.Find("Folded linen top");
                middle.localRotation = Quaternion.Euler(5, 23, 7);
                top.localRotation = Quaternion.Euler(-8, -27, -11);
                top.localPosition += new Vector3(.07f, .02f, .03f);
                var mark = Box("Creased used cloth", bundle.transform, new Vector3(-.17f, .11f, .02f), new Vector3(.29f, .07f, .48f), "Cream plaster", false, false);
                mark.transform.localRotation = Quaternion.Euler(4, 18, 16);
                var label = bundle.GetComponentsInChildren<TextMesh>(true).First();
                label.text = "USED\n" + bundle.sourceRoomId; label.characterSize = .044f * 10 / label.fontSize;
                Text("Used linen top tag", bundle.transform, "USED", new Vector3(0, .166f, -.05f), .063f, Mat("Ink").color, new Vector3(90, 0, 0));
                foreach (var child in bundle.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            }
            // Keep the hamper and used-set colour coding consistent while retaining the same item mechanics.
            var hamper = Object.FindAnyObjectByType<LaundryHamperInteraction>();
            var backing = hamper.transform.Find("Hamper label backing");
            if (backing) backing.GetComponent<Renderer>().sharedMaterial = Mat("Burgundy velvet");
            var rack = Object.FindAnyObjectByType<RoomKeyRack>();
            Text("Key handoff rack engraving", rack.transform, "ROOM NUMBER = KEY NUMBER", new Vector3(0, -.61f, -.111f), .059f, Lettering);
            CorrectFirstDayClueLayout();
        }

        static void CorrectFirstDayClueLayout()
        {
            // Keep every status above the reception books, and the laundry guide above its shelf.
            var board = GameObject.Find("Room status board").transform;
            board.position = new Vector3(-5.4f, 2.75f, 5.55f);
            board.Find("Board brass frame").localScale = new Vector3(5.3f, 1.6f, .10f);
            board.Find("Room state enamel").localScale = new Vector3(5.16f, 1.46f, .045f);
            var display = board.GetComponent<RoomStatusBoard>();
            for (int i = 0; i < display.labels.Length; i++)
                display.labels[i].transform.localPosition = new Vector3(i < 5 ? -1.29f : 1.29f, .35f - i % 5 * .20f, -.096f);
            GameObject.Find("Sign USED SETS  >  HAMPER CLEAN SET  >  BED").transform.position = new Vector3(11.65f, 3.22f, 6.05f);
            foreach (var bundle in Object.FindObjectsByType<LinenBundleItem>(FindObjectsSortMode.None).Where(b => b.sourceRoomId != 0))
                bundle.transform.Find("Used linen top tag").localPosition = new Vector3(0, .255f, -.05f);
        }
    }
}
