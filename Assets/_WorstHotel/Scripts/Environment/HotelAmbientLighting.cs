using UnityEngine;
using UnityEngine.Rendering;

namespace WorstHotel
{
    /// <summary>Deterministic bounce-light approximation for the unbaked prototype interior.</summary>
    [ExecuteAlways]
    public sealed class HotelAmbientLighting : MonoBehaviour
    {
        public Color ambientFill = new Color(.22f, .24f, .29f);
        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientFill;
            RenderSettings.ambientIntensity = 1;
            // A batch render can run before Unity updates the generated ambient probe. Supplying it
            // explicitly also keeps runtime and verification-camera lighting identical.
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(ambientFill.linear);
            RenderSettings.ambientProbe = probe;
        }
    }
}
