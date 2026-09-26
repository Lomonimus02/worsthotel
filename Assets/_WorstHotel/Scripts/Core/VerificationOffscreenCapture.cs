#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorstHotel
{
    /// <summary>
    /// Explicit verification-only GPU capture, independent of the window's backbuffer.
    /// Call on the main thread outside a render callback, after the UI has had a frame to update.
    /// The caller owns and must destroy the returned texture.
    /// </summary>
    public static class VerificationOffscreenCapture
    {
        static bool capturing;

        // This reports submission of the real IMGUI renderer list, not a visual assertion.
        public static bool LastOverlaySubmitted { get; private set; }

        public static Texture2D Capture(FirstPersonController[] players)
        {
            if (capturing) throw new InvalidOperationException("Verification capture is already rendering.");
            if (players == null || players.Length == 0)
                throw new ArgumentException("Verification capture requires the actual player cameras.", nameof(players));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Offscreen verification requires a graphics device; omit -nographics.");

            capturing = true;
            LastOverlaySubmitted = false;
            var oldActive = RenderTexture.active;
            RenderTexture composite = null;
            Texture2D pixels = null;
            try
            {
                int width = Mathf.Max(1, Screen.width);
                int height = Mathf.Max(1, Screen.height);
                composite = Allocate(width, height, "Verification split-screen composite");
                RenderTexture.active = composite;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = oldActive;

                Camera overlayCamera = null;
                for (int i = 0; i < players.Length; i++)
                {
                    var camera = players[i] ? players[i].PlayerCamera : null;
                    if (!camera) throw new InvalidOperationException("Verification player camera is missing.");
                    overlayCamera = camera;
                    var viewport = camera.rect;
                    int x = Mathf.Clamp(Mathf.RoundToInt(viewport.xMin * width), 0, width - 1);
                    int y = Mathf.Clamp(Mathf.RoundToInt(viewport.yMin * height), 0, height - 1);
                    int right = Mathf.Clamp(Mathf.RoundToInt(viewport.xMax * width), x + 1, width);
                    int top = Mathf.Clamp(Mathf.RoundToInt(viewport.yMax * height), y + 1, height);
                    var view = Allocate(right - x, top - y, "Verification player viewport");
                    try
                    {
                        RenderCamera(camera, view);
                        Graphics.CopyTexture(view, 0, 0, 0, 0, view.width, view.height,
                            composite, 0, 0, x, y);
                    }
                    finally { Release(view); }
                }

                RenderOverlay(overlayCamera, composite);
                RenderTexture.active = composite;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false)
                { name = "Verification GPU readback" };
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                return pixels;
            }
            catch
            {
                if (pixels) Destroy(pixels);
                throw;
            }
            finally
            {
                RenderTexture.active = oldActive;
                Release(composite);
                capturing = false;
            }
        }

        static void RenderCamera(Camera camera, RenderTexture destination)
        {
            var rect = camera.rect;
            var aspect = camera.aspect;
            var target = camera.targetTexture;
            try
            {
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)destination.width / destination.height;
                var request = new RenderPipeline.StandardRequest { destination = destination };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("The current render pipeline does not support StandardRequest.");
                // URP's StandardRequest renders the actual camera stack, including post processing.
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            finally
            {
                camera.targetTexture = target;
                camera.rect = rect;
                camera.aspect = aspect;
            }
        }

        static void RenderOverlay(Camera camera, RenderTexture composite)
        {
            // URP intentionally excludes overlay UI from cameras targeting a RenderTexture.
            // Its own DrawScreenSpaceUIPass uses this same LowLevel renderer list for IMGUI.
            // A separate request supplies a valid SRP context after the world views are composed.
            var scratch = Allocate(composite.width, composite.height, "Verification overlay context");
            Exception failure = null;
            bool receivedContext = false;
            void DrawOverlay(ScriptableRenderContext context, Camera renderedCamera)
            {
                if (renderedCamera != camera || receivedContext) return;
                receivedContext = true;
                var command = CommandBufferPool.Get("Verification actual IMGUI overlay");
                try
                {
                    var overlay = context.CreateUIOverlayRendererList(camera, UISubset.LowLevel);
                    command.SetRenderTarget(composite);
                    command.SetViewport(new Rect(0, 0, composite.width, composite.height));
                    command.DrawRendererList(overlay);
                    context.ExecuteCommandBuffer(command);
                    context.Submit();
                    LastOverlaySubmitted = true;
                }
                catch (Exception exception) { failure = exception; }
                finally { CommandBufferPool.Release(command); }
            }

            RenderPipelineManager.endCameraRendering += DrawOverlay;
            try
            {
                RenderCamera(camera, scratch);
                if (failure != null)
                    throw new InvalidOperationException("The native IMGUI overlay capture failed.", failure);
                if (!receivedContext)
                    throw new InvalidOperationException("The render request did not supply an SRP camera context.");
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= DrawOverlay;
                Release(scratch);
            }
        }

        static RenderTexture Allocate(int width, int height, string name)
        {
            var texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default, 1);
            texture.name = name;
            texture.filterMode = FilterMode.Point;
            return texture;
        }

        static void Release(RenderTexture texture)
        {
            if (texture) RenderTexture.ReleaseTemporary(texture);
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
#endif
