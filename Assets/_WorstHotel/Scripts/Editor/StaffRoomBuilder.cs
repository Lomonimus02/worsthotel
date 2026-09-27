using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void AddStaffRoom(GameObject gameplay)
        {
            var room = Group("Staff room", gameplay.transform).transform;
            // This is a niche inside the existing utility shell. Explicit unit-box
            // colliders preserve the 0.12 m partitions; WallRun is physically thicker.
            foreach (float z in new[] { 32.80f, 36.00f })
            {
                Box("Staff room end partition", room, new Vector3(-5.24f, 1.30f, z),
                    new Vector3(2.88f, 2.60f, .12f), "Cream plaster");
                Box("Worn partition skirting", room, new Vector3(-5.24f, .10f, z),
                    new Vector3(2.88f, .20f, .125f), "Mahogany", false, false);
            }
            foreach (float z in new[] { 33.25f, 35.55f })
                Box("Staff room entry partition", room, new Vector3(-3.80f, 1.30f, z),
                    new Vector3(.12f, 2.60f, .90f), "Cream plaster");
            Box("Staff room entry lintel", room, new Vector3(-3.80f, 2.45f, 34.40f),
                new Vector3(.12f, .30f, 1.40f), "Cream plaster");
            Sign(room, "STAFF ROOM", new Vector3(-3.725f, 2.46f, 34.40f), 1.16f, .28f, .091f, -90);
            Box("Old plaster repair", room, new Vector3(-4.64f, 1.48f, 35.932f),
                new Vector3(.54f, .43f, .012f), "New plaster patch", false, false);

            BuildStaffCot(room, 0, 33.32f, new Vector3(-5.60f, 0, 34.10f));
            BuildStaffCot(room, 1, 35.48f, new Vector3(-4.65f, 0, 34.68f));

            var desk = Group("Small staff table", room, new Vector3(-4.11f, 0, 33.23f)).transform;
            Box("Worn table top", desk, new Vector3(0, .64f, 0), new Vector3(.40f, .08f, .55f), "Walnut panels", true);
            foreach (float x in new[] { -.15f, .15f })
                foreach (float z in new[] { -.22f, .22f })
                    Box("Table leg", desk, new Vector3(x, .31f, z), new Vector3(.055f, .62f, .055f), "Mahogany");
            Cylinder("Old staff mug", desk, new Vector3(-.03f, .744f, -.04f), .053f, .128f, "Cream linen");
            Cylinder("Mug dark rim", desk, new Vector3(-.03f, .81f, -.04f), .045f, .005f, "Ink");
            Pipe("Mug handle top", desk, new Vector3(.015f, .785f, -.04f), new Vector3(.056f, .785f, -.04f), .012f, "Cream linen");
            Pipe("Mug handle side", desk, new Vector3(.056f, .785f, -.04f), new Vector3(.056f, .729f, -.04f), .012f, "Cream linen");
            Pipe("Mug handle bottom", desk, new Vector3(.056f, .729f, -.04f), new Vector3(.015f, .729f, -.04f), .012f, "Cream linen");

            var lockers = Group("Staff lockers", room, new Vector3(-4.11f, 0, 35.49f)).transform;
            Box("Old locker cabinet", lockers, new Vector3(0, .80f, 0), new Vector3(.40f, 1.60f, .66f), "Teal upholstery", true);
            foreach (float z in new[] { -.16f, .16f })
            {
                Box("Locker door", lockers, new Vector3(-.204f, .83f, z), new Vector3(.018f, 1.42f, .285f), "Boiler enamel", true, false);
                Sphere("Locker brass handle", lockers, new Vector3(-.224f, .89f, z + .08f), new Vector3(.055f, .08f, .045f), "Aged brass");
                for (int index = 0; index < 3; index++)
                    Box("Locker vent", lockers, new Vector3(-.216f, 1.36f + index * .045f, z),
                        new Vector3(.006f, .012f, .16f), "Ink", false, false);
            }
            Sign(room, "STAFF NOTICE\nMUGS BACK ON THE TABLE\nLEAVE THE AISLE CLEAR", new Vector3(-4.75f, 1.96f, 35.925f),
                1.18f, .55f, .052f);
            var fixture = Group("Modest staff light", room, new Vector3(-5.65f, 2.16f, 35.91f)).transform;
            Box("Old brass lamp mount", fixture, Vector3.zero, new Vector3(.21f, .29f, .055f), "Aged brass", true, false);
            Sphere("Warm staff lamp", fixture, new Vector3(0, -.02f, -.095f), new Vector3(.28f, .20f, .18f), "Warm lamp");
            var glow = Group("Staff light glow", fixture, new Vector3(0, -.04f, -.19f)).AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, .78f, .50f);
            glow.intensity = 1.35f; glow.range = 3.4f; glow.shadows = LightShadows.None;
        }

        static void BuildStaffCot(Transform parent, int id, float z, Vector3 standingPosition)
        {
            var cot = Group("Staff bed " + (id + 1), parent, new Vector3(-5.40f, 0, z));
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
