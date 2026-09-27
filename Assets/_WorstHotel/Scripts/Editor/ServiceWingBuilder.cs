using UnityEditor;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static readonly Vector3 BoilerOffset = new Vector3(25, 0, -30.7f);
        static readonly Vector3 ServicePanelPosition = new Vector3(28.95f, 0, 2.3f);

        static void BuildServiceWing(Transform environment)
        {
            var wing = Group("Lobby service wing", environment).transform;
            ServiceFloor(wing, "Service hall", new Vector3(19.75f, 0, -1.7f), 19.5f, 3);
            ServiceFloor(wing, "Laundry", new Vector3(13.1f, 0, 3.3f), 6.2f, 7);
            ServiceFloor(wing, "Staff sleeping room", new Vector3(18.25f, 0, 2.3f), 4.1f, 5);
            ServiceFloor(wing, "Boiler room", new Vector3(24.9f, 0, 4.65f), 9.2f, 9.7f);
            ServiceWall(wing, new Vector3(19.75f, 0, -3.2f), 19.5f);
            ServiceWall(wing, new Vector3(29.5f, 0, 3.15f), 12.7f, 90);
            ServiceWall(wing, new Vector3(13.1f, 0, 6.8f), 6.2f);
            ServiceWall(wing, new Vector3(18.25f, 0, 4.8f), 4.1f);
            ServiceWall(wing, new Vector3(24.9f, 0, 9.5f), 9.2f);
            ServiceWall(wing, new Vector3(16.2f, 0, 3.3f), 7, 90);
            ServiceWall(wing, new Vector3(20.3f, 0, 4.65f), 9.7f, 90);

            ServiceDoor(wing, "Staff entrance", "STAFF\nONLY", new Vector3(10, 0, -1.6f), 90, 1.35f);
            ServiceFront(wing, 10, 16.2f, 13.3f, 1.35f, "LAUNDRY / SUPPLIES");
            ServiceFront(wing, 16.2f, 20.3f, 19.1f, 1, "STAFF SLEEPING ROOM", true);
            ServiceFront(wing, 20.3f, 29.5f, 24.8f, 1.35f, "BOILER / ELECTRICAL");
            Sign(wing, "STAFF ONLY", new Vector3(9.70f, 3.48f, -1.6f), 2.55f, .34f, .14f, 90);
            Sign(wing, "LOBBY", new Vector3(10.30f, 3.48f, -1.6f), 1.4f, .34f, .12f, -90);

            foreach (float x in new[] { 12.1f, 18.6f, 26.5f })
                ServiceCeilingLamp(wing, "Service corridor light", new Vector3(x, 3.5f, -1.65f), 1.55f, 5.5f);
            ServiceCeilingLamp(wing, "Laundry work light", new Vector3(13.1f, 3.5f, 3.3f), 2.5f, 7);
            foreach (var position in new[] { new Vector3(15.5f, 3.76f, -1.6f), new Vector3(23.1f, 3.76f, -1.6f),
                new Vector3(13.2f, 3.76f, 4.4f), new Vector3(24.6f, 3.76f, 3.6f) })
                ServiceRooflight(wing, position);

            var folding = Group("Laundry folding table", wing, new Vector3(10.8f, 0, 4.3f), new Vector3(0, -90, 0)).transform;
            Box("Folding worktop", folding, new Vector3(0, .91f, 0), new Vector3(1.75f, .12f, .65f), "Cream linen", true);
            foreach (float x in new[] { -.69f, .69f })
                Box("Folding table leg", folding, new Vector3(x, .43f, 0), new Vector3(.12f, .86f, .5f), "Pipe iron");
            Sign(wing, "HEATERS\nTAKE BY HANDLE", new Vector3(10.24f, 2.10f, 2.1f), 1.7f, .54f, .09f, -90);
            Sign(wing, "SORT · FOLD · RESTOCK", new Vector3(13.1f, 3.16f, 6.64f), 3.4f, .42f, .13f);
            Box("Laundry wall repair", wing, new Vector3(15.98f, 1.92f, 2.3f), new Vector3(.024f, .67f, .88f), "New plaster patch", false, false);
            Box("Service hall paint repair", wing, new Vector3(17.1f, 1.6f, -3.078f), new Vector3(.73f, .44f, .025f), "New plaster patch", false, false);
        }

        static void ServiceFloor(Transform parent, string name, Vector3 center, float width, float depth)
        {
            Box(name + " tile floor", parent, center + Vector3.down * .16f, new Vector3(width, .32f, depth), "Utility tile");
            Box(name + " ceiling", parent, center + Vector3.up * 3.91f, new Vector3(width + .12f, .22f, depth + .12f), "Cream linen");
            // Grout stays inside each room's footprint rather than crossing its doors or neighbouring walls.
            for (float x = center.x - width * .5f + 1; x < center.x + width * .5f; x += 1)
                Box("Service floor grout", parent, new Vector3(x, .005f, center.z), new Vector3(.018f, .008f, depth), "Pipe iron", false, false);
            for (float z = center.z - depth * .5f + 1; z < center.z + depth * .5f; z += 1)
                Box("Service floor grout", parent, new Vector3(center.x, .005f, z), new Vector3(width, .008f, .018f), "Pipe iron", false, false);
        }

        static void ServiceWall(Transform parent, Vector3 position, float length, float yaw = 0)
        {
            var section = Group("Thick service partition", parent, position, new Vector3(0, yaw, 0)).transform;
            Box("Service plaster wall", section, new Vector3(0, 1.9f, 0), new Vector3(length, 3.8f, .22f), "Cream plaster");
            foreach (int side in new[] { -1, 1 })
            {
                Box("Washable lower wall", section, new Vector3(0, .68f, side * .116f), new Vector3(length, 1.34f, .014f), "Gallery sage", false, false);
                Box("Service skirting", section, new Vector3(0, .085f, side * .133f), new Vector3(length, .17f, .048f), "Pipe iron", false, false);
                Box("Paint dividing rail", section, new Vector3(0, 1.36f, side * .128f), new Vector3(length, .045f, .035f), "Ivory moulding", false, false);
            }
        }

        static DoorInteractable ServiceDoor(Transform parent, string name, string label, Vector3 position, float yaw, float width)
        {
            var instance = Place(door, parent, position, new Vector3(0, yaw, 0));
            instance.name = name; instance.transform.localScale = new Vector3(width, 1, 1);
            var interaction = instance.GetComponent<DoorInteractable>();
            interaction.displayName = label.Replace('\n', ' '); interaction.roomId = 0;
            interaction.openAwayFromPlayer = false; interaction.openAngle = yaw == 180 ? 90 : -100;
            Sign(interaction.doorPivot, label, new Vector3(.755f, 2.2f, -.145f), 1.12f, .49f, .075f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(interaction);
            return interaction;
        }

        static void ServiceFront(Transform parent, float left, float right, float doorway, float scale, string label, bool rightHinge = false)
        {
            float halfGap = 1.04f * scale;
            ServiceWall(parent, new Vector3((left + doorway - halfGap) * .5f, 0, -.2f), doorway - halfGap - left);
            ServiceWall(parent, new Vector3((right + doorway + halfGap) * .5f, 0, -.2f), right - doorway - halfGap);
            Box("Service door lintel", parent, new Vector3(doorway, 3.51f, -.2f), new Vector3(halfGap * 2, .58f, .22f), "Cream plaster");
            ServiceDoor(parent, label + " door", label, new Vector3(doorway, 0, -.2f), rightHinge ? 180 : 0, scale);
            Sign(parent, label, new Vector3(doorway, 3.48f, -.45f), halfGap * 2, .34f, .080f);
        }

        static void ServiceCeilingLamp(Transform parent, string name, Vector3 position, float intensity, float range)
        {
            var fixture = Group(name + " fixture", parent, position).transform;
            Box("Work light housing", fixture, Vector3.zero, new Vector3(1.1f, .15f, .40f), "Pipe iron", true, false);
            Box("Work light diffuser", fixture, new Vector3(0, -.1f, 0), new Vector3(.92f, .06f, .29f), "Warm lamp", true, false);
            DisableFixtureShadows(fixture);
            PointLight(parent, name, position + Vector3.down * .27f, new Color(.91f, .94f, 1), intensity, range);
        }

        static void ServiceRooflight(Transform parent, Vector3 position)
        {
            var roof = Group("Service glazed rooflight", parent, position).transform;
            Box("Rooflight reveal", roof, Vector3.zero, new Vector3(1.35f, .12f, 1.55f), "Pipe iron", false, false);
            Box("Rooflight glazing", roof, new Vector3(0, -.07f, 0), new Vector3(1.14f, .018f, 1.34f), "Window blue", false, false);
            Box("Rooflight bar", roof, new Vector3(0, -.09f, 0), new Vector3(.05f, .05f, 1.37f), "Ivory moulding", false, false);
            DisableFixtureShadows(roof);
            ExteriorLight(parent, position + Vector3.down * .34f, 1.1f, 7);
        }

        static void DressGuestConnection(Transform parent)
        {
            foreach (int side in new[] { -1, 1 })
            {
                Box("Gallery runner border", parent, new Vector3(side * 1.49f, .008f, 34.5f), new Vector3(.07f, .01f, 11), "Aged brass", false, false);
                Window(parent, new Vector3(side * 1.94f, 2.2f, 34.5f), side * 90);
                foreach (float z in new[] { 31f, 38f })
                {
                    Place(lamp, parent, new Vector3(side * 1.80f, 2.24f, z), new Vector3(0, side * 90, 0));
                    PointLight(parent, "Gallery connecting light", new Vector3(side * 1.45f, 2.40f, z), new Color(1, .88f, .72f), 1.8f, 6);
                }
            }
            foreach (float z in new[] { 32.4f, 36.6f, 39.65f })
            {
                Place(pier, parent, new Vector3(-1.93f, 0, z));
                Place(pier, parent, new Vector3(1.93f, 0, z));
                Box("Gallery ceiling beam", parent, new Vector3(0, 3.55f, z), new Vector3(4.14f, .22f, .4f), "Ivory moulding");
            }
        }
    }
}
