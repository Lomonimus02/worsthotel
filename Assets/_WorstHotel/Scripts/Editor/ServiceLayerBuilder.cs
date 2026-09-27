using System.Linq;
using UnityEditor;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void AddServiceLayer(GameObject gameplay)
        {
            var root = Group("Guest service equipment", gameplay.transform).transform;
            var items = Group("Physical service supplies", gameplay.transform).transform;
            BuildServiceShelf(root, items, ServiceItemKind.Blanket, new Vector3(14.1f, 0, 6.20f), 0);
            BuildServiceShelf(root, items, ServiceItemKind.ReplacementBulb, new Vector3(15.61f, 0, 4.1f), 90);
            BuildServiceLuggage(root, items);
            BuildReceptionServiceControls(root);
            for (int i = 0; i < HotelLayout.RoomCount; i++) BuildRoomServiceControls(root, 101 + i, i % 2 == 0 ? -1 : 1, HotelLayout.RoomZ(i));
            foreach (var child in root.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            foreach (var child in items.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        static void BuildServiceShelf(Transform root, Transform items, ServiceItemKind kind, Vector3 position, float yaw)
        {
            bool blanket = kind == ServiceItemKind.Blanket;
            var shelf = Group(blanket ? "Clean blanket storage" : "Replacement bulb maintenance shelf", root, position, new Vector3(0, yaw, 0));
            var station = shelf.AddComponent<ServiceStockShelfInteraction>(); station.kind = kind;
            station.displayName = blanket ? "Clean blanket storage" : "Replacement bulb storage";
            Box("Service shelf back", shelf.transform, new Vector3(0, 1.02f, .24f), new Vector3(1.62f, 2.04f, .10f), "Mahogany", true);
            for (int row = 0; row < 3; row++)
                Box("Service supply shelf " + row, shelf.transform, new Vector3(0, .54f + row * .46f, 0), new Vector3(1.60f, .08f, .61f), "Walnut panels", true);
            foreach (int side in new[] { -1, 1 })
                Box("Service shelf side", shelf.transform, new Vector3(side * .77f, 1.02f, 0), new Vector3(.08f, 2.04f, .62f), "Mahogany", true);
            station.stockLabel = Text("Visible finite service stock", shelf.transform, blanket ? "CLEAN BLANKETS\n3 AVAILABLE" : "REPLACEMENT BULBS\n3 AVAILABLE",
                new Vector3(0, 1.84f, -.32f), .094f, Lettering);
            var grab = EnsureGrabConfiguration();
            // Six authored slots support debug/tuning up to six; the default shift exposes only three model items.
            for (int i = 0; i < 6; i++)
            {
                var anchor = Group((blanket ? "BlanketSource" : "BulbSource") + i, shelf.transform,
                    new Vector3(i % 2 == 0 ? -.36f : .36f, .71f + i / 2 * .46f, -.13f)).transform;
                var obj = Group((blanket ? "Extra blanket " : "Replacement bulb ") + i, items);
                obj.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                var body = obj.AddComponent<Rigidbody>(); body.mass = blanket ? .65f : .22f; body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll; body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var shape = obj.AddComponent<BoxCollider>(); shape.size = blanket ? new Vector3(.58f, .23f, .42f) : new Vector3(.24f, .28f, .24f);
                var pickup = obj.AddComponent<PhysicsPickup>(); pickup.itemName = blanket ? "Extra blanket" : "Replacement light bulb";
                pickup.holdDistance = 1.2f; pickup.grabConfig = grab;
                if (blanket)
                {
                    for (int fold = 0; fold < 3; fold++)
                        Box("Folded spare blanket", obj.transform, new Vector3(0, -.075f + fold * .071f, 0), new Vector3(.57f - fold * .025f, .07f, .40f), "Teal upholstery", true, false);
                    Box("Spare blanket cloth band", obj.transform, new Vector3(0, 0, -.209f), new Vector3(.17f, .225f, .014f), "Cream linen", true, false);
                    Text("Blanket item label", obj.transform, "BLANKET", new Vector3(0, 0, -.221f), .033f, Mat("Ink").color);
                }
                else
                {
                    Box("Bulb protective carton", obj.transform, Vector3.zero, new Vector3(.235f, .275f, .235f), "Gauge ivory", true, false);
                    Sphere("Bulb glass in open carton", obj.transform, new Vector3(0, .145f, 0), Vector3.one * .145f, "Cream linen");
                    Text("Bulb carton label", obj.transform, "BULB", new Vector3(0, -.017f, -.12f), .052f, Mat("Ink").color);
                }
                var item = obj.AddComponent<ServiceSupplyItem>(); item.itemId = (blanket ? "blanket:" : "bulb:") + i; item.sourceAnchor = anchor;
            }
        }

        static void BuildServiceLuggage(Transform root, Transform items)
        {
            // BuildLobby already creates the functional cart. Keep it when adding luggage storage.
            // Keep the complete solid use volume clear of the z=.35 reception arrival lane,
            // including the guest capsule radius at the fifth and sixth waiting positions.
            var area = Group("Guest luggage storage area", root, new Vector3(-8.1f, 0, -1.10f));
            var zone = area.AddComponent<LuggageStorageZone>(); zone.displayName = "Guest luggage storage";
            Box("Luggage area platform", area.transform, new Vector3(0, .12f, 0), new Vector3(2.05f, .24f, 1.87f), "Mahogany", true);
            Box("Luggage storage signpost", area.transform, new Vector3(-.97f, 1.05f, .92f), new Vector3(.09f, 1.90f, .09f), "Aged brass", true);
            Box("Luggage storage sign backing", area.transform, new Vector3(0, 1.9f, .92f), new Vector3(2.08f, .47f, .065f), "Teal upholstery", true, false);
            Text("Luggage storage sign", area.transform, "LUGGAGE STORAGE\nPUT DOWN HERE · COLLECT LATER", new Vector3(0, 1.9f, .875f), .075f, Lettering);
            var interact = area.AddComponent<BoxCollider>(); interact.center = new Vector3(0, .85f, 0); interact.size = new Vector3(2.30f, 1.65f, 2.10f); interact.isTrigger = true;
            // The whole platform is a use target; placement is a short deliberate action while carrying.
            zone.storageBounds = interact; zone.storageAnchors = new Transform[24];
            for (int i = 0; i < 24; i++)
            {
                zone.storageAnchors[i] = Group("StoredLuggageAnchor" + i, area.transform,
                    new Vector3(i % 2 == 0 ? -.47f : .47f, .62f, -.61f + (i / 2) * .61f)).transform;
                var source = Group("LuggageRecoveryAnchor" + i, root,
                    new Vector3(-2.0f + (i % 2) * .8f, .43f, -.50f + (i / 2) * .8f)).transform;
                var obj = Group("Guest service suitcase slot " + i, items, source.position);
                var body = obj.AddComponent<Rigidbody>(); body.mass = 5; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
                body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var shape = obj.AddComponent<BoxCollider>(); shape.size = new Vector3(.71f, .68f, .38f);
                var pickup = obj.AddComponent<PhysicsPickup>(); pickup.itemName = "Guest luggage · offer help to the owner"; pickup.holdDistance = 1.30f; pickup.grabConfig = EnsureGrabConfiguration();
                Box("Guest luggage case", obj.transform, Vector3.zero, new Vector3(.70f, .64f, .37f), i % 2 == 0 ? "Luggage mustard" : "Burgundy velvet", true, false);
                Box("Luggage handle", obj.transform, new Vector3(0, .365f, 0), new Vector3(.29f, .105f, .09f), "Aged brass", true, false);
                foreach (int side in new[] { -1, 1 })
                    Box("Guest case leather strap", obj.transform, new Vector3(side * .21f, 0, -.192f), new Vector3(.085f, .64f, .018f), "Mahogany", true, false);
                Box("Guest identity label", obj.transform, new Vector3(0, .02f, -.202f), new Vector3(.32f, .23f, .018f), "Cream linen", true, false);
                var item = obj.AddComponent<ServiceSupplyItem>(); item.luggageSlot = i; item.sourceAnchor = source; item.storageZone = zone;
                item.identityLabel = Text("Guest luggage identity", obj.transform, "LUGGAGE", new Vector3(0, .02f, -.216f), .053f, Mat("Ink").color);
            }
        }

        static void BuildReceptionServiceControls(Transform root)
        {
            var board = Group("Reception guest service board", root, new Vector3(-3.50f, 1.42f, 2.60f));
            var control = board.AddComponent<ReceptionServiceBoardInteraction>(); control.displayName = "Guest service board";
            Box("Service board foot", board.transform, new Vector3(0, .02f, 0), new Vector3(1.16f, .07f, .49f), "Mahogany", true);
            Box("Service board frame", board.transform, new Vector3(0, .46f, .12f), new Vector3(1.19f, .87f, .095f), "Mahogany", true);
            Box("Service board face", board.transform, new Vector3(0, .46f, .063f), new Vector3(1.08f, .76f, .02f), "Gauge ivory", true, false);
            control.summary = Text("Reception service board summary", board.transform, "GUEST SERVICES\nNOW + UPCOMING", new Vector3(0, .47f, .046f), .077f, Mat("Ink").color);
            var phone = Group("Reception wake-up telephone", root, new Vector3(-6.75f, 1.48f, 2.36f));
            var calls = phone.AddComponent<ReceptionPhoneInteraction>(); calls.displayName = "Reception wake-up telephone";
            var incoming = phone.AddComponent<IncomingServicePhoneCue>();
            incoming.ringIndicator = Sphere("Incoming service call lamp", phone.transform, new Vector3(.24f, .20f, -.16f),
                Vector3.one * .07f, "Signal green");
            incoming.ringIndicator.SetActive(false);
            var hit = phone.AddComponent<BoxCollider>(); hit.center = new Vector3(0, .12f, 0); hit.size = new Vector3(.70f, .38f, .51f);
            // Existing complaint-phone builder supplies the same handset, dial, lamp and audio anchor here.
            calls.phoneLabel = Text("Wake telephone status", phone.transform, "RECEPTION PHONE\nNO CALLS PROMISED", new Vector3(0, .11f, -.235f), .052f, Lettering);
        }

        static void BuildRoomServiceControls(Transform root, int roomId, int side, float z)
        {
            var luggageMat = Group("Inside room luggage acceptance " + roomId, root, new Vector3(side * 4.2f, .015f, z - 1.25f));
            var luggageZone = luggageMat.AddComponent<LuggageDeliveryZone>(); luggageZone.roomId = roomId;
            luggageZone.size = new Vector3(1.8f, 1.5f, 1.65f);
            // The knocker is on door-local +X. Mirror the whole shelf onto the other jamb;
            // using the same world -Z side put right-room shelves behind their knockers.
            // The shelf also stays outside the solid leaf's full +/-105-degree sweep.
            // WallPanel's doubled collider depth puts its corridor face at |x|=1.83;
            // the whole shelf, use box and parcel must sit in front of that real surface.
            var parcelPoint = Group("Room blanket drop-off " + roomId, root, new Vector3(side * 1.74f, .48f, z + side * 1.65f));
            parcelPoint.transform.localRotation = Quaternion.Euler(0, side * 90, 0);
            var drop = parcelPoint.AddComponent<RoomBlanketDropOffInteraction>();
            drop.roomId = roomId; drop.displayName = "Room " + roomId + " delivery shelf";
            drop.deliveryAnchor = Group("Blanket parcel anchor " + roomId, parcelPoint.transform, new Vector3(0, .17f, -.21f)).transform;
            var dropHit = parcelPoint.AddComponent<BoxCollider>(); dropHit.center = new Vector3(0, .49f, -.065f);
            dropHit.size = new Vector3(.68f, .25f, .14f);
            Box("Delivery shelf tray", parcelPoint.transform, new Vector3(0, -.04f, -.15f), new Vector3(.76f, .10f, .43f), "Mahogany", true);
            Box("Delivery shelf back", parcelPoint.transform, new Vector3(0, .33f, 0), new Vector3(.70f, .60f, .06f), "Walnut panels", true, false);
            Text("Room delivery shelf label", parcelPoint.transform, "ROOM " + roomId + "\nDELIVERY", new Vector3(0, .45f, -.145f), .058f, Lettering);
            var radiator = GameObject.Find("Radiator" + roomId);
            var valveObject = Group("Radiator control " + roomId, radiator.transform, new Vector3(-1, 1.03f, -.27f));
            var valve = valveObject.AddComponent<RadiatorValveInteraction>(); valve.roomId = roomId; valve.displayName = "Room " + roomId + " radiator valve";
            var valveCollider = valveObject.AddComponent<BoxCollider>(); valveCollider.center = new Vector3(0, .04f, -.035f); valveCollider.size = new Vector3(.42f, .55f, .26f);
            valve.knob = Group("Radiator setting knob", valveObject.transform).transform;
            Cylinder("Knurled valve knob", valve.knob, Vector3.zero, .135f, .105f, "Aged brass", new Vector3(90, 0, 0));
            Box("Valve white pointer", valve.knob, new Vector3(0, .075f, -.06f), new Vector3(.027f, .085f, .02f), "Gauge ivory", false, false);
            valve.settingLabel = Text("Radiator readable setting", valveObject.transform, "RADIATOR 1 / 3\nLOW", new Vector3(0, .30f, -.08f), .060f, Mat("Ink").color);
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            var markers = presentation.roomMarkers.Single(marker => marker.roomId == roomId);
            var radiatorPosition = valveObject.transform.position - valveObject.transform.forward * 1.33f;
            radiatorPosition.y = .01f;
            markers.radiatorAnchor = Group("RadiatorAnchor" + roomId, root, radiatorPosition).transform;
            markers.radiatorTarget = valveObject.transform;
            var room = GameObject.Find("Room" + roomId).transform;
            // Open a genuine walking aisle between the foot of the bed and the armchair.
            // The old chair left less than a guest-capsule diameter on the valve approach.
            var chairSeat = room.GetComponentsInChildren<Transform>().FirstOrDefault(part => part.name == "Chair seat");
            if (chairSeat)
            {
                var position = chairSeat.parent.localPosition; position.z = -2.25f;
                chairSeat.parent.localPosition = position;
            }
            var lamp = Group("Bedside service lamp " + roomId, room, new Vector3(-side * .85f, .79f, 1.35f));
            var lightControl = lamp.AddComponent<RoomLampInteraction>(); lightControl.roomId = roomId; lightControl.displayName = "Room " + roomId + " bedside lamp";
            var lampCollider = lamp.AddComponent<BoxCollider>(); lampCollider.center = new Vector3(0, .29f, 0); lampCollider.size = new Vector3(.40f, .62f, .37f);
            Cylinder("Bedside lamp foot", lamp.transform, Vector3.zero, .18f, .06f, "Aged brass");
            Cylinder("Bedside lamp stem", lamp.transform, new Vector3(0, .19f, 0), .035f, .36f, "Aged brass");
            lightControl.bulbSurface = Sphere("Service lamp glass", lamp.transform, new Vector3(0, .43f, 0), new Vector3(.31f, .32f, .31f), "Warm lamp").GetComponent<Renderer>();
            lightControl.bulbLight = Group("Working bedside point light", lamp.transform, new Vector3(0, .43f, 0)).AddComponent<Light>();
            lightControl.bulbLight.type = LightType.Point; lightControl.bulbLight.color = new Color(1, .78f, .42f); lightControl.bulbLight.intensity = .65f;
            lightControl.bulbLight.range = 2.4f; lightControl.bulbLight.shadows = LightShadows.None;
            lightControl.statusLabel = Text("Bedside lamp status", lamp.transform, "ROOM " + roomId + " LAMP\nWORKING", new Vector3(0, .07f, -.20f), .043f, Lettering);
            var bed = room.GetComponentsInChildren<LinenBedInteraction>().Single();
            var delivery = Group("Blanket delivery " + roomId, bed.transform, new Vector3(-side * 1.08f, .82f, .25f));
            var blanket = delivery.AddComponent<RoomBlanketDeliveryInteraction>(); blanket.roomId = roomId; blanket.displayName = "Room " + roomId + " extra blanket";
            var target = delivery.AddComponent<BoxCollider>(); target.size = new Vector3(.17f, .37f, .55f);
            Box("Blanket delivery marker", delivery.transform, Vector3.zero, new Vector3(.11f, .35f, .53f), "Teal upholstery", true, false);
            Text("Blanket bed label", delivery.transform, "EXTRA\nBLANKET", new Vector3(-side * .091f, 0, 0), .064f, Lettering, new Vector3(0, side * 90, 0));
            blanket.deliveredBlanket = Box("Delivered extra blanket " + roomId, bed.transform, new Vector3(0, 1.13f, -.62f), new Vector3(1.76f, .08f, .43f), "Teal upholstery", true, false);
            blanket.deliveredBlanket.SetActive(false);
        }
    }
}
