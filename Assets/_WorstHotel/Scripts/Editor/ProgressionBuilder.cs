using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void BuildNorthWingShell(Transform environment)
        {
            var wing = Group("North Wing architecture", environment).transform;
            Box("North Wing carpet", wing, new Vector3(0, -.16f, 48.5f), new Vector3(20, .32f, 17), "Gallery carpet");
            Box("North Wing ceiling", wing, new Vector3(0, 3.91f, 48.5f), new Vector3(20.3f, .22f, 17.3f), "Cream linen");
            WallRun(wing, new Vector3(0, 0, 57), 20);
            foreach (int side in new[] { -1, 1 })
            {
                WallRun(wing, new Vector3(side * 10, 0, 48.5f), 17, 90);
                WallRun(wing, new Vector3(side * 6.075f, 0, 42.5f), 7.85f);
                WallRun(wing, new Vector3(side * 6.075f, 0, 49.5f), 7.85f);
                // Close the shoulders beside the narrow guest gallery.
                WallRun(wing, new Vector3(side * 6.075f, 0, 40), 7.85f);
                float previous = 40f;
                foreach (float z in new[] { 46f, 53f })
                {
                    float before = z - 1.04f;
                    WallRun(wing, new Vector3(side * 2.15f, 0, (before + previous) * .5f), before - previous, 90);
                    Box("Wing door lintel", wing, new Vector3(side * 2.15f, 3.51f, z), new Vector3(.32f, .58f, 2.08f), "Cream plaster");
                    previous = z + 1.04f;
                }
                WallRun(wing, new Vector3(side * 2.15f, 0, (previous + 57) * .5f), 57 - previous, 90);
                Box("North runner brass border", wing, new Vector3(side * 1.49f, .008f, 48.5f), new Vector3(.07f, .01f, 17), "Aged brass", false, false);
            }
            Pipe("North heating supply", wing, new Vector3(1.58f, 3.15f, 40.1f), new Vector3(1.58f, 3.15f, 56.6f), .11f, "Repair copper");
            DressNorthGallery(wing);
        }

        static void RelocateBoilerAssembly()
        {
            // Relocate the complete authored assembly together, including interaction and feedback anchors.
            foreach (string name in new[] { "Utility fittings", "Boiler", "ValveAnchor", "PanelAnchor", "BreakerAnchor",
                "LatchAAnchor", "LatchBAnchor", "RestartAnchor", "GaugeAnchor", "SteamAnchor",
                "Pressure warning beacon", "Boiler capacity display", "Upgraded boiler burner" })
            {
                var obj = GameObject.Find(name); if (obj) obj.transform.position += BoilerOffset;
            }
        }

        static void AddProgressionPresentation(GameObject gameplay, GameObject environment)
        {
            var view = gameplay.AddComponent<HotelProgressionPresentation>();
            var barrier = Group("North Wing locked construction barrier", environment.transform, new Vector3(0, 0, 40));
            view.wingBarrier = barrier;
            Box("Restoration barrier collision", barrier.transform, new Vector3(0, 1.43f, 0), new Vector3(4.18f, 2.86f, .3f), "Walnut panels");
            foreach (float y in new[] { .55f, 1.25f, 2.15f })
            {
                var board = Box("Brass construction brace", barrier.transform, new Vector3(0, y, -.20f), new Vector3(4.05f, .16f, .08f), "Luggage mustard", false, false);
                board.transform.localEulerAngles = new Vector3(0, 0, y < 1 ? -8 : 8);
            }
            view.wingSign = Text("North Wing status", environment.transform, "NORTH WING / CLOSED", new Vector3(0, 3.18f, 39.62f), .10f, Lettering);
            var lobbyNotice = Group("Renovation notice at reception", gameplay.transform, new Vector3(-7.12f, 1.45f, 1.75f));
            Box("Renovation notice backing", lobbyNotice.transform, new Vector3(0, .3f, 0), new Vector3(1.30f, .75f, .06f), "Mahogany", true, false);
            view.lobbySign = Text("Renovation summary", lobbyNotice.transform, "RENOVATION LEDGER", new Vector3(0, .3f, -.04f), .070f, Lettering);

            var burner = Group("Upgraded boiler burner", gameplay.transform, new Vector3(-1.90f, 0, 36.35f));
            view.boilerBurner = burner;
            Box("New burner casing", burner.transform, new Vector3(0, .85f, 0), new Vector3(.78f, 1.15f, .58f), "Boiler enamel", true);
            Pipe("New burner connection", burner.transform, new Vector3(0, 1.25f, .1f), new Vector3(0, 1.25f, .85f), .13f, "Repair copper");
            Text("Burner upgrade plate", burner.transform, "CAPACITY\nUPGRADED", new Vector3(0, 1, -.31f), .069f, Lettering);
            var insulation = Group("Room 102 permanent window seals", GameObject.Find("Room102").transform, new Vector3(3.65f, 2.2f, .1f));
            view.insulatedWindow = insulation;
            foreach (float edge in new[] { -1f, 1f })
            {
                Box("New insulated window upright", insulation.transform, new Vector3(-.04f, 0, edge * .94f), new Vector3(.09f, 1.9f, .075f), "Ivory moulding", false, false);
                Box("New insulated window rail", insulation.transform, new Vector3(-.04f, edge * .94f, 0), new Vector3(.09f, .075f, 1.95f), "Ivory moulding", false, false);
            }
            Text("Window works plate", insulation.transform, "DOUBLE SEALED", new Vector3(-.10f, -.72f, 0), .07f, Lettering, new Vector3(0, 90, 0));
            for (int i = 0; i < 2; i++)
            {
                var box = Group("Upgraded circuit " + (i == 0 ? "A" : "B"), gameplay.transform,
                    ServicePanelPosition + Quaternion.Euler(0, 90, 0) * new Vector3(-.5f + i, 3.35f, -.1f), new Vector3(0, 90, 0));
                Box("New circuit capacity module", box.transform, Vector3.zero, new Vector3(.74f, .35f, .22f), "Repair copper", true, false);
                Text("Circuit module plate", box.transform, (i == 0 ? "A" : "B") + " +2.50", new Vector3(0, 0, -.13f), .074f, Lettering);
                if (i == 0) view.electricalA = box; else view.electricalB = box;
            }
            var wingLights = Group("North Wing corridor lights", environment.transform); view.wingCorridorLights = wingLights;
            foreach (float z in new[] { 41.8f, 48.1f, 55.1f })
                foreach (float side in new[] { -1.45f, 1.45f })
                    PointLight(wingLights.transform, "Hall warm fill", new Vector3(side, 2.40f, z), new Color(1, .88f, .72f), 1.8f, 6);
        }
    }
}
