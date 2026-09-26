using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void AddLivingGuestPresentation(GameObject gameplay)
        {
            var presentation = gameplay.GetComponent<GuestPresentation>();
            if (presentation == null) presentation = gameplay.AddComponent<GuestPresentation>();
            presentation.arrivalSpawn = GameObject.Find("GuestSpawn").transform;
            var anchors = Group("Living guest markers", gameplay.transform).transform;
            presentation.receptionPlaces = new Transform[6];
            for (int i = 0; i < 6; i++)
                presentation.receptionPlaces[i] = Group("ReceptionWait" + (i + 1), anchors,
                    new Vector3(-2.8f - i * 1.05f, .01f, 1.30f)).transform;

            presentation.roomMarkers = new GuestRoomMarkers[6];
            for (int i = 0; i < 6; i++)
            {
                int id = 101 + i, side = i % 2 == 0 ? -1 : 1;
                float z = 10 + i / 2 * 7;
                var room = GameObject.Find("Room" + id).transform;
                var markers = new GuestRoomMarkers
                {
                    roomId = id,
                    door = GameObject.Find("Door" + id).GetComponent<DoorInteractable>(),
                    roomTarget = GameObject.Find("RoomTarget" + id).transform,
                    rest = Group("RestAnchor" + id, anchors, new Vector3(side * 7.2f, .01f, z - 1.6f)).transform,
                    loud = Group("RoomLoud" + id, anchors, new Vector3(side * 7.2f, .01f, z - 1.6f)).transform,
                    shower = Group("ShowerAnchor" + id, anchors, new Vector3(side * 8.65f, .01f, z + 2.45f)).transform,
                    bedApproach = Group("BedApproach" + id, anchors, new Vector3(side * 5.0f, .01f, z - .75f)).transform,
                    bedAnchor = Group("BedAnchor" + id, anchors, new Vector3(side * 6.6f, 1.34f, z - .65f)).transform,
                    deskAnchor = Group("DeskAnchor" + id, anchors, new Vector3(side * 5.15f, .01f, z + 2.45f)).transform,
                    unpackAnchor = Group("UnpackAnchor" + id, anchors, new Vector3(side * 5.30f, .01f, z - 2.10f)).transform,
                    phoneAnchor = Group("PhoneAnchor" + id, anchors, new Vector3(side * 4.75f, .01f, z - 1.60f)).transform,
                    doorInsideAnchor = Group("DoorInsideAnchor" + id, anchors, new Vector3(side * 3.25f, .01f, z)).transform,
                    doorOutsideAnchor = Group("DoorOutsideAnchor" + id, anchors, new Vector3(side * 1.15f, .01f, z)).transform
                };
                markers.bedAnchor.rotation = Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-90, 0, 0);
                markers.bedApproach.rotation = Quaternion.Euler(0, side * 90, 0);
                markers.rest.rotation = Quaternion.Euler(0, side * 90, 0);
                markers.loud.rotation = Quaternion.Euler(0, side * 90, 0);
                markers.shower.rotation = Quaternion.Euler(0, side * 90, 0);
                markers.deskAnchor.rotation = Quaternion.identity;
                markers.unpackAnchor.rotation = Quaternion.Euler(0, 180, 0);
                markers.door.roomId = id;
                presentation.roomMarkers[i] = markers;

                var desk = Group("Guest writing desk " + id, room, new Vector3(-side * .85f, 0, 3.15f)).transform;
                Box("Writing desk top", desk, new Vector3(0, .90f, 0), new Vector3(1.32f, .10f, .55f), "Walnut panels", true, false);
                foreach (int leg in new[] { -1, 1 })
                    Box("Writing desk leg", desk, new Vector3(leg * .54f, .44f, .05f), new Vector3(.09f, .88f, .33f), "Mahogany", true, false);
                Box("Guest notebook", desk, new Vector3(0, .975f, -.06f), new Vector3(.45f, .045f, .29f), "Ivory moulding", false, false);
                Box("Notebook spine", desk, new Vector3(-.19f, 1.001f, -.06f), new Vector3(.035f, .01f, .30f), "Burgundy velvet", false, false);
                var caseRoot = Group("Guest room luggage " + id, room, new Vector3(-side * .70f, 0, -2.95f)).transform;
                Box("Open travel case", caseRoot, new Vector3(0, .26f, 0), new Vector3(.85f, .42f, .50f), "Luggage mustard", true, false);
                Box("Travel case lining", caseRoot, new Vector3(0, .48f, 0), new Vector3(.72f, .04f, .39f), "Burgundy velvet", false, false);
                Box("Folded travel shirt", caseRoot, new Vector3(.08f, .53f, 0), new Vector3(.40f, .06f, .27f), "Ivory moulding", false, false);

                // Small original fixtures fit the existing furniture; the clear north route skirts the bed.
                var shower = Group("Shower corner " + id, room, new Vector3(side * 2.65f, 0, 2.45f)).transform;
                Box("Shower tray", shower, new Vector3(0, .015f, 0), new Vector3(1.38f, .03f, 1.34f), "Ivory moulding", true, false);
                Box("Shower back tile", shower, new Vector3(side * .65f, 1.11f, 0), new Vector3(.075f, 2.22f, 1.40f), "Utility tile", true, false);
                Box("Shower privacy screen", shower, new Vector3(0, 1.05f, -.69f), new Vector3(1.4f, 2.10f, .065f), "Ivory moulding", true, false);
                Box("Shower end tile", shower, new Vector3(0, 1.05f, .69f), new Vector3(1.4f, 2.10f, .065f), "Utility tile", true, false);
                markers.showerCurtain = Box("Shower privacy curtain " + id, shower, new Vector3(-side * .68f, 1.07f, 0),
                    new Vector3(.045f, 1.95f, 1.31f), "Teal upholstery", false, false);
                markers.showerCurtain.SetActive(false);
                Pipe("Shower riser", shower, new Vector3(side * .51f, .70f, .22f), new Vector3(side * .51f, 2.12f, .22f), .032f, "Repair copper");
                Pipe("Shower arm", shower, new Vector3(side * .51f, 2.12f, .22f), new Vector3(0, 2.12f, .22f), .032f, "Aged brass");
                Cylinder("Rain head", shower, new Vector3(0, 2.06f, .22f), .17f, .07f, "Aged brass");
                Sphere("Mixer tap", shower, new Vector3(side * .48f, 1.04f, .22f), Vector3.one * .15f, "Aged brass");
                markers.showerWater = Group("Shower water " + id, shower);
                for (int stream = 0; stream < 5; stream++)
                    Cylinder("Water stream", markers.showerWater.transform,
                        new Vector3((stream - 2) * .06f, 1.29f, .22f), .009f, 1.4f, "Window blue");
                for (int puff = 0; puff < 3; puff++)
                    Sphere("Warm shower steam", markers.showerWater.transform,
                        new Vector3((puff - 1) * .24f, 2.14f + puff * .12f, -.08f),
                        new Vector3(.38f, .22f, .30f), "Ivory moulding");
                markers.showerWater.SetActive(false);

                var radio = Group("Room radio " + id, room, new Vector3(side * 2.72f, .69f, -.61f)).transform;
                Box("Television cabinet", radio, Vector3.zero, new Vector3(.72f, .48f, .33f), "Walnut panels", true, false);
                Box("Television screen", radio, new Vector3(-.03f, 0, -.183f), new Vector3(.49f, .32f, .028f), "Window blue", true, false);
                Box("Television picture", radio, new Vector3(-.06f, -.07f, -.202f), new Vector3(.25f, .11f, .015f), "Teal upholstery", false, false);
                Cylinder("Radio feet", radio, new Vector3(0, -.46f, 0), .10f, .54f, "Mahogany");
                markers.loudIndicator = Sphere("Radio playing " + id, radio, new Vector3(.24f, .09f, -.18f), Vector3.one * .065f, "Signal green");
                markers.loudIndicator.SetActive(false);
            }
        }
    }
}
