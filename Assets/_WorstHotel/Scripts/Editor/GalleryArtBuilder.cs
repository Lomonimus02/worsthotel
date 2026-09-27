using UnityEngine;
using UnityEngine.Rendering;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void DisableFixtureShadows(Transform fixture)
        {
            // The opaque proxy for translucent glass must not project a solid black silhouette.
            foreach (var surface in fixture.GetComponentsInChildren<Renderer>())
                surface.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void AddExteriorSpill(Transform parent)
        {
            var daylight = Group("Window and rooflight daylight", parent).transform;
            ExteriorLight(daylight, new Vector3(0, 2.7f, -4.3f), 2.2f, 9);
            foreach (int side in new[] { -1 })
            {
                Window(daylight, new Vector3(side * 9.65f, 2.2f, 1.1f), side * 90);
                ExteriorLight(daylight, new Vector3(side * 8.75f, 2.5f, 1.1f), 1.8f, 8);
            }
            for (int i = 0; i < HotelLayout.RoomCount; i++)
                ExteriorLight(daylight, new Vector3(i % 2 == 0 ? -8.8f : 8.8f, 2.45f, HotelLayout.RoomZ(i) + .1f), 1.7f, 7);
            foreach (float z in new[] { 5.5f, 9f, 16f, 23f, 29f, 35f, 43.5f, 50.5f, 55.5f })
            {
                var roof = Group("Recessed glazed rooflight", daylight, new Vector3(0, 3.76f, z)).transform;
                Box("Rooflight deep timber reveal", roof, Vector3.zero, new Vector3(2.05f, .12f, 1.9f), "Mahogany", false, false);
                Box("Sky glazing", roof, new Vector3(0, -.067f, 0), new Vector3(1.77f, .018f, 1.63f), "Window blue", false, false);
                foreach (float x in new[] { -.9f, 0, .9f })
                    Box("Rooflight glazing bar", roof, new Vector3(x, -.09f, 0), new Vector3(.06f, .065f, 1.75f), "Ivory moulding", false, false);
                foreach (float end in new[] { -.85f, .85f })
                    Box("Rooflight end moulding", roof, new Vector3(0, -.09f, end), new Vector3(1.95f, .065f, .08f), "Ivory moulding", false, false);
                DisableFixtureShadows(roof);
                ExteriorLight(daylight, new Vector3(0, 3.42f, z), 1.35f, 8);
            }
            ExteriorLight(daylight, new Vector3(0, 2.7f, 56.1f), 1.4f, 7);
        }

        static void ExteriorLight(Transform parent, Vector3 position, float intensity, float range)
        {
            // Explicitly excluded from the electrical circuit bindings: only visible glazing supplies this light.
            var light = Group("Exterior spill from glazing", parent, position).AddComponent<Light>();
            // Glazing sends light into the interior hemisphere; one shadow map instead of six.
            light.type = LightType.Spot; light.spotAngle = 145; light.innerSpotAngle = 95;
            Vector3 inward = position.y > 3 ? Vector3.down : Mathf.Abs(position.x) > 8
                ? new Vector3(-Mathf.Sign(position.x), -.20f, 0) : new Vector3(0, -.20f, position.z > 50 ? -1 : 1);
            light.transform.localRotation = Quaternion.LookRotation(inward);
            light.color = new Color(.66f, .80f, 1);
            light.intensity = intensity; light.range = range; light.shadows = LightShadows.Soft;
            light.shadowResolution = LightShadowResolution.Low;
            light.shadowBias = .025f; light.shadowNormalBias = .1f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            var extra = light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            extra.usePipelineSettings = false;
            var settings = new UnityEditor.SerializedObject(extra);
            settings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static float WingBedOffset(int index) => index < 6 ? 0 : index == 6 ? -.18f : index == 7 ? .12f : index == 8 ? -.08f : .24f;

        static void DressNorthGallery(Transform wing)
        {
            // All wall-mounted details stay outside the 3.4 m aisle; no new floor-level obstacles.
            foreach (float z in new[] { 40.12f, 49.5f, 56.45f })
            {
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Gallery fluted pier", wing, new Vector3(side * 1.87f, 1.75f, z), new Vector3(.24f, 3.5f, .32f), "Walnut panels", false, false);
                    Box("Gallery pier capital", wing, new Vector3(side * 1.84f, 3.39f, z), new Vector3(.42f, .24f, .50f), "Ivory moulding", false, false);
                    Box("Pier brass inlay", wing, new Vector3(side * 1.735f, 2.03f, z), new Vector3(.025f, 1.85f, .06f), "Aged brass", false, false);
                }
                Box("Gallery overhead architrave", wing, new Vector3(0, 3.61f, z), new Vector3(3.90f, .23f, .43f), "Ivory moulding", false, false);
                Box("Gallery overhead brass bead", wing, new Vector3(0, 3.46f, z), new Vector3(3.65f, .045f, .47f), "Aged brass", false, false);
            }
            Box("North Wing entrance signboard", wing, new Vector3(0, 3.19f, 39.75f), new Vector3(3.3f, .48f, .16f), "Mahogany", true, false);
            Box("Gallery transition carpet", wing, new Vector3(0, .016f, 37.1f), new Vector3(2.65f, .018f, 5.6f), "Gallery carpet", false, false);
            foreach (int side in new[] { -1, 1 })
            {
                Place(lamp, wing, new Vector3(side * 1.80f, 2.24f, 41.8f), new Vector3(0, side * 90, 0));
                foreach (float z in new[] { 43.15f, 49.45f, 56f })
                {
                    Box("Sage wallpaper inset", wing, new Vector3(side * 1.974f, 2.24f, z), new Vector3(.022f, 1.90f, 1.48f), "Gallery sage", false, false);
                    GalleryPicture(wing, new Vector3(side * 1.92f, 2.22f, z), side * 90, side < 0 ? "Gallery ochre" : "Gallery blue", .83f);
                }
                Sign(wing, side < 0 ? "107 / 109" : "108 / 110", new Vector3(side * 1.79f, 1.43f, 41), .96f, .31f, .07f, side * 90);
            }
            Box("Older wallpaper repair", wing, new Vector3(-1.955f, 1.58f, 44.15f), new Vector3(.035f, .37f, .29f), "New plaster patch", false, false);
            Window(wing, new Vector3(0, 2.30f, 56.72f), 0);
            Box("Window seat frame", wing, new Vector3(0, .30f, 56.35f), new Vector3(1.68f, .56f, .53f), "Mahogany", true);
            Box("Window seat cushion", wing, new Vector3(0, .62f, 56.30f), new Vector3(1.60f, .16f, .51f), "Teal upholstery", true, false);
            Sign(wing, "NORTH GALLERY · 1928", new Vector3(0, 3.40f, 56.63f), 2.43f, .36f, .086f);
            Plant(wing, new Vector3(-1.23f, 0, 56.30f));
        }

        static void DressWingRoom(Transform room, Transform roomBed, int index, int side)
        {
            string accent = index == 6 ? "Gallery sage" : index == 7 ? "Gallery ochre" : index == 8 ? "Gallery blue" : "Burgundy velvet";
            foreach (var surface in roomBed.GetComponentsInChildren<Renderer>())
                if (surface.name == "Burgundy runner" || surface.name == "Headboard upholstery") surface.sharedMaterial = Mat(accent);
            Box("Headboard wallpaper panel", room, new Vector3(side * .6f, 2.16f, 3.29f), new Vector3(2.60f, 2.06f, .024f), accent, false, false);
            GalleryPicture(room, new Vector3(side * .6f, 2.34f, 3.21f), 0, accent, index < 8 ? 1.12f : .9f, index - 6);
            if (index >= 8)
                GalleryPicture(room, new Vector3(side * 2.7f, 2.22f, -3.28f), 180, "Gallery sage", .70f);
            Box("Room entrance brass threshold", room, new Vector3(-side * 3.0f, .015f, 0), new Vector3(.32f, .016f, 1.52f), "Aged brass", false, false);
            // Use the two ends of the writing desk: the bedside table is occupied by the service lamp,
            // and the centre of the writing desk is reserved for the guest's notebook.
            var tray = Group("Guest welcome tray", room, new Vector3(-side * .85f, .95f, 3.15f)).transform;
            Box("Reading book", tray, new Vector3(.49f, .035f, -.03f), new Vector3(.22f, .07f, .30f), accent, true, false);
            if (index % 2 == 0)
            {
                Cylinder("Small ceramic vase", tray, new Vector3(-.48f, .12f, -.02f), .065f, .24f, "Cream linen");
                Pipe("Dried flower stem", tray, new Vector3(-.48f, .20f, -.02f), new Vector3(-.46f, .43f, -.02f), .012f, "Plant green");
                Sphere("Dried flower", tray, new Vector3(-.46f, .43f, -.02f), Vector3.one * .13f, "Gallery ochre");
            }
            else Cylinder("Tea cup", tray, new Vector3(-.48f, .07f, -.08f), .075f, .14f, "Cream linen");
            foreach (var surface in room.GetComponentsInChildren<Renderer>())
                if (surface.name == "Curtain fold") surface.sharedMaterial = Mat(accent);
        }

        static void GalleryPicture(Transform parent, Vector3 position, float yaw, string accent, float scale, int motif = 0)
        {
            var picture = Group("Framed travel print", parent, position, new Vector3(0, yaw, 0)).transform;
            picture.localScale = Vector3.one * scale;
            Box("Picture timber frame", picture, Vector3.zero, new Vector3(1.10f, .85f, .07f), "Mahogany", true, false);
            Box("Picture brass lining", picture, new Vector3(0, 0, -.04f), new Vector3(1.02f, .77f, .02f), "Aged brass", false, false);
            Box("Picture paper", picture, new Vector3(0, 0, -.055f), new Vector3(.94f, .69f, .015f), "Cream linen", false, false);
            Box("Print distant sky", picture, new Vector3(0, .02f, -.066f), new Vector3(.81f, .50f, .01f), "Gallery blue", false, false);
            Sphere("Print sun", picture, new Vector3(motif == 1 ? -.24f : .22f, .14f, -.075f), new Vector3(.14f, .14f, .012f), "Gallery ochre");
            if (motif == 1)
            {
                Box("Print distant water", picture, new Vector3(0, -.10f, -.077f), new Vector3(.81f, .19f, .01f), "Teal upholstery", false, false);
                Box("Print sailing boat", picture, new Vector3(.08f, -.09f, -.084f), new Vector3(.30f, .055f, .012f), "Mahogany", false, false);
                Box("Print sail", picture, new Vector3(.10f, .04f, -.084f), new Vector3(.14f, .20f, .012f), "Cream linen", false, false);
            }
            else if (motif == 2)
            {
                foreach (float x in new[] { -.23f, 0f, .23f })
                {
                    Box("Print tree trunk", picture, new Vector3(x, -.09f, -.079f), new Vector3(.025f, .27f, .012f), "Mahogany", false, false);
                    Sphere("Print tree crown", picture, new Vector3(x, .03f + x * .2f, -.084f), new Vector3(.19f, .27f, .012f), "Gallery sage");
                }
            }
            else if (motif == 3)
            {
                for (int building = 0; building < 3; building++)
                {
                    float x = -.25f + building * .23f, height = building == 1 ? .30f : .21f;
                    Box("Print village house", picture, new Vector3(x, -.18f + height * .5f, -.078f), new Vector3(.18f, height, .012f), building == 1 ? accent : "Gallery ochre", false, false);
                    Box("Print village window", picture, new Vector3(x, -.04f, -.085f), new Vector3(.045f, .065f, .01f), "Cream linen", false, false);
                }
            }
            else Sphere("Print rolling hill", picture, new Vector3(-.16f, -.12f, -.078f), new Vector3(.49f, .24f, .012f), accent);
            Box("Print foreground", picture, new Vector3(0, -.19f, -.084f), new Vector3(.81f, .08f, .012f), "Gallery sage", false, false);
            DisableFixtureShadows(picture);
        }
    }
}
