using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void UpgradeOperationalWork()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            LoadExisting();
            AddOperationalWork(GameObject.Find("Gameplay"));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Operational work authored in the existing hotel scene.");
        }

        public static void AddOperationalWork(GameObject gameplay)
        {
            var rack = Object.FindAnyObjectByType<RoomKeyRack>();
            foreach (var key in rack.keys.Where(k => k.roomId != 0))
            {
                var room = GameObject.Find("Room" + key.roomId).transform;
                int side = key.roomId % 2 == 1 ? -1 : 1;
                var keyAnchor = room.Find("Forgotten key on bedside table");
                if (!keyAnchor) keyAnchor = Group("Forgotten key on bedside table", room, new Vector3(-side * .85f, .85f, 1.0f)).transform;
                keyAnchor.localRotation = Quaternion.Euler(90, 0, 0);
                key.leftInsideAnchor = keyAnchor;
                if (room.GetComponentInChildren<RoomResetInteraction>()) continue;
                // The inner bedside position overlapped the guest's standing sleep anchor.
                // Keep it at the foot, clear of both the sleep anchor and the armchair
                // centred at side * 2.45, z -2.25 in the outer corner.
                var waste = Group("Waste basket turnover " + key.roomId, room, new Vector3(side * 1.2f, 0, -2.4f));
                Cylinder("Waste basket", waste.transform, new Vector3(0, .22f, 0), .24f, .44f, "Pipe iron");
                var wasteHit = waste.AddComponent<BoxCollider>(); wasteHit.center = new Vector3(0, .34f, 0); wasteHit.size = new Vector3(.58f, .68f, .58f);
                var trash = Sphere("One coarse waste bundle", waste.transform, new Vector3(0, .48f, 0), new Vector3(.46f, .46f, .42f), "Cream plaster");
                Box("Waste paper folded top", trash.transform, new Vector3(.15f, .45f, 0), new Vector3(.65f, .18f, .6f), "Cream linen", false, false);
                var wasteAction = waste.AddComponent<RoomResetInteraction>();
                wasteAction.roomId = key.roomId; wasteAction.element = RoomDisorder.Waste; wasteAction.seconds = 3;
                wasteAction.disorderVisual = trash; wasteAction.displayName = "Waste basket";

                var towels = Group("Used towels turnover " + key.roomId, room, new Vector3(side * .3f, .08f, -1.42f));
                var towelsHit = towels.AddComponent<BoxCollider>(); towelsHit.center = new Vector3(0, .10f, 0); towelsHit.size = new Vector3(.85f, .35f, .58f);
                var cloth = Box("Used towel bundle", towels.transform, new Vector3(0, .08f, 0), new Vector3(.72f, .15f, .49f), "Cream plaster", true, false);
                var fold = Box("Rumpled towel", cloth.transform, new Vector3(.06f, .8f, 0), new Vector3(.85f, .8f, .8f), "Cream linen", false, false);
                fold.transform.localRotation = Quaternion.Euler(0, 14, 8);
                var towelAction = towels.AddComponent<RoomResetInteraction>();
                towelAction.roomId = key.roomId; towelAction.element = RoomDisorder.Towels; towelAction.seconds = 3;
                towelAction.disorderVisual = cloth; towelAction.displayName = "Used towels";
                towelAction.hideColliderWhenTidy = true;

                var seat = room.GetComponentsInChildren<Transform>().First(t => t.name == "Chair seat").parent;
                var chairAction = seat.gameObject.AddComponent<RoomResetInteraction>();
                chairAction.roomId = key.roomId; chairAction.element = RoomDisorder.Chair; chairAction.seconds = 2;
                chairAction.displayName = "Armchair"; chairAction.movingProp = seat;
                chairAction.tidyPosition = seat.localPosition; chairAction.untidyPosition = seat.localPosition + Vector3.forward * .12f;
                chairAction.tidyRotation = seat.localRotation; chairAction.untidyRotation = seat.localRotation * Quaternion.Euler(0, side * 24, 0);
                foreach (var action in room.GetComponentsInChildren<RoomResetInteraction>())
                    foreach (var child in action.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            }
            if (!rack.keys.Any(k => k.roomId == 0))
            {
                var mount = Group("Staff key hook", gameplay.transform, new Vector3(-3.8f, 1.78f, 5.56f)).transform;
                Box("Staff key backboard", mount, Vector3.zero, new Vector3(.55f, .72f, .12f), "Mahogany", true);
                Text("Staff key sign", mount, "STAFF", new Vector3(0, .25f, -.071f), .08f, Lettering);
                var anchor = Group("Staff key dock", mount, new Vector3(0, -.07f, -.20f)).transform;
                var keyObject = Object.Instantiate(rack.keys[0].gameObject, rack.keys[0].transform.parent);
                keyObject.name = "Staff master key"; keyObject.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                var key = keyObject.GetComponent<RoomKeyItem>(); key.roomId = 0; key.rackAnchor = anchor; key.leftInsideAnchor = null;
                keyObject.GetComponent<PhysicsPickup>().itemName = "STAFF key";
                foreach (var label in keyObject.GetComponentsInChildren<TextMesh>()) { label.text = "STAFF"; label.characterSize *= .52f; }
                rack.keys = rack.keys.Concat(new[] { key }).ToArray();
            }
            // Hang it on the rear wall, with a standing space in the aisle behind reception.
            // Keep upgrades idempotent for scenes authored by an earlier milestone pass.
            GameObject.Find("Staff key hook").transform.position = new Vector3(-3.8f, 1.78f, 5.56f);
            var staffKey = rack.keys.Single(k => k.roomId == 0);
            staffKey.transform.SetPositionAndRotation(staffKey.rackAnchor.position, staffKey.rackAnchor.rotation);
        }
    }
}
