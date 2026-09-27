using System.Linq;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void AddElectricalPanel(GameObject gameplay)
        {
            var cabinet = Group("Room electrical panel", gameplay.transform, ServicePanelPosition, new Vector3(0, 90, 0));
            var panel = cabinet.AddComponent<ElectricalPanelPresentation>();
            Box("Electrical cabinet back", cabinet.transform, new Vector3(0, 1.75f, .15f), new Vector3(2.2f, 2.05f, .37f), "Boiler enamel", true);
            Box("Electrical inner mounting plate", cabinet.transform, new Vector3(0, 1.75f, -.05f), new Vector3(2.05f, 1.91f, .035f), "Gauge ivory", true, false);
            foreach (int sign in new[] { -1, 1 })
            {
                Box("Cabinet raised side", cabinet.transform, new Vector3(sign * 1.04f, 1.75f, -.12f), new Vector3(.12f, 2.03f, .36f), "Pipe iron", true);
                Box("Panel stand leg", cabinet.transform, new Vector3(sign * .80f, .42f, .12f), new Vector3(.17f, .84f, .22f), "Pipe iron", true);
                Box("Panel stand foot", cabinet.transform, new Vector3(sign * .80f, .08f, .07f), new Vector3(.42f, .16f, .92f), "Pipe iron", true);
            }
            Box("Circuit divider", cabinet.transform, new Vector3(0, 1.70f, -.12f), new Vector3(.055f, 1.68f, .10f), "Pipe iron", false, false);
            var pivot = Group("Electrical cabinet hinged cover", cabinet.transform, new Vector3(-1.10f, 1.75f, -.68f));
            Box("Solid cover", pivot.transform, new Vector3(1.10f, 0, 0), new Vector3(2.20f, 2.04f, .08f), "Boiler enamel", true);
            Box("Cover brass frame", pivot.transform, new Vector3(1.10f, .30f, -.057f), new Vector3(1.65f, .48f, .025f), "Aged brass", true, false);
            Text("Electrical cover label", pivot.transform, "ROOM POWER\nA / B", new Vector3(1.10f, .30f, -.08f), .15f, Mat("Ink").color);
            Box("Cover handle", pivot.transform, new Vector3(2.01f, -.10f, -.11f), new Vector3(.105f, .52f, .15f), "Aged brass", true, false);
            var doorBody = pivot.AddComponent<Rigidbody>(); doorBody.isKinematic = true; doorBody.interpolation = RigidbodyInterpolation.Interpolate;
            panel.cover = pivot.AddComponent<DoorInteractable>();
            panel.cover.displayName = "room electrical panel"; panel.cover.doorPivot = pivot.transform; panel.cover.openAngle = 105;
            panel.circuits = new ElectricalPanelPresentation.CircuitView[2];
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -.52f : .52f;
                string id = i == 0 ? "A" : "B";
                Text("Circuit " + id + " room label", cabinet.transform, id + "  /  " + (i == 0 ? "WEST ODD ROOMS" : "EAST EVEN ROOMS"), new Vector3(x, 2.49f, -.105f), .065f, Mat("Ink").color);
                var station = Group("Electrical breaker " + id, cabinet.transform, new Vector3(x, 1.80f, -.23f));
                Box("Breaker body", station.transform, Vector3.zero, new Vector3(.71f, .99f, .23f), "Pipe iron", true, false);
                var collider = station.AddComponent<BoxCollider>(); collider.size = new Vector3(.76f, 1.05f, .38f);
                var control = station.AddComponent<ElectricalBreakerControl>(); control.circuitId = id; control.panel = panel;
                control.displayName = "Electrical circuit " + id + (i == 0 ? " · WEST / ODD ROOMS" : " · EAST / EVEN ROOMS");
                var lever = Group("Electrical lever " + id, station.transform, new Vector3(0, 0, -.15f)).transform;
                Box("Large breaker grip", lever, new Vector3(0, .02f, -.07f), new Vector3(.43f, .51f, .19f), "Safety red", true, false);
                Text("Breaker on marking", station.transform, "ON", new Vector3(0, .38f, -.124f), .075f, Lettering);
                Text("Breaker off marking", station.transform, "OFF", new Vector3(0, -.39f, -.124f), .075f, Lettering);
                var readout = Text("Circuit " + id + " actual load", cabinet.transform, "LOAD 0.00 / 4.00\nPOWER ON", new Vector3(x, 1.01f, -.12f), .051f, Mat("Ink").color);
                Box("Circuit consumer list backing " + id, cabinet.transform, new Vector3(x, .48f, -.17f), new Vector3(1.01f, .64f, .06f), "Gauge ivory", true, false);
                var consumers = Text("Circuit " + id + " real consumers", cabinet.transform, "ROOMS (0) 0.00\nHEATERS OFF\nREMOVE LOAD BEFORE RESET",
                    new Vector3(x, .49f, -.208f), .045f, Mat("Ink").color);
                var lens = Sphere("Circuit " + id + " status lens", cabinet.transform, new Vector3(x, 2.94f, -.12f), new Vector3(.19f, .17f, .15f), "Warm lamp");
                var light = lens.AddComponent<Light>(); light.type = LightType.Point; light.range = 2.0f; light.intensity = 0; light.shadows = LightShadows.None;
                Text("External circuit " + id + " label", cabinet.transform, id, new Vector3(x, 3.16f, -.14f), .12f, Lettering);
                panel.circuits[i] = new ElectricalPanelPresentation.CircuitView
                    { circuitId = id, lever = lever, readout = readout, consumers = consumers, warningLens = lens.GetComponent<Renderer>(), warningLight = light };
            }
            panel.roomLights = new ElectricalPanelPresentation.RoomPowerBinding[HotelLayout.RoomCount + 2];
            for (int i = 0; i < HotelLayout.RoomCount; i++)
            {
                int id = 101 + i;
                var room = GameObject.Find("Room" + id);
                panel.roomLights[i] = new ElectricalPanelPresentation.RoomPowerBinding
                {
                    roomId = id,
                    lights = new[] { GameObject.Find("Room light " + id).GetComponent<Light>() },
                    luminousSurfaces = room.GetComponentsInChildren<Renderer>().Where(renderer => renderer.sharedMaterial == Mat("Warm lamp") &&
                        renderer.GetComponentInParent<RoomLampInteraction>() == null).ToArray()
                };
            }
            // Rooms alternate across the hall: odd numbers west, even numbers east.
            // Include the corresponding lobby, hall sconces and utility luminaires.
            var assignedLights = panel.roomLights.Take(HotelLayout.RoomCount).SelectMany(binding => binding.lights).ToHashSet();
            var assignedSurfaces = panel.roomLights.Take(HotelLayout.RoomCount).SelectMany(binding => binding.luminousSurfaces).ToHashSet();
            for (int side = 0; side < 2; side++)
            {
                bool west = side == 0;
                panel.roomLights[HotelLayout.RoomCount + side] = new ElectricalPanelPresentation.RoomPowerBinding
                {
                    circuitId = west ? "A" : "B",
                    lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(light =>
                        light.type != LightType.Directional && !assignedLights.Contains(light) && !light.name.StartsWith("Exterior spill") &&
                        !light.transform.IsChildOf(cabinet.transform) && light.name != "Pressure warning beacon" &&
                        light.GetComponentInParent<RoomLampInteraction>() == null && (light.transform.position.x < 0) == west).ToArray(),
                    luminousSurfaces = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(renderer =>
                        renderer.sharedMaterial == Mat("Warm lamp") && !assignedSurfaces.Contains(renderer) &&
                        !renderer.transform.IsChildOf(cabinet.transform) && renderer.name != "Pressure warning beacon" && renderer.name != "Complaint lamp" &&
                        renderer.GetComponentInParent<PortableHeater>() == null &&
                        renderer.GetComponentInParent<RoomLampInteraction>() == null && (renderer.transform.position.x < 0) == west).ToArray()
                };
            }
        }
    }
}
