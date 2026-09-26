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
            CaptureView(cam, "lobby", new Vector3(3, 1.8f, -2.5f), new Vector3(-3, 1.4f, 3));
            CaptureView(cam, "corridor", new Vector3(0, 1.8f, 7), new Vector3(.1f, 1.7f, 25));
            CaptureView(cam, "utility", new Vector3(2, 1.8f, 31), new Vector3(-.5f, 1.5f, 36.8f));
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
