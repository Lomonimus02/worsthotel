using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>Finite physical linen stock, one hamper, and one concise turnover target per room.</summary>
        public static void AddLinenStorage(GameObject gameplay)
        {
            var linenRoot = Group("Player linen supplies", gameplay.transform).transform;
            var itemsRoot = Group("Physical linen bundles", gameplay.transform).transform;
            var shelf = Group("Clean linen shelf", linenRoot, new Vector3(11.65f, 0, 6.20f));
            var storage = shelf.AddComponent<LinenStorage>();
            storage.cleanStock = new LinenBundleItem[HotelLayout.RoomCount];
            Box("Linen shelf back", shelf.transform, new Vector3(0, 1.4f, .25f), new Vector3(1.60f, 2.8f, .10f), "Walnut panels", true);
            foreach (int side in new[] { -1, 1 })
                Box("Linen shelf side", shelf.transform, new Vector3(side * .76f, 1.4f, 0), new Vector3(.08f, 2.8f, .60f), "Mahogany", true);
            Box("Linen shelf plinth", shelf.transform, new Vector3(0, .065f, 0), new Vector3(1.60f, .13f, .60f), "Mahogany", true);
            for (int row = 0; row < 5; row++)
                Box("Clean stock shelf " + (row + 1), shelf.transform, new Vector3(0, .63f + row * .45f, 0),
                    new Vector3(1.49f, .08f, .60f), "Walnut panels", true);
            Text("Clean linen storage sign", shelf.transform, "CLEAN LINEN", new Vector3(0, 2.78f, -.312f), .15f, Lettering);

            var grabSettings = EnsureGrabConfiguration();
            for (int i = 0; i < storage.cleanStock.Length; i++)
            {
                var source = Group("CleanLinenAnchor" + i, shelf.transform,
                    new Vector3(i % 2 == 0 ? -.36f : .36f, .80f + (i / 2) * .45f, -.21f)).transform;
                storage.cleanStock[i] = MakeLinenBundle(itemsRoot, "clean:" + i, 0, source, grabSettings);
            }

            var hamperObject = Group("Dirty linen hamper", linenRoot, new Vector3(15.22f, 0, 1.25f));
            var hamper = hamperObject.AddComponent<LaundryHamperInteraction>();
            hamper.displayName = "Dirty linen hamper";
            var hamperTarget = hamperObject.AddComponent<BoxCollider>();
            hamperTarget.center = new Vector3(0, .47f, 0);
            hamperTarget.size = new Vector3(.82f, .94f, .72f);
            Box("Hamper base", hamperObject.transform, new Vector3(0, .11f, 0), new Vector3(.82f, .16f, .72f), "Mahogany", true, false);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Hamper canvas side", hamperObject.transform, new Vector3(side * .37f, .52f, 0),
                    new Vector3(.08f, .80f, .70f), "Cream plaster", true, false);
                Box("Hamper front or back", hamperObject.transform, new Vector3(0, .52f, side * .32f),
                    new Vector3(.76f, .80f, .08f), "Cream plaster", true, false);
                Box("Hamper rim side", hamperObject.transform, new Vector3(side * .37f, .94f, 0),
                    new Vector3(.08f, .08f, .72f), "Aged brass", true, false);
                Box("Hamper rim end", hamperObject.transform, new Vector3(0, .94f, side * .32f),
                    new Vector3(.80f, .08f, .08f), "Aged brass", true, false);
            }
            Box("Hamper inner shadow", hamperObject.transform, new Vector3(0, .22f, 0), new Vector3(.65f, .04f, .53f), "Ink", false, false);
            Box("Hamper label backing", hamperObject.transform, new Vector3(0, .65f, -.37f), new Vector3(.69f, .33f, .03f), "Mahogany", true, false);
            hamper.statusLabel = Text("Laundry hamper status", hamperObject.transform, "DIRTY LINEN", new Vector3(0, .65f, -.39f), .088f, Lettering);
            hamper.depositAnchor = Group("LaundryDepositAnchor", hamperObject.transform, new Vector3(0, .84f, 0)).transform;

            for (int i = 0; i < HotelLayout.RoomCount; i++)
            {
                int roomId = 101 + i, side = i % 2 == 0 ? -1 : 1;
                float doorZ = HotelLayout.RoomZ(i);
                var room = GameObject.Find("Room" + roomId).transform;
                Transform bedTransform = null;
                foreach (Transform child in room)
                    if (child.Find("Mattress") != null) { bedTransform = child; break; }
                if (bedTransform == null) throw new System.InvalidOperationException("Room " + roomId + " has no authored bed.");
                var bedInteraction = bedTransform.gameObject.AddComponent<LinenBedInteraction>();
                bedInteraction.roomId = roomId;
                bedInteraction.displayName = "Room " + roomId + " bed";
                var madePieces = new List<GameObject>();
                foreach (Transform child in bedTransform)
                    if (child.name == "Duvet" || child.name == "Burgundy runner" || child.name == "Pillow")
                        madePieces.Add(child.gameObject);
                bedInteraction.madeBedPieces = madePieces.ToArray();
                var anchor = Group("DirtyLinenAnchor" + roomId, linenRoot, new Vector3(side * 5.95f, 1.015f, doorZ + .15f)).transform;
                bedInteraction.dirtyBundle = MakeLinenBundle(itemsRoot, "dirty:" + roomId, roomId, anchor, grabSettings);

                // A visible inner-bed face is reachable from the clear lane beside the mattress.
                Box("Bed linen context face", bedTransform, new Vector3(-side * 1.04f, .84f, -.51f),
                    new Vector3(.10f, .39f, .82f), "Mahogany", true);
                bedInteraction.statusLabel = Text("Bed linen status " + roomId, bedTransform, "ROOM " + roomId + "\nLINEN READY",
                    new Vector3(-side * 1.095f, .84f, -.51f), .075f, Lettering, new Vector3(0, side * 90, 0));
                foreach (var child in bedTransform.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            }
        }

        static LinenBundleItem MakeLinenBundle(Transform parent, string id, int roomId, Transform anchor, GrabPhysicsConfig settings)
        {
            bool dirty = roomId != 0;
            var bundle = Group(dirty ? "Dirty linen " + roomId : "Clean linen " + id, parent);
            bundle.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            var body = bundle.AddComponent<Rigidbody>();
            body.mass = .6f;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var collider = bundle.AddComponent<BoxCollider>();
            collider.size = new Vector3(.62f, .26f, .44f);
            var pickup = bundle.AddComponent<PhysicsPickup>();
            pickup.itemName = dirty ? "Dirty linen · room " + roomId : "Clean linen bundle";
            pickup.holdDistance = 1.2f;
            pickup.grabConfig = settings;
            string cloth = dirty ? "Cream plaster" : "Cream linen";
            Box("Folded linen base", bundle.transform, new Vector3(0, -.071f, 0), new Vector3(.61f, .11f, .43f), cloth, true, false);
            var middle = Box("Folded linen middle", bundle.transform, new Vector3(0, .021f, 0), new Vector3(.56f, .085f, .37f), cloth, true, false);
            var top = Box("Folded linen top", bundle.transform, new Vector3(0, .093f, 0), new Vector3(.50f, .060f, .33f), cloth, true, false);
            if (dirty)
            {
                middle.transform.localEulerAngles = new Vector3(0, 5, 0);
                top.transform.localEulerAngles = new Vector3(0, -9, 0);
            }
            Box("Linen bundle band", bundle.transform, new Vector3(0, 0, -.219f), new Vector3(.16f, .244f, .016f),
                dirty ? "Burgundy velvet" : "Teal upholstery", true, false);
            Text("Linen bundle source label", bundle.transform, dirty ? roomId.ToString() : "CLEAN", new Vector3(0, -.006f, -.231f),
                dirty ? .060f : .042f, Lettering);
            var item = bundle.AddComponent<LinenBundleItem>();
            item.itemId = id;
            item.sourceRoomId = roomId;
            item.sourceAnchor = anchor;
            foreach (var child in bundle.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            // The initial authored rooms are clean. Runtime state reveals this same body on turnover.
            if (dirty)
            {
                foreach (var visual in bundle.GetComponentsInChildren<Renderer>()) visual.enabled = false;
                collider.enabled = false;
                body.isKinematic = true;
            }
            return item;
        }
    }
}
