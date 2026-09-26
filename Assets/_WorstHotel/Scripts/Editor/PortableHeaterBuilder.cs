using UnityEditor;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void AddPortableHeater(GameObject gameplay)
        {
            var registry = gameplay.AddComponent<RoomVolumeRegistry>();
            registry.volumes = new RoomVolumeRegistry.RoomVolume[6];
            for (int i = 0; i < 6; i++)
            {
                int row = i / 2, side = i % 2 == 0 ? -1 : 1;
                float south = row == 0 ? 6.38f : 13.88f + (row - 1) * 7;
                float north = row == 2 ? 28.62f : 13.12f + row * 7;
                registry.volumes[i] = new RoomVolumeRegistry.RoomVolume(101 + i,
                    new Bounds(new Vector3(side * 6.075f, 1.67f, (south + north) * .5f),
                        new Vector3(7.03f, 3.46f, north - south)));
            }

            var heater = Group("Portable heater", gameplay.transform, new Vector3(3.8f, .035f, 31.4f));
            var body = heater.AddComponent<Rigidbody>();
            body.mass = 6; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var collider = heater.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .63f, 0); collider.size = new Vector3(.92f, 1.26f, .62f);
            var pickup = heater.AddComponent<PhysicsPickup>();
            pickup.itemName = "Portable electric heater"; pickup.holdDistance = 1.8f;
            pickup.grabConfig = AssetDatabase.LoadAssetAtPath<GrabPhysicsConfig>(Root + "/ScriptableObjects/GrabPhysics.asset");
            var tool = heater.AddComponent<PortableHeater>();
            tool.roomVolumes = registry; tool.placementCollider = collider;
            Box("Enamel heater shell", heater.transform, new Vector3(0, .57f, 0), new Vector3(.86f, .97f, .47f), "Boiler enamel", true, false);
            Box("Front grille surround", heater.transform, new Vector3(0, .56f, -.255f), new Vector3(.75f, .71f, .055f), "Aged brass", true, false);
            tool.heatGlow = Box("Heating element", heater.transform, new Vector3(0, .55f, -.293f), new Vector3(.66f, .61f, .015f), "Warm lamp", false, false).GetComponent<Renderer>();
            for (int i = 0; i < 7; i++)
                Box("Protective grille bar", heater.transform, new Vector3(-.30f + i * .10f, .55f, -.306f), new Vector3(.034f, .64f, .025f), "Pipe iron", false, false);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Wide heater foot", heater.transform, new Vector3(side * .31f, .065f, 0), new Vector3(.21f, .13f, .60f), "Pipe iron", true, false);
                Box("Carry handle post", heater.transform, new Vector3(side * .28f, 1.12f, 0), new Vector3(.075f, .19f, .13f), "Ink", true, false);
            }
            Box("Carry handle", heater.transform, new Vector3(0, 1.215f, 0), new Vector3(.64f, .09f, .15f), "Ink", true, false);
            tool.switchLever = Box("Heater rocker switch", heater.transform, new Vector3(.24f, .98f, -.19f), new Vector3(.18f, .13f, .09f), "Safety red", true, false).transform;
            tool.statusLamp = Sphere("Heater power indicator", heater.transform, new Vector3(-.28f, .98f, -.24f), Vector3.one * .065f, "Warm lamp").GetComponent<Renderer>();
            tool.statusLabel = Text("Heater state", heater.transform, "OFF", new Vector3(-.01f, .98f, -.251f), .053f, Lettering);
            // The same supported device, not another load model: two physical alternatives can exceed one circuit.
            var second = Object.Instantiate(heater, gameplay.transform);
            second.name = "Portable heater 2";
            second.transform.position = new Vector3(2.6f, .035f, 31.4f);
            second.GetComponent<PortableHeater>().heaterId = "portable-heater-2";
        }
    }
}
