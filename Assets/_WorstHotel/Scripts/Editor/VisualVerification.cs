using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WorstHotel.Editor
{
    public static class VisualVerification
    {
        // Run in batch mode WITH a graphics device (omit -nographics).
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/_WorstHotel/Scenes/PrototypeHotel.unity");
            Directory.CreateDirectory("docs/screenshots");
            var go = new GameObject("VerificationCamera");
            var cam = go.AddComponent<Camera>();
            cam.AdditionalData();
            cam.fieldOfView = 68;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 90;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.34f,.42f,.48f);
            CaptureView(cam, "service052-lobby-door", new Vector3(4, 1.8f, -2.2f), new Vector3(10, 1.8f, -1.6f));
            CaptureView(cam, "service052-hall", new Vector3(11, 1.8f, -1.65f), new Vector3(25, 1.7f, -1.65f));
            CaptureView(cam, "service052-boiler", new Vector3(24.6f, 1.75f, 1.6f), new Vector3(24.8f, 1.7f, 6.4f));
            CaptureView(cam, "service052-electrical", new Vector3(26.1f, 1.75f, 1.6f), new Vector3(28.95f, 1.8f, 2.3f));
            CaptureView(cam, "service052-laundry", new Vector3(13.3f, 1.75f, .85f), new Vector3(13.4f, 1.3f, 5.7f));
            CaptureView(cam, "service052-staff", new Vector3(19.0f, 1.75f, .75f), new Vector3(17.6f, 1.1f, 3));
            CaptureView(cam, "service052-guest-gallery", new Vector3(0, 1.8f, 29.8f), new Vector3(.1f, 1.8f, 44));
            Object.DestroyImmediate(go);
            Debug.Log("WORST HOTEL: verification views saved.");
        }

        static void AdditionalData(this Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
        }

        static void CaptureView(Camera cam, string name, Vector3 position, Vector3 target)
        {
            cam.transform.position = position;
            cam.transform.LookAt(target);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            texture.Apply();
            File.WriteAllBytes("docs/screenshots/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous;
            cam.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(rt);
        }
    }
}
