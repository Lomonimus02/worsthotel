using UnityEditor;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>Six real, numbered keys beside the reception counter, with fixed return docks.</summary>
        public static void AddRoomKeyRack(GameObject gameplay)
        {
            var rackObject = Group("Room key rack", gameplay.transform, new Vector3(-2.3f, 1.85f, 4.25f));
            var rack = rackObject.AddComponent<RoomKeyRack>();
            rack.keys = new RoomKeyItem[HotelLayout.RoomCount];
            var rackTransform = rackObject.transform;
            Box("Key rack backboard", rackTransform, Vector3.zero, new Vector3(2.13f, 1.40f, .16f), "Mahogany", true);
            Box("Key rack inset", rackTransform, new Vector3(0, 0, -.088f), new Vector3(2.02f, 1.29f, .028f), "Walnut panels", true, false);
            Box("Key rack pedestal", rackTransform, new Vector3(0, -1.43f, .02f), new Vector3(.68f, .84f, .38f), "Mahogany", true);
            Box("Key rack upright", rackTransform, new Vector3(0, -.92f, .055f), new Vector3(.25f, .44f, .22f), "Mahogany", true);
            Box("Key rack foot", rackTransform, new Vector3(0, -1.79f, .02f), new Vector3(.80f, .12f, .44f), "Mahogany", true);
            Text("Key rack heading", rackTransform, "ROOM KEYS", new Vector3(0, .58f, -.108f), .105f, Lettering);

            var keysRoot = Group("Physical room keys", gameplay.transform).transform;
            var grabSettings = EnsureGrabConfiguration();
            for (int i = 0; i < rack.keys.Length; i++)
            {
                int roomId = 101 + i;
                float x = (i % 5 - 2f) * .39f;
                float y = .16f - (i / 5) * .53f;
                // The label remains at the empty dock after its matching physical key is taken.
                Text("Rack number " + roomId, rackTransform, roomId.ToString(),
                    new Vector3(x, y + .16f, -.111f), .085f, Lettering);
                Cylinder("Brass key hook " + roomId, rackTransform, new Vector3(x, y + .12f, -.149f),
                    .014f, .13f, "Aged brass", new Vector3(90, 0, 0));
                Sphere("Hook tip " + roomId, rackTransform, new Vector3(x, y + .128f, -.211f),
                    Vector3.one * .039f, "Aged brass");
                var anchor = Group("KeyRackAnchor" + roomId, rackTransform, new Vector3(x, y - .065f, -.215f)).transform;

                var keyObject = Group("Room key " + roomId, keysRoot);
                keyObject.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                var body = keyObject.AddComponent<Rigidbody>();
                body.mass = .2f;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var tagCollider = keyObject.AddComponent<BoxCollider>();
                tagCollider.center = new Vector3(0, -.026f, -.014f);
                tagCollider.size = new Vector3(.245f, .295f, .104f);
                var keyCollider = keyObject.AddComponent<BoxCollider>();
                keyCollider.center = new Vector3(0, .175f, -.012f);
                keyCollider.size = new Vector3(.12f, .12f, .052f);
                var pickup = keyObject.AddComponent<PhysicsPickup>();
                pickup.itemName = "Room " + roomId + " key";
                pickup.holdDistance = 1.1f;
                pickup.grabConfig = grabSettings;

                var tag = keyObject.transform;
                Box("Brass tag rim", tag, new Vector3(0, -.026f, 0), new Vector3(.245f, .295f, .07f), "Aged brass", true, false);
                Box("Numbered enamel tag", tag, new Vector3(0, -.025f, -.037f), new Vector3(.218f, .268f, .014f),
                    i < 3 ? "Burgundy velvet" : "Teal upholstery", true, false);
                Text("Physical key number " + roomId, tag, roomId.ToString(), new Vector3(0, -.027f, -.047f), .105f, Lettering);
                Text("Physical key back number " + roomId, tag, roomId.ToString(), new Vector3(0, -.027f, .037f),
                    .105f, new Color(.08f, .06f, .035f), new Vector3(0, 180, 0));
                var bow = Group("Old brass key bow", tag, new Vector3(0, .18f, -.014f));
                bow.transform.localScale = Vector3.one * .115f;
                bow.AddComponent<MeshFilter>().sharedMesh = Wheel;
                bow.AddComponent<MeshRenderer>().sharedMaterial = Mat("Aged brass");
                Box("Old key shaft", tag, new Vector3(.012f, .082f, -.053f), new Vector3(.024f, .12f, .025f), "Aged brass", true, false);
                Box("Old key first tooth", tag, new Vector3(.034f, .045f, -.053f), new Vector3(.055f, .023f, .025f), "Aged brass", false, false);
                Box("Old key second tooth", tag, new Vector3(.031f, .076f, -.053f), new Vector3(.049f, .018f, .025f), "Aged brass", false, false);

                var item = keyObject.AddComponent<RoomKeyItem>();
                item.roomId = roomId;
                item.rackAnchor = anchor;
                rack.keys[i] = item;
                foreach (var child in keyObject.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            }
        }
    }
}
