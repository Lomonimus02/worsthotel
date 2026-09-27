using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    /// <summary>Authored hotel layout, reproducibly assembled from the project's original modular kit.</summary>
    public static partial class PrototypeSceneBuilder
    {
        public const string ScenePath = Root + "/Scenes/PrototypeHotel.unity";
        static GameObject wall, pier, door, radiator, lamp, bed, chair, table;
        static readonly Color Lettering = new Color(.95f, .84f, .58f);

        [MenuItem("Tools/Worst Hotel/Build Prototype Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode before rebuilding the hotel.");
            Prepare();
            ConfigureRenderPipeline();
            BuildKit();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var environment = Group("Environment", null);
            var lighting = Group("Lighting", null);
            var gameplay = Group("Gameplay", null);
            var spawns = Group("SpawnPoints", null);
            BuildShell(environment.transform);
            BuildLobby(environment.transform, gameplay.transform);
            BuildRooms(environment.transform, spawns.transform);
            BuildUtility(environment.transform, gameplay.transform);
            BuildLights(lighting.transform);
            var p1 = Group("Player1Spawn", spawns.transform, new Vector3(-1.1f, .08f, -1.8f));
            var p2 = Group("Player2Spawn", spawns.transform, new Vector3(1.1f, .08f, -1.8f));
            Group("GuestSpawn", spawns.transform, new Vector3(0, .08f, -3.8f));
            var bootstrap = gameplay.AddComponent<LocalCoopBootstrap>();
            bootstrap.spawn1 = p1.transform;
            bootstrap.spawn2 = p2.transform;
            BuildGameplay(gameplay);
            AddEnvironmentFeedback(gameplay, environment);
            AddElectricalPanel(gameplay);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = gameplay;
            Debug.Log("Worst Hotel: authored hotel rebuilt with six accessible rooms, local player spawns, original modular kit and URP lighting.");
        }

        // Later delivery phases add a separate partial source file; phase 1 has no hidden gameplay placeholder.
        static partial void BuildGameplay(GameObject gameplay);

        static void ConfigureRenderPipeline()
        {
            string rendererPath = Root + "/Settings/HotelRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (!renderer)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "Hotel Renderer";
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            string pipelinePath = Root + "/Settings/HotelURP.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (!pipeline)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Hotel URP";
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            var serialized = new SerializedObject(pipeline);
            var renderers = serialized.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            pipeline.renderScale = 1;
            pipeline.msaaSampleCount = 2;
            pipeline.supportsHDR = true;
            pipeline.shadowDistance = 45;
            pipeline.useSRPBatcher = true;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 1;
            EditorUtility.SetDirty(pipeline);
        }

        static void BuildKit()
        {
            var w = Group("WallPanel", null);
            var plaster = Box("Thick plaster", w.transform, new Vector3(0, 1.9f, 0), new Vector3(2, 3.8f, .32f), "Cream plaster");
            plaster.GetComponent<BoxCollider>().size = new Vector3(1, 1, 2);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Walnut wainscot", w.transform, new Vector3(0, .53f, side * .19f), new Vector3(2, 1.06f, .12f), "Walnut panels", false, false);
                Box("Skirting", w.transform, new Vector3(0, .12f, side * .24f), new Vector3(2, .23f, .16f), "Mahogany", false, false);
                Box("Chair rail", w.transform, new Vector3(0, 1.12f, side * .23f), new Vector3(2, .13f, .16f), "Mahogany", false, false);
                Box("Rail brass bead", w.transform, new Vector3(0, 1.19f, side * .24f), new Vector3(2, .045f, .14f), "Aged brass", false, false);
                Box("Crown lower", w.transform, new Vector3(0, 3.45f, side * .20f), new Vector3(2, .15f, .13f), "Ivory moulding", false, false);
                Box("Crown upper", w.transform, new Vector3(0, 3.60f, side * .25f), new Vector3(2, .19f, .23f), "Ivory moulding", false, false);
                for (int i = -1; i <= 1; i++)
                    Box("Raised panel stile", w.transform, new Vector3(i * .91f, .63f, side * .28f), new Vector3(.06f, .75f, .06f), "Mahogany", false, false);
            }
            wall = SavePrefab(w, "WallPanel_2m");

            var p = Group("Pilaster", null);
            Box("Column", p.transform, new Vector3(0, 1.8f, 0), new Vector3(.32f, 3.6f, .30f), "Ivory moulding");
            Box("Foot", p.transform, new Vector3(0, .20f, 0), new Vector3(.48f, .4f, .40f), "Mahogany");
            Box("Capital", p.transform, new Vector3(0, 3.25f, 0), new Vector3(.55f, .24f, .44f), "Ivory moulding");
            Box("Capital brass", p.transform, new Vector3(0, 3.10f, 0), new Vector3(.39f, .045f, .35f), "Aged brass", false, false);
            pier = SavePrefab(p, "Pilaster");

            var d = Group("Guest door", null);
            Box("Frame left", d.transform, new Vector3(-.89f, 1.47f, 0), new Vector3(.24f, 2.94f, .54f), "Mahogany");
            Box("Frame right", d.transform, new Vector3(.89f, 1.47f, 0), new Vector3(.24f, 2.94f, .54f), "Mahogany");
            Box("Pediment", d.transform, new Vector3(0, 3.0f, 0), new Vector3(2.09f, .23f, .6f), "Mahogany");
            Box("Brass crown", d.transform, new Vector3(0, 3.13f, -.02f), new Vector3(2.18f, .06f, .64f), "Aged brass", false, false);
            var pivot = Group("DoorPivot", d.transform, new Vector3(-.755f, 0, 0));
            Box("Door leaf", pivot.transform, new Vector3(.755f, 1.38f, 0), new Vector3(1.51f, 2.76f, .16f), "Walnut panels", true);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Raised upper field", pivot.transform, new Vector3(.755f, 1.83f, side * .104f), new Vector3(1.17f, 1.30f, .06f), "Mahogany", true, false);
                Box("Upper inset", pivot.transform, new Vector3(.755f, 1.83f, side * .144f), new Vector3(1.02f, 1.14f, .035f), "Walnut panels", true, false);
                Box("Lower field", pivot.transform, new Vector3(.755f, .53f, side * .104f), new Vector3(1.17f, .62f, .06f), "Mahogany", true, false);
                Box("Handle plate", pivot.transform, new Vector3(1.27f, 1.2f, side * .12f), new Vector3(.18f, .37f, .04f), "Aged brass", true, false);
                Sphere("Handle", pivot.transform, new Vector3(1.27f, 1.24f, side * .22f), Vector3.one * .15f, "Aged brass");
            }
            var rb = pivot.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var interactable = d.AddComponent<DoorInteractable>(); interactable.displayName = "guest room"; interactable.doorPivot = pivot.transform;
            interactable.openAwayFromPlayer = true;
            door = SavePrefab(d, "GuestDoor");

            var r = Group("Cast iron radiator", null);
            Pipe("Top manifold", r.transform, new Vector3(-.85f, .83f, 0), new Vector3(.85f, .83f, 0), .09f, "Ivory moulding");
            Pipe("Bottom manifold", r.transform, new Vector3(-.85f, .23f, 0), new Vector3(.85f, .23f, 0), .09f, "Ivory moulding");
            for (int i = 0; i < 8; i++) Box("Radiator fin", r.transform, new Vector3(-.7f + i * .2f, .53f, 0), new Vector3(.14f, .78f, .28f), "Ivory moulding", true);
            Pipe("Supply", r.transform, new Vector3(-1, 0, 0), new Vector3(-1, .85f, 0), .065f, "Repair copper");
            Sphere("Thermostat", r.transform, new Vector3(-1, .87f, -.15f), Vector3.one * .2f, "Aged brass");
            radiator = SavePrefab(r, "Radiator");

            var l = Group("Warm wall lamp", null);
            Box("Mount", l.transform, Vector3.zero, new Vector3(.22f, .45f, .11f), "Aged brass", true, false);
            Pipe("Curved bracket", l.transform, new Vector3(0, -.04f, 0), new Vector3(0, -.04f, -.30f), .06f, "Aged brass");
            Cylinder("Shade rim", l.transform, new Vector3(0, .12f, -.30f), .23f, .08f, "Aged brass");
            Sphere("Milk glass", l.transform, new Vector3(0, .23f, -.3f), new Vector3(.43f, .38f, .43f), "Warm lamp");
            lamp = SavePrefab(l, "WallLamp");

            var b = Group("Guest bed", null);
            Box("Bed base", b.transform, new Vector3(0, .38f, 0), new Vector3(2.0f, .5f, 2.5f), "Mahogany", true);
            Box("Mattress", b.transform, new Vector3(0, .71f, 0), new Vector3(1.96f, .34f, 2.42f), "Cream linen", true);
            Box("Duvet", b.transform, new Vector3(0, .92f, -.20f), new Vector3(2.04f, .22f, 1.99f), "Cream linen", true);
            Box("Burgundy runner", b.transform, new Vector3(0, 1.04f, -.82f), new Vector3(2.08f, .09f, .57f), "Burgundy velvet", true, false);
            Box("Headboard", b.transform, new Vector3(0, 1.05f, 1.27f), new Vector3(2.24f, 1.4f, .21f), "Mahogany", true);
            Box("Headboard upholstery", b.transform, new Vector3(0, 1.26f, 1.12f), new Vector3(1.88f, .82f, .11f), "Teal upholstery", true, false);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Box("Pillow", b.transform, new Vector3(sign * .47f, 1.03f, .79f), new Vector3(.8f, .19f, .46f), "Cream linen", true, false);
                Cylinder("Bed leg", b.transform, new Vector3(sign * .81f, .15f, -.9f), .11f, .3f, "Aged brass");
            }
            bed = SavePrefab(b, "GuestBed");

            var c = Group("Lobby armchair", null);
            Box("Chair seat", c.transform, new Vector3(0, .48f, 0), new Vector3(1.28f, .5f, 1.18f), "Teal upholstery", true);
            Box("Chair back", c.transform, new Vector3(0, 1.01f, .46f), new Vector3(1.32f, 1.18f, .4f), "Teal upholstery", true);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Chair arm", c.transform, new Vector3(side * .62f, .79f, -.02f), new Vector3(.27f, .37f, 1.16f), "Teal upholstery", true);
                Box("Arm wood cap", c.transform, new Vector3(side * .62f, .99f, -.05f), new Vector3(.31f, .09f, 1.10f), "Mahogany", true, false);
                Cylinder("Chair leg", c.transform, new Vector3(side * .51f, .14f, -.40f), .08f, .28f, "Mahogany");
            }
            chair = SavePrefab(c, "LobbyArmchair");

            var t = Group("Side table", null);
            Box("Table top", t.transform, new Vector3(0, .70f, 0), new Vector3(.85f, .14f, .75f), "Mahogany", true);
            Box("Table body", t.transform, new Vector3(0, .45f, 0), new Vector3(.70f, .43f, .62f), "Walnut panels", true);
            Box("Drawer", t.transform, new Vector3(0, .52f, -.33f), new Vector3(.60f, .20f, .06f), "Mahogany", true);
            Sphere("Drawer knob", t.transform, new Vector3(0, .52f, -.4f), Vector3.one * .09f, "Aged brass");
            table = SavePrefab(t, "SideTable");
        }

        static void WallRun(Transform parent, Vector3 center, float length, float yaw = 0)
        {
            int pieces = Mathf.CeilToInt(length / 2);
            float width = length / pieces;
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < pieces; i++)
            {
                Vector3 offset = rotation * new Vector3(-length * .5f + width * (i + .5f), 0, 0);
                var obj = Place(wall, parent, center + offset, new Vector3(0, yaw, 0));
                obj.transform.localScale = new Vector3(width * .5f, 1, 1);
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
            }
        }

        static void BuildShell(Transform root)
        {
            var floors = Group("Floors and ceilings", root).transform;
            Box("Lobby carpet", floors, new Vector3(0, -.16f, .5f), new Vector3(20, .32f, 11), "Grand carpet");
            Box("Rooms and corridor carpet", floors, new Vector3(0, -.16f, 17.5f), new Vector3(20, .32f, 23), "Grand carpet");
            Box("Utility tile floor", floors, new Vector3(0, -.16f, 34.5f), new Vector3(14, .32f, 11), "Utility tile");
            Box("Lobby ceiling", floors, new Vector3(0, 3.91f, .5f), new Vector3(20.3f, .22f, 11.3f), "Cream linen");
            Box("Hall and room ceiling", floors, new Vector3(0, 3.91f, 17.5f), new Vector3(20.3f, .22f, 23.3f), "Cream linen");
            Box("Utility ceiling", floors, new Vector3(0, 3.91f, 34.5f), new Vector3(14.3f, .22f, 11.3f), "Utility tile");
            foreach (int side in new[] { -1, 1 })
            {
                Box("Runner gold border", floors, new Vector3(side * 1.49f, .008f, 17.5f), new Vector3(.07f, .01f, 23), "Aged brass", false, false);
                Box("Runner dark border", floors, new Vector3(side * 1.60f, .009f, 17.5f), new Vector3(.1f, .012f, 23), "Mahogany", false, false);
            }
            var walls = Group("Architecture", root).transform;
            WallRun(walls, new Vector3(-10, 0, 12), 34, 90);
            WallRun(walls, new Vector3(10, 0, 12), 34, 90);
            WallRun(walls, new Vector3(-5.83f, 0, -5), 8.34f);
            WallRun(walls, new Vector3(5.83f, 0, -5), 8.34f);
            Box("Entrance overlight", walls, new Vector3(0, 3.28f, -5), new Vector3(3.32f, 1.04f, .32f), "Cream plaster");
            Box("Entrance glazing", walls, new Vector3(0, 1.4f, -5), new Vector3(3.2f, 2.8f, .15f), "Window blue");
            for (int x = -1; x <= 1; x++)
                Box("Entrance brass mullion", walls, new Vector3(x * 1.55f, 1.4f, -4.86f), new Vector3(.1f, 2.8f, .12f), "Aged brass");
            Text("Entry", walls, "GRAND HOTEL", new Vector3(0, 2.43f, -4.74f), .15f, Lettering, new Vector3(0, 180, 0));
            foreach (int side in new[] { -1, 1 })
            {
                WallRun(walls, new Vector3(side * 6.075f, 0, 6), 7.85f);
                WallRun(walls, new Vector3(side * 8.5f, 0, 29), 3);
                for (int i = 0; i < 2; i++) WallRun(walls, new Vector3(side * 6.075f, 0, 13.5f + i * 7), 7.85f);
                WallRun(walls, new Vector3(side * 7, 0, 34.5f), 11, 90);
                WallRun(walls, new Vector3(side * 4.55f, 0, 29), 4.9f);
            }
            WallRun(walls, new Vector3(0, 0, 40), 14);
            for (int side = -1; side <= 1; side += 2)
            {
                float previous = 6;
                for (int i = 0; i < 3; i++)
                {
                    float z = 10 + i * 7;
                    float before = z - 1.04f;
                    WallRun(walls, new Vector3(side * 2.15f, 0, (before + previous) * .5f), before - previous, 90);
                    Box("Door lintel plaster", walls, new Vector3(side * 2.15f, 3.51f, z), new Vector3(.32f, .58f, 2.08f), "Cream plaster");
                    previous = z + 1.04f;
                }
                WallRun(walls, new Vector3(side * 2.15f, 0, (previous + 29) * .5f), 29 - previous, 90);
            }
            foreach (float z in new[] { 6f, 13.5f, 20.5f, 28.8f })
            {
                Place(pier, walls, new Vector3(-1.93f, 0, z));
                Place(pier, walls, new Vector3(1.93f, 0, z));
                Box("Ceiling crossbeam", walls, new Vector3(0, 3.55f, z), new Vector3(4.14f, .22f, .4f), "Ivory moulding");
                Box("Crossbeam inlay", walls, new Vector3(0, 3.42f, z), new Vector3(3.74f, .04f, .18f), "Aged brass", false, false);
            }
            Sign(walls, "GUEST ROOMS  /  101—106", new Vector3(0, 3.10f, 6.02f), 3.55f, .45f, .18f);
            Sign(walls, "BOILER ROOM", new Vector3(0, 3.06f, 28.9f), 2.9f, .48f, .21f);
        }

        static void BuildLobby(Transform environment, Transform gameplay)
        {
            var lobby = Group("Lobby furnishings", environment).transform;
            var desk = Group("Reception desk", lobby, new Vector3(-5, 0, 2.8f)).transform;
            Box("Reception plinth", desk, new Vector3(0, .12f, 0), new Vector3(5.9f, .24f, 1.75f), "Mahogany", true);
            Box("Curved desk body", desk, new Vector3(0, .7f, 0), new Vector3(5.55f, 1.16f, 1.50f), "Walnut panels", true);
            Box("Counter", desk, new Vector3(0, 1.31f, 0), new Vector3(6.0f, .17f, 1.86f), "Mahogany", true);
            Box("Brass counter edge", desk, new Vector3(0, 1.22f, -.81f), new Vector3(5.6f, .07f, .09f), "Aged brass", true, false);
            for (int i = -1; i <= 1; i++)
            {
                Box("Raised desk panel", desk, new Vector3(i * 1.76f, .73f, -.78f), new Vector3(1.51f, .70f, .07f), "Mahogany", true, false);
                Box("Inset desk panel", desk, new Vector3(i * 1.76f, .73f, -.83f), new Vector3(1.31f, .51f, .035f), "Walnut panels", true, false);
            }
            Text("Reception lettering", desk, "R E C E P T I O N", new Vector3(0, .77f, -.865f), .21f, Lettering);
            var terminal = Group("ReceptionTerminal", gameplay, new Vector3(-5, 1.42f, 2.40f));
            Box("Terminal base", terminal.transform, Vector3.zero, new Vector3(1.32f, .18f, .77f), "Boiler enamel", true);
            Box("Terminal cabinet", terminal.transform, new Vector3(0, .43f, .12f), new Vector3(1.35f, .88f, .5f), "Ivory moulding", true);
            Box("Terminal glass", terminal.transform, new Vector3(0, .46f, -.15f), new Vector3(1.10f, .60f, .04f), "Terminal glass", true, false);
            Text("Terminal label", terminal.transform, "RECEPTION\nMANAGEMENT", new Vector3(0, .45f, -.183f), .11f, Lettering);
            Box("Terminal keyboard", terminal.transform, new Vector3(0, .12f, -.43f), new Vector3(.95f, .065f, .34f), "Ink", true, false);
            for (int i = 0; i < 8; i++) Box("Chunky key", terminal.transform, new Vector3(-.37f + i * .105f, .161f, -.43f), new Vector3(.065f, .027f, .20f), "Cream linen", true, false);
            Cylinder("Desk bell base", desk, new Vector3(2.08f, 1.43f, -.35f), .25f, .08f, "Aged brass");
            Sphere("Service bell", desk, new Vector3(2.08f, 1.51f, -.35f), new Vector3(.4f, .22f, .4f), "Aged brass");
            Box("Guest ledger", desk, new Vector3(-1.83f, 1.45f, -.35f), new Vector3(.70f, .09f, .49f), "Burgundy velvet", true, false);
            Sign(lobby, "THE WORST\nHOTEL EVER", new Vector3(-5, 2.47f, 5.56f), 5.35f, 1.48f, .47f);
            Place(chair, lobby, new Vector3(6, 0, 3.1f));
            Place(chair, lobby, new Vector3(8.2f, 0, 1), new Vector3(0, -90, 0));
            Place(chair, lobby, new Vector3(5.5f, 0, -1.3f), new Vector3(0, 180, 0));
            var coffee = Group("Coffee table", lobby, new Vector3(6.45f, 0, .9f)).transform;
            Cylinder("Round table top", coffee, new Vector3(0, .58f, 0), .95f, .15f, "Mahogany", default, true);
            Cylinder("Table stem", coffee, new Vector3(0, .28f, 0), .2f, .55f, "Aged brass");
            Box("Old magazine", coffee, new Vector3(.23f, .69f, 0), new Vector3(.5f, .045f, .37f), "Luggage mustard", false, false);
            Plant(lobby, new Vector3(8.7f, 0, -3.5f));
            Plant(lobby, new Vector3(-8.7f, 0, -3.5f));
            Sign(lobby, "EVERYTHING WORKS.*\n*Usually.", new Vector3(6, 2.37f, 5.57f), 2.8f, .86f, .13f);
            foreach (float lampX in new[] { -8.6f, -1.5f })
                Place(lamp, lobby, new Vector3(lampX, 2.25f, 5.60f));
            Place(lamp, lobby, new Vector3(8.7f, 2.25f, 5.6f));
            BuildLuggage(lobby, new Vector3(-6.5f, .02f, -.4f));
            Box("Decades of paint patch", lobby, new Vector3(9.8f, 1.78f, 4.2f), new Vector3(.015f, .8f, 1.12f), "New plaster patch", false, false);
        }

        static void BuildRooms(Transform environment, Transform spawns)
        {
            for (int i = 0; i < 6; i++)
            {
                int number = 101 + i;
                int side = i % 2 == 0 ? -1 : 1;
                float z = 10 + i / 2 * 7;
                var room = Group("Room" + number, environment, new Vector3(side * 6.0f, 0, z)).transform;
                Group("RoomTarget" + number, spawns, new Vector3(side * 4.3f, .05f, z + .7f));
                var instance = Place(door, environment, new Vector3(side * 2.15f, 0, z), new Vector3(0, side * 90, 0));
                instance.name = "Door" + number;
                instance.GetComponent<DoorInteractable>().displayName = "room " + number;
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<DoorInteractable>());
                Sign(instance.GetComponent<DoorInteractable>().doorPivot, number.ToString(), new Vector3(.755f, 2.20f, -.145f), .57f, .36f, .15f);
                // Leave room for the guests' full arm span between open doors, the bed and the north wall.
                Place(bed, room, new Vector3(side * .6f, 0, .66f));
                Place(table, room, new Vector3(side * -.85f, 0, 1.35f));
                Place(chair, room, new Vector3(side * 2.45f, 0, -1.8f), new Vector3(0, side * 90, 0));
                var rad = Place(radiator, room, new Vector3(side * 3.60f, 0, .2f), new Vector3(0, side * 90, 0));
                rad.name = "Radiator" + number;
                Window(room, new Vector3(side * 3.70f, 2.2f, .1f), side * 90);
                Place(lamp, room, new Vector3(-.7f, 2.20f, 3.15f));
                Place(lamp, environment, new Vector3(side * 1.80f, 2.24f, z + 2.10f), new Vector3(0, side * 90, 0));
                // Visible repairs differ by room, but are deliberately unrelated to simulation values here.
                if (i == 3 || i == 5)
                    Box("Fresh plaster repair", room, new Vector3(0, 1.79f, -3.29f), new Vector3(1.13f, .70f, .018f), "New plaster patch", false, false);
                Box("Wardrobe", room, new Vector3(side * -1.9f, 1.06f, -2.5f), new Vector3(1.17f, 2.12f, .70f), "Walnut panels", true);
                Box("Wardrobe doors", room, new Vector3(side * -1.9f, 1.12f, -2.88f), new Vector3(1.03f, 1.82f, .06f), "Mahogany", true, false);
                Sphere("Wardrobe knob", room, new Vector3(side * -1.9f, 1.2f, -2.95f), Vector3.one * .10f, "Aged brass");
            }
            var hall = Group("Corridor details", environment).transform;
            for (int i = 0; i < 3; i++)
            {
                float z = 7.4f + i * 7;
                Sign(hall, i == 0 ? "WELCOME" : i == 1 ? "QUIET PLEASE" : "HEAT IS A PRIVILEGE", new Vector3(-1.79f, 2.15f, z), 1.5f, .60f, .083f, -90);
            }
            // One proud new copper section amongst the older iron, above player height.
            Pipe("Hall heating main", hall, new Vector3(1.58f, 3.15f, 6.3f), new Vector3(1.58f, 3.15f, 28.4f), .11f, "Pipe iron");
            Pipe("Copper pipe repair", hall, new Vector3(1.58f, 3.15f, 19.1f), new Vector3(1.58f, 3.15f, 20.3f), .145f, "Repair copper");
            for (int i = 0; i < 7; i++) Cylinder("Pipe collar", hall, new Vector3(1.58f, 3.15f, 7 + i * 3), .16f, .12f, "Aged brass", new Vector3(90, 0, 0));
        }

        static void BuildUtility(Transform environment, Transform gameplay)
        {
            var utility = Group("Utility fittings", environment).transform;
            // Painted grid lines give this room its own surface language without extra dependencies.
            for (int x = -6; x <= 6; x++) Box("Tile grout", utility, new Vector3(x, .006f, 34.5f), new Vector3(.025f, .008f, 10.5f), "Pipe iron", false, false);
            for (int z = 30; z <= 39; z++) Box("Tile grout", utility, new Vector3(0, .007f, z), new Vector3(13.5f, .008f, .025f), "Pipe iron", false, false);
            Sign(utility, "PLANT No. 01  /  HEATING", new Vector3(0, 3.08f, 39.61f), 6.0f, .54f, .17f);
            var boiler = Group("Boiler", gameplay, new Vector3(0, 0, 37.7f)).transform;
            Box("Boiler foundation", boiler, new Vector3(-.6f, .17f, .35f), new Vector3(4.15f, .34f, 2.7f), "Pipe iron", true);
            Cylinder("Pressure vessel", boiler, new Vector3(-.6f, 1.73f, .35f), 1.50f, 2.55f, "Boiler enamel", new Vector3(0, 0, 90), true);
            Sphere("Left rounded end", boiler, new Vector3(-1.88f, 1.73f, .35f), new Vector3(.48f, 2.94f, 2.94f), "Boiler enamel");
            Sphere("Right rounded end", boiler, new Vector3(.68f, 1.73f, .35f), new Vector3(.48f, 2.94f, 2.94f), "Boiler enamel");
            Cylinder("Tank band A", boiler, new Vector3(-1.5f, 1.73f, .35f), 1.55f, .12f, "Aged brass", new Vector3(0, 0, 90));
            Cylinder("Tank band B", boiler, new Vector3(.25f, 1.73f, .35f), 1.55f, .12f, "Aged brass", new Vector3(0, 0, 90));
            Box("Manufacturer plate", boiler, new Vector3(-.63f, 1.63f, -1.19f), new Vector3(1.1f, .48f, .06f), "Aged brass", true, false);
            Text("Boiler brand", boiler, "GRAND HEAT\nMODEL 1968", new Vector3(-.63f, 1.64f, -1.23f), .089f, Mat("Ink").color);
            Pipe("Main riser", utility, new Vector3(-3.1f, .25f, 38.3f), new Vector3(-3.1f, 3.38f, 38.3f), .19f, "Pipe iron");
            Pipe("Upper manifold", utility, new Vector3(-3.1f, 3.38f, 38.3f), new Vector3(5.7f, 3.38f, 38.3f), .19f, "Pipe iron");
            Sphere("Main elbow", utility, new Vector3(-3.1f, 3.38f, 38.3f), Vector3.one * .46f, "Pipe iron");
            Pipe("Room heating supply", utility, new Vector3(5.7f, 3.38f, 38.3f), new Vector3(5.7f, 3.38f, 29.3f), .16f, "Repair copper");
            Pipe("Relief branch", utility, new Vector3(-3.1f, 1.55f, 38.3f), new Vector3(-3.1f, 1.55f, 36.7f), .17f, "Repair copper");
            Pipe("Tank connection", utility, new Vector3(-3.1f, 1.55f, 38.3f), new Vector3(-1.8f, 1.55f, 38.3f), .17f, "Pipe iron");
            Pipe("Steam outlet", utility, new Vector3(-3.1f, 2.50f, 38.3f), new Vector3(-4.25f, 2.50f, 38.3f), .13f, "Repair copper");
            var valve = Group("ValveAnchor", gameplay, new Vector3(-3.1f, 1.55f, 36.52f)).transform;
            var wheel = Group("Wheel", valve).AddComponent<MeshFilter>(); wheel.sharedMesh = Wheel;
            wheel.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Mat("Safety red");
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2 / 3;
                Pipe("Wheel spoke", valve, Vector3.zero, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .36f, .035f, "Safety red");
            }
            Cylinder("Valve hub", valve, Vector3.zero, .105f, .18f, "Aged brass", new Vector3(90, 0, 0));
            var vc = valve.gameObject.AddComponent<BoxCollider>(); vc.size = new Vector3(1.0f, 1.0f, .24f);
            Sign(utility, "1  RELIEF VALVE\nHOLD PRESSURE", new Vector3(-3.1f, 2.40f, 36.67f), 2.15f, .67f, .14f);

            var panel = Group("PanelAnchor", gameplay, new Vector3(1.75f, 1.74f, 36.48f)).transform;
            Box("Service cabinet", panel, new Vector3(0, 0, .35f), new Vector3(1.54f, 1.74f, .63f), "Pipe iron", true);
            Box("Panel door", panel, new Vector3(0, 0, -.015f), new Vector3(1.43f, 1.61f, .10f), "Boiler enamel", true);
            Box("Panel grip", panel, new Vector3(.51f, -.02f, -.11f), new Vector3(.13f, .48f, .15f), "Aged brass", true, false);
            Text("Panel label", panel, "2  SERVICE\nPANEL", new Vector3(0, .35f, -.08f), .12f, Lettering);

            var breaker = Group("BreakerAnchor", gameplay, new Vector3(3.13f, 2.08f, 36.48f)).transform;
            Box("Breaker backing", breaker, new Vector3(0, 0, .13f), new Vector3(.64f, 1.04f, .28f), "Ivory moulding", true);
            Box("Breaker handle", breaker, new Vector3(0, 0, -.12f), new Vector3(.37f, .54f, .28f), "Safety red", true);
            Sign(utility, "3  BOILER\nISOLATION", new Vector3(3.13f, 2.81f, 36.63f), 1.31f, .50f, .10f);

            foreach (int index in new[] { 0, 1 })
            {
                var latch = Group(index == 0 ? "LatchAAnchor" : "LatchBAnchor", gameplay, new Vector3(1.31f + index * .87f, .61f, 36.41f)).transform;
                Cylinder("Latch collar", latch, Vector3.zero, .19f, .18f, "Aged brass", new Vector3(90, 0, 0));
                Box("Latch handle", latch, new Vector3(0, 0, -.11f), new Vector3(.50f, .13f, .11f), "Ivory moulding", true);
                latch.gameObject.AddComponent<BoxCollider>().size = new Vector3(.6f, .48f, .3f);
                Text("Latch number", latch, index == 0 ? "4A" : "4B", new Vector3(0, -.31f, -.13f), .092f, Lettering);
            }
            var restart = Group("RestartAnchor", gameplay, new Vector3(3.13f, .98f, 36.42f)).transform;
            Box("Restart base", restart, new Vector3(0, 0, .10f), new Vector3(.68f, .66f, .28f), "Pipe iron", true);
            Cylinder("Restart button", restart, new Vector3(0, .04f, -.105f), .20f, .16f, "Signal green", new Vector3(90, 0, 0));
            Text("Restart label", restart, "5  RESTART", new Vector3(0, -.24f, -.07f), .079f, Lettering);

            var gauge = Group("GaugeAnchor", gameplay, new Vector3(-.52f, 2.96f, 36.29f)).transform;
            Cylinder("Gauge brass case", gauge, Vector3.zero, .54f, .21f, "Aged brass", new Vector3(90, 0, 0));
            Cylinder("Gauge face", gauge, new Vector3(0, 0, -.12f), .475f, .025f, "Gauge ivory", new Vector3(90, 0, 0));
            for (int i = 0; i < 11; i++)
            {
                float a = (210 - i * 24) * Mathf.Deg2Rad;
                var tick = Box("Gauge tick", gauge, new Vector3(Mathf.Cos(a) * .38f, Mathf.Sin(a) * .38f, -.145f), new Vector3(.027f, .10f, .01f), i > 7 ? "Safety red" : "Ink", false, false);
                tick.transform.localEulerAngles = new Vector3(0, 0, a * Mathf.Rad2Deg - 90);
            }
            var needle = Group("Needle", gauge, new Vector3(0, 0, -.168f)).transform;
            Box("Needle blade", needle, new Vector3(0, .16f, 0), new Vector3(.032f, .38f, .02f), "Safety red", false, false);
            Sphere("Needle pin", gauge, new Vector3(0, 0, -.19f), Vector3.one * .095f, "Ink");
            needle.localEulerAngles = new Vector3(0, 0, 70);
            Text("Gauge units", gauge, "PRESSURE", new Vector3(0, -.22f, -.15f), .054f, Mat("Ink").color);
            Group("SteamAnchor", gameplay, new Vector3(-4.3f, 2.50f, 38.3f), new Vector3(0, -90, 0));

            var bench = Group("Repair workbench", utility, new Vector3(-5.3f, 0, 32.0f)).transform;
            Box("Workbench top", bench, new Vector3(0, 1.0f, 0), new Vector3(2.25f, .18f, 1.02f), "Walnut panels", true);
            for (int i = -1; i <= 1; i += 2) Box("Workbench legs", bench, new Vector3(i * .89f, .48f, 0), new Vector3(.18f, .97f, .78f), "Pipe iron");
            Box("New repair sheet", utility, new Vector3(6.76f, 2.18f, 35), new Vector3(.03f, 1.5f, 2.2f), "New plaster patch", false, false);
            Sign(utility, "BOILER REPAIR\nVALVE > PANEL > ISOLATION\nLATCH A > LATCH B > RESTART", new Vector3(4.8f, 2.15f, 39.61f), 3.1f, 1.05f, .13f);
        }

        static void Sign(Transform parent, string words, Vector3 position, float width, float height, float letterSize = .13f, float yaw = 0)
        {
            var sign = Group("Sign " + words.Replace('\n', ' '), parent, position, new Vector3(0, yaw, 0)).transform;
            Box("Brass frame", sign, Vector3.zero, new Vector3(width, height, .09f), "Aged brass", true, false);
            Box("Enamel face", sign, new Vector3(0, 0, -.055f), new Vector3(width - .10f, height - .10f, .045f), "Mahogany", true, false);
            Text("Lettering", sign, words, new Vector3(0, 0, -.085f), letterSize, Lettering);
        }

        static void Window(Transform parent, Vector3 position, float yaw)
        {
            var window = Group("Recessed window", parent, position, new Vector3(0, yaw, 0)).transform;
            Box("Deep reveal", window, Vector3.zero, new Vector3(2.55f, 1.8f, .20f), "Mahogany");
            Box("Daylight pane", window, new Vector3(0, 0, -.125f), new Vector3(2.22f, 1.53f, .03f), "Window blue", false, false);
            Box("Mullion", window, new Vector3(0, 0, -.16f), new Vector3(.11f, 1.54f, .06f), "Ivory moulding", false, false);
            Box("Crossbar", window, new Vector3(0, .02f, -.16f), new Vector3(2.23f, .08f, .06f), "Ivory moulding", false, false);
            Box("Sill", window, new Vector3(0, -.94f, -.20f), new Vector3(2.86f, .15f, .46f), "Ivory moulding");
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 4; i++)
                    Cylinder("Curtain fold", window, new Vector3(side * (1.10f + i * .13f), -.03f, -.30f), .11f, 2.07f, "Burgundy velvet");
            Pipe("Curtain rail", window, new Vector3(-1.65f, 1.04f, -.3f), new Vector3(1.65f, 1.04f, -.3f), .065f, "Aged brass");
        }

        static void Plant(Transform parent, Vector3 position)
        {
            var plant = Group("Lobby palm", parent, position).transform;
            Cylinder("Planter", plant, new Vector3(0, .34f, 0), .45f, .68f, "Aged brass", default, true);
            Cylinder("Pot soil", plant, new Vector3(0, .69f, 0), .38f, .035f, "Mahogany");
            for (int i = 0; i < 7; i++)
            {
                float angle = i * Mathf.PI * 2 / 7;
                var end = new Vector3(Mathf.Sin(angle) * .45f, 1.65f + (i % 2) * .35f, Mathf.Cos(angle) * .45f);
                Pipe("Stem", plant, new Vector3(0, .65f, 0), end, .035f, "Plant green");
                var leaf = Sphere("Leaf", plant, end, new Vector3(.34f, .83f, .20f), "Plant green");
                leaf.transform.localEulerAngles = new Vector3(25, angle * Mathf.Rad2Deg, -25);
            }
        }

        static void BuildLuggage(Transform parent, Vector3 position)
        {
            var cart = Group("Brass luggage cart", parent, position).transform;
            var body = cart.gameObject.AddComponent<Rigidbody>(); body.mass = 80;
            body.linearDamping = 1.4f; body.angularDamping = 3;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var controls = cart.gameObject.AddComponent<LuggageCart>(); controls.displayName = "Brass luggage cart";
            Box("Cart platform", cart, new Vector3(0, .28f, 0), new Vector3(1.50f, .18f, 1.12f), "Burgundy velvet", true);
            Box("Brass platform rim", cart, new Vector3(0, .20f, 0), new Vector3(1.55f, .08f, 1.17f), "Aged brass", true, false);
            var ground = cart.gameObject.AddComponent<BoxCollider>(); ground.center = new Vector3(0, .17f, 0); ground.size = new Vector3(1.40f, .32f, 1.0f);
            foreach (int side in new[] { -1, 1 })
            {
                Pipe("Cart upright", cart, new Vector3(side * .66f, .22f, -.47f), new Vector3(side * .66f, 1.92f, -.47f), .085f, "Aged brass");
                Pipe("Cart front upright", cart, new Vector3(side * .66f, .22f, .47f), new Vector3(side * .66f, 1.92f, .47f), .085f, "Aged brass");
                Pipe("Cart roof side", cart, new Vector3(side * .66f, 1.92f, -.47f), new Vector3(side * .66f, 1.92f, .47f), .085f, "Aged brass");
                Cylinder("Cart wheel", cart, new Vector3(side * .54f, .16f, -.3f), .15f, .09f, "Ink", new Vector3(0, 0, 90));
                Cylinder("Cart wheel", cart, new Vector3(side * .54f, .16f, .3f), .15f, .09f, "Ink", new Vector3(0, 0, 90));
            }
            Pipe("Cart top rail", cart, new Vector3(-.66f, 1.92f, -.47f), new Vector3(.66f, 1.92f, -.47f), .085f, "Aged brass");
            Pipe("Cart handle", cart, new Vector3(-.58f, 1.15f, -.65f), new Vector3(.58f, 1.15f, -.65f), .09f, "Aged brass");
            controls.handle = Group("Cart handle position", cart, new Vector3(0, 1.15f, -.65f)).transform;
            var handleHit = controls.handle.gameObject.AddComponent<BoxCollider>(); handleHit.size = new Vector3(1.28f, .30f, .20f);
            Text("Cart handle instructions", cart, "LUGGAGE SERVICE\nUSE HANDLE · PUSH · STEER", new Vector3(0, 1.38f, -.54f), .065f, Lettering);
            foreach (var child in cart.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        static void LooseSuitcase(Transform parent, Vector3 position, string material)
        {
            var suitcase = Group("Loose suitcase", parent, position);
            Box("Suitcase", suitcase.transform, Vector3.zero, new Vector3(.74f, .58f, .38f), material, true);
            Box("Leather strap", suitcase.transform, new Vector3(.19f, 0, 0), new Vector3(.09f, .60f, .4f), "Mahogany", true, false);
            Box("Case handle", suitcase.transform, new Vector3(0, .34f, 0), new Vector3(.26f, .12f, .08f), "Aged brass", true, false);
            var rigidbody = suitcase.AddComponent<Rigidbody>(); rigidbody.mass = 2.5f; rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            suitcase.AddComponent<PhysicsPickup>().itemName = "Guest suitcase";
        }

        static void BuildLights(Transform parent)
        {
            parent.gameObject.AddComponent<HotelAmbientLighting>().Apply();
            RenderSettings.reflectionIntensity = .4f;
            RenderSettings.fog = false;
            var sun = Group("Soft daylight bounce", parent, default, new Vector3(50, -30, 0)).AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .94f, .84f); sun.intensity = .55f; sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            for (int i = 0; i < 3; i++) PointLight(parent, "Lobby warm fill", new Vector3(-6 + i * 6, 3.18f, .3f), new Color(1, .77f, .48f), 4, 8);
            for (int i = 0; i < 4; i++)
                foreach (float side in new[] { -1.45f, 1.45f })
                    PointLight(parent, "Hall warm fill", new Vector3(side, 3.15f, 8 + i * 6), new Color(1, .79f, .53f), 1.5f, 4.5f);
            for (int i = 0; i < 6; i++) PointLight(parent, "Room light " + (101 + i), new Vector3(i % 2 == 0 ? -6 : 6, 3.25f, 10 + i / 2 * 7), new Color(1, .82f, .59f), 3.0f, 6.0f);
            PointLight(parent, "Boiler work light", new Vector3(-2, 3.28f, 35.5f), new Color(.82f, .90f, 1), 3.3f, 8);
            PointLight(parent, "Panel work light", new Vector3(4, 3.20f, 36), new Color(1, .85f, .61f), 3.0f, 6);
            foreach (var position in new[] { new Vector3(-2, 3.63f, 35.5f), new Vector3(4, 3.63f, 36) })
            {
                var fixture = Group("Industrial work light", parent, position).transform;
                Box("Light enclosure", fixture, Vector3.zero, new Vector3(1.7f, .16f, .54f), "Pipe iron", true, false);
                Box("Opal diffuser", fixture, new Vector3(0, -.10f, 0), new Vector3(1.45f, .08f, .37f), "Warm lamp", true, false);
                foreach (float x in new[] { -.5f, 0, .5f })
                    Box("Light guard", fixture, new Vector3(x, -.16f, 0), new Vector3(.035f, .035f, .46f), "Pipe iron", false, false);
            }
            for (int i = 0; i < 3; i++)
            {
                var fixture = Group("Lobby pendant", parent, new Vector3(-6 + i * 6, 3.40f, .3f)).transform;
                Cylinder("Suspension", fixture, new Vector3(0, .16f, 0), .04f, .50f, "Aged brass");
                Cylinder("Shade rim", fixture, new Vector3(0, -.12f, 0), .49f, .11f, "Aged brass");
                Sphere("Milk glass pendant", fixture, new Vector3(0, -.01f, 0), new Vector3(.91f, .36f, .91f), "Warm lamp");
            }
            var probes = Group("Light probes", parent).AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            for (int z = -2; z <= 38; z += 4)
                foreach (float x in new[] { -6f, 0f, 6f })
                    foreach (float y in new[] { .7f, 2.6f }) positions.Add(new Vector3(x, y, z));
            probes.probePositions = positions.ToArray();
            var reflection = Group("Lobby reflection", parent, new Vector3(0, 1.9f, .4f)).AddComponent<ReflectionProbe>();
            reflection.mode = ReflectionProbeMode.Realtime; reflection.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            reflection.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce; reflection.resolution = 128;
            reflection.size = new Vector3(20, 4, 11); reflection.boxProjection = true; reflection.intensity = .45f;
            string path = Root + "/Settings/HotelVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                var bloom = profile.Add<Bloom>(true); bloom.intensity.value = .10f; bloom.threshold.value = 1.2f;
                var tonemap = profile.Add<Tonemapping>(true); tonemap.mode.value = TonemappingMode.ACES;
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            }
            var volume = Group("Warm subtle grading", parent).AddComponent<Volume>();
            volume.isGlobal = true; volume.sharedProfile = profile;
        }

        static void PointLight(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = Group(name, parent, position).AddComponent<Light>();
            light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = LightShadows.None; light.lightmapBakeType = LightmapBakeType.Mixed;
        }
    }
}
