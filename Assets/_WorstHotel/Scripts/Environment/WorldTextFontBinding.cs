using UnityEngine;

namespace WorstHotel
{
    /// <summary>Keep the depth-tested world material connected to Unity's dynamic font atlas.</summary>
    [ExecuteAlways, RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public sealed class WorldTextFontBinding : MonoBehaviour
    {
        TextMesh text;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock properties;

        void OnEnable()
        {
            text = GetComponent<TextMesh>();
            meshRenderer = GetComponent<MeshRenderer>();
            Font.textureRebuilt += FontTextureRebuilt;
            RefreshAtlas();
        }

        void OnDisable() => Font.textureRebuilt -= FontTextureRebuilt;

        void FontTextureRebuilt(Font rebuilt)
        {
            if (text != null && text.font == rebuilt) RefreshAtlas();
        }

        public void RefreshAtlas()
        {
            if (text == null) text = GetComponent<TextMesh>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (text.font == null || text.font.material == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetTexture("_MainTex", text.font.material.mainTexture);
            meshRenderer.SetPropertyBlock(properties);
        }
    }
}
