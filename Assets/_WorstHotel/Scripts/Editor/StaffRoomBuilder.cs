using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void AddStaffRoom(GameObject gameplay)
        {
            var room = Group("Staff room", gameplay.transform).transform;
            BuildStaffCot(room, 0, 1.10f, new Vector3(19.0f, 0, 1.35f));
            BuildStaffCot(room, 1, 3.80f, new Vector3(19.0f, 0, 3.55f));
            var table = Group("Small staff table", room, new Vector3(19.57f, 0, 4.13f)).transform;
            Box("Worn table top", table, new Vector3(0, .64f, 0), new Vector3(.55f, .08f, .55f), "Walnut panels", true);
            foreach (float x in new[] { -.2f, .2f })
                foreach (float z in new[] { -.2f, .2f })
                    Box("Table leg", table, new Vector3(x, .31f, z), new Vector3(.055f, .62f, .055f), "Mahogany");
            Cylinder("Old staff mug", table, new Vector3(0, .744f, 0), .053f, .128f, "Cream linen");
            var lockers = Group("Staff lockers", room, new Vector3(16.64f, 0, 2.43f)).transform;
            Box("Old locker cabinet", lockers, new Vector3(0, .80f, 0), new Vector3(.44f, 1.60f, .80f), "Teal upholstery", true);
            foreach (float z in new[] { -.19f, .19f })
            {
                Box("Locker door", lockers, new Vector3(.23f, .83f, z), new Vector3(.018f, 1.42f, .35f), "Boiler enamel", true, false);
                Sphere("Locker handle", lockers, new Vector3(.26f, .89f, z + .07f), new Vector3(.055f, .08f, .045f), "Aged brass");
            }
            Box("Staff room patched plaster", room, new Vector3(17.2f, 1.83f, 4.675f), new Vector3(.82f, .62f, .024f), "New plaster patch", false, false);
            Sign(room, "STAFF NOTICE\nPLEASE LET THE NEXT SHIFT SLEEP", new Vector3(18.2f, 2.35f, 4.64f), 2.4f, .55f, .080f);
            var fixture = Group("Modest staff light", room, new Vector3(18.1f, 2.8f, 4.63f)).transform;
            Box("Old lamp mount", fixture, Vector3.zero, new Vector3(.21f, .29f, .055f), "Pipe iron", true, false);
            Sphere("Warm staff lamp", fixture, new Vector3(0, -.02f, -.12f), new Vector3(.28f, .20f, .18f), "Warm lamp");
            DisableFixtureShadows(fixture);
            PointLight(room, "Staff room lamp", new Vector3(18.1f, 2.70f, 4.32f), new Color(1, .87f, .70f), 1.15f, 5.5f);
        }
        static void BuildStaffCot(Transform parent, int id, float z, Vector3 standingPosition)
        {
            var cot = Group("Staff bed " + (id + 1), parent, new Vector3(17.65f, 0, z));
            var interaction = cot.AddComponent<StaffBedInteraction>();
            interaction.bedId = id; interaction.displayName = "Staff bed " + (id + 1);
            // All solid cot parts belong to the same interaction root. Headboard and
            // mattress remain inside the total 2.00 x 0.82 m footprint, leaving a 1.34 m aisle.
            Box("Cot wooden base", cot.transform, new Vector3(0, .24f, 0), new Vector3(1.96f, .36f, .78f), "Mahogany", true);
            Box("Cot mattress", cot.transform, new Vector3(0, .49f, 0), new Vector3(1.92f, .18f, .78f), "Cream linen", true);
            Box("Low west headboard", cot.transform, new Vector3(-.97f, .50f, 0), new Vector3(.06f, .94f, .82f), "Walnut panels", true);
            Box("Used teal blanket", cot.transform, new Vector3(.19f, .602f, 0), new Vector3(1.45f, .045f, .74f), "Teal upholstery", true, false);
            Box("Folded blanket edge", cot.transform, new Vector3(.54f, .633f, 0), new Vector3(.22f, .035f, .73f), "Teal upholstery", true, false);
            Box("Staff pillow", cot.transform, new Vector3(-.69f, .64f, 0), new Vector3(.38f, .13f, .56f), "Cream linen", true, false);
            interaction.interactionTarget = Group("Bed use target", cot.transform,
                new Vector3(-.10f, .56f, id == 0 ? .30f : -.30f)).transform;
            interaction.standingAnchor = Group("Staff bed " + (id + 1) + " clear standing anchor", parent, standingPosition).transform;
        }
    }
}
