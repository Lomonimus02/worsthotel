using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Editor
{
    /// <summary>Small original modular kit. Generated meshes and materials are ordinary project assets.</summary>
    internal static class HotelKitAssets
    {
        internal const string Root = "Assets/_WorstHotel";
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        internal static Mesh SoftBlock;
        internal static Mesh Wheel;

        internal static void Prepare()
        {
            foreach (string dir in new[] { "Art/Materials", "Art/Models", "Art/Textures", "Art/VFX", "Art/Shaders", "Prefabs/Environment", "Prefabs/Gameplay", "Scenes", "Settings" })
                System.IO.Directory.CreateDirectory(Root + "/" + dir);
            AssetDatabase.Refresh();
            Materials.Clear();
            MakeMaterial("Cream plaster", new Color(.83f, .76f, .59f), .08f);
            MakeMaterial("New plaster patch", new Color(.93f, .87f, .72f), .05f);
            MakeMaterial("Ivory moulding", new Color(.94f, .86f, .68f), .22f);
            MakeMaterial("Mahogany", new Color(.32f, .16f, .095f), .3f);
            MakeMaterial("Walnut panels", new Color(.48f, .28f, .16f), .24f);
            MakeMaterial("Aged brass", new Color(.63f, .42f, .12f), .58f, .62f);
            MakeMaterial("Burgundy velvet", new Color(.36f, .045f, .078f), .15f);
            MakeMaterial("Teal upholstery", new Color(.045f, .29f, .28f), .16f);
            MakeMaterial("Cream linen", new Color(.88f, .84f, .72f), .1f);
            MakeMaterial("Boiler enamel", new Color(.2f, .38f, .36f), .38f, .22f);
            MakeMaterial("Pipe iron", new Color(.19f, .22f, .23f), .42f, .58f);
            MakeMaterial("Repair copper", new Color(.57f, .27f, .12f), .45f, .5f);
            MakeMaterial("Safety red", new Color(.66f, .065f, .037f), .32f);
            MakeMaterial("Signal green", new Color(.2f, .55f, .19f), .27f);
            MakeMaterial("Gauge ivory", new Color(.98f, .94f, .76f), .21f);
            MakeMaterial("Ink", new Color(.035f, .045f, .038f), .15f);
            MakeMaterial("Window blue", new Color(.48f, .71f, .8f), .65f, 0, .23f);
            MakeMaterial("Warm lamp", new Color(1, .74f, .36f), .15f, 0, 1.25f);
            MakeMaterial("Terminal glass", new Color(.075f, .19f, .15f), .4f, 0, .25f);
            MakeMaterial("Utility tile", new Color(.45f, .47f, .42f), .14f);
            MakeMaterial("Plant green", new Color(.14f, .33f, .12f), .15f);
            MakeMaterial("Luggage mustard", new Color(.75f, .49f, .12f), .2f);
            MakeWorldTextMaterial();
            MakeCarpet();
            SoftBlock = SaveMesh("SoftBlock", CreateSoftBlock());
            Wheel = SaveMesh("ValveWheel", CreateRing());
        }

        internal static Material Mat(string name) => Materials[name];

        static void MakeWorldTextMaterial()
        {
            const string path = Root + "/Art/Materials/World lettering.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("WorstHotel/World Text");
            if (shader == null) throw new System.InvalidOperationException("Missing original WorldText shader; reimport Assets/_WorstHotel/Art/Shaders.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_Color", Color.white);
            Materials["World lettering"] = material;
            EditorUtility.SetDirty(material);
        }

        static void MakeMaterial(string name, Color color, float smoothness, float metal = 0, float emission = 0)
        {
            string path = Root + "/Art/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metal);
            material.enableInstancing = true;
            if (emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            Materials[name] = material;
            EditorUtility.SetDirty(material);
        }

        static void MakeCarpet()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { name = "Grand carpet weave", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - 64), dy = Mathf.Abs(y - 64);
                float diamond = dx + dy;
                bool motif = Mathf.Abs(diamond - 32) < 2 || (dx < 3 && dy < 7) || (dy < 3 && dx < 7);
                bool corner = (x < 2 || x > 125 || y < 2 || y > 125);
                float weave = ((x + y) % 2 == 0) ? 1f : .94f;
                var col = motif ? new Color(.7f, .46f, .21f) : corner ? new Color(.32f, .09f, .09f) : new Color(.39f, .065f, .09f);
                tex.SetPixel(x, y, col * weave);
            }
            tex.Apply();
            string path = Root + "/Art/Textures/GrandCarpet.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) { EditorUtility.CopySerialized(tex, existing); Object.DestroyImmediate(tex); tex = existing; }
            else AssetDatabase.CreateAsset(tex, path);
            MakeMaterial("Grand carpet", Color.white, .06f);
            Mat("Grand carpet").SetTexture("_BaseMap", tex);
            Mat("Grand carpet").SetTextureScale("_BaseMap", new Vector2(12, 16));
        }

        static Mesh SaveMesh(string name, Mesh generated)
        {
            generated.name = name;
            string path = Root + "/Art/Models/" + name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) { EditorUtility.CopySerialized(generated, saved); Object.DestroyImmediate(generated); return saved; }
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        static Mesh CreateSoftBlock()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            float[] values = { -.5f, -.39f, .39f, .5f };
            foreach (var normal in faces)
            {
                Vector3 u = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(normal, u);
                int first = vertices.Count;
                for (int iy = 0; iy < 4; iy++)
                for (int ix = 0; ix < 4; ix++)
                {
                    var point = normal * .5f + u * values[ix] + v * values[iy];
                    var core = new Vector3(Mathf.Clamp(point.x, -.39f, .39f), Mathf.Clamp(point.y, -.39f, .39f), Mathf.Clamp(point.z, -.39f, .39f));
                    var n = (point - core).normalized;
                    vertices.Add(core + n * .11f);
                    normals.Add(n);
                    uv.Add(new Vector2(ix / 3f, iy / 3f));
                }
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    int a = first + y * 4 + x, b = a + 1, c = a + 4, d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateRing()
        {
            const int ringSegments = 32, tubeSegments = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= ringSegments; i++)
            for (int j = 0; j <= tubeSegments; j++)
            {
                float a = i * Mathf.PI * 2 / ringSegments, b = j * Mathf.PI * 2 / tubeSegments;
                float radius = .4f + .065f * Mathf.Cos(b);
                vertices.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, Mathf.Sin(b) * .065f));
                if (i == ringSegments || j == tubeSegments) continue;
                int p = i * (tubeSegments + 1) + j;
                triangles.Add(p); triangles.Add(p + tubeSegments + 1); triangles.Add(p + 1);
                triangles.Add(p + 1); triangles.Add(p + tubeSegments + 1); triangles.Add(p + tubeSegments + 2);
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        internal static GameObject Group(string name, Transform parent, Vector3 position = default, Vector3 rotation = default)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localEulerAngles = rotation;
            return obj;
        }

        internal static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, string material, bool soft = false, bool collision = true)
        {
            GameObject obj;
            if (soft)
            {
                obj = Group(name, parent, position);
                obj.AddComponent<MeshFilter>().sharedMesh = SoftBlock;
                obj.AddComponent<MeshRenderer>().sharedMaterial = Mat(material);
                if (collision) obj.AddComponent<BoxCollider>();
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = name; obj.transform.SetParent(parent, false); obj.transform.localPosition = position;
                obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
                if (!collision) Object.DestroyImmediate(obj.GetComponent<Collider>());
            }
            obj.transform.localScale = size;
            return obj;
        }

        internal static GameObject Sphere(string name, Transform parent, Vector3 position, Vector3 size, string material, bool collision = false)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = name; obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
            if (!collision) Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }

        internal static GameObject Cylinder(string name, Transform parent, Vector3 position, float radius, float length, string material, Vector3 rotation = default, bool collision = false)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            obj.name = name; obj.transform.SetParent(parent, false); obj.transform.localPosition = position;
            obj.transform.localEulerAngles = rotation; obj.transform.localScale = new Vector3(radius * 2, length * .5f, radius * 2);
            obj.GetComponent<Renderer>().sharedMaterial = Mat(material);
            if (!collision) Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }

        internal static void Pipe(string name, Transform parent, Vector3 start, Vector3 end, float radius, string material)
        {
            Vector3 delta = end - start;
            var obj = Cylinder(name, parent, (start + end) * .5f, radius, delta.magnitude, material);
            obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        }

        internal static TextMesh Text(string name, Transform parent, string content, Vector3 position, float size, Color color, Vector3 rotation = default)
        {
            var obj = Group(name, parent, position, rotation);
            var text = obj.AddComponent<TextMesh>();
            text.text = content; text.fontSize = 72; text.characterSize = size * 10f / text.fontSize;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = color;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = Mat("World lettering");
            obj.AddComponent<WorldTextFontBinding>().RefreshAtlas();
            return text;
        }

        internal static GameObject SavePrefab(GameObject source, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, Root + "/Prefabs/Environment/" + name + ".prefab");
            Object.DestroyImmediate(source);
            return prefab;
        }

        internal static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Vector3 rotation = default)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = position; instance.transform.localEulerAngles = rotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            return instance;
        }
    }
}
