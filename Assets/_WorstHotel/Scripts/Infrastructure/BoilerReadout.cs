using UnityEngine;

namespace WorstHotel
{
    /// <summary>Presentation only: the large analog dial and warning lamp read the authoritative boiler.</summary>
    public sealed class BoilerReadout : MonoBehaviour
    {
        public Transform needle;
        public Light warningLight;
        public Renderer warningLens;
        Material lensMaterial;
        void Start()
        {
            if (warningLens) lensMaterial = warningLens.material;
        }
        void Update()
        {
            var session = GameSession.Instance;
            if (!session || session.Simulation == null) return;
            var boiler = session.Simulation.Boiler;
            if (needle) needle.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(120, -120, boiler.Pressure / session.BoilerSettings.MaxPressure));
            bool alarm = boiler.Failed || boiler.Pressure >= session.BoilerSettings.WarningPressure;
            float pulse = alarm ? .65f + .35f * Mathf.Sin(Time.time * 8) : .15f;
            if (warningLight) warningLight.intensity = alarm ? pulse * 2 : 0;
            if (lensMaterial)
            {
                lensMaterial.SetColor("_BaseColor", alarm ? new Color(.95f, .16f, .045f) : new Color(.20f, .35f, .18f));
                lensMaterial.SetColor("_EmissionColor", alarm ? new Color(1, .09f, .015f) * pulse : Color.black);
            }
        }
        void OnDestroy() { if (lensMaterial) Destroy(lensMaterial); }
    }
}
