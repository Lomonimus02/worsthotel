using UnityEngine;

namespace WorstHotel
{
    /// <summary>Presentation only: the large analog dial and warning lamp read the authoritative boiler.</summary>
    public sealed class BoilerReadout : MonoBehaviour
    {
        public Transform needle;
        public Light warningLight;
        public Renderer warningLens;
        public TextMesh capacityReadout;
        public GameObject capacityDisplay;
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
            if (capacityDisplay && capacityDisplay.activeSelf != boiler.CapacityModelEnabled) capacityDisplay.SetActive(boiler.CapacityModelEnabled);
            if (capacityReadout && boiler.CapacityModelEnabled)
            {
                capacityReadout.text = boiler.MaintenanceInProgress ?
                    "MAINTENANCE · HEATING OFF\n" + BoilerMaintenanceLabels.Remaining(session.Simulation) + " LEFT\nREADY " + GuestLabels.HotelMoment(session.Simulation, boiler.MaintenanceEndsAt) : CapacityLabels.BoilerReadout(boiler);
                capacityReadout.color = boiler.Failed || boiler.CapacityModelEnabled && CapacityBands.AtLeast(boiler.CapacityBand, CapacityBand.Overloaded) ?
                    new Color(.65f, .12f, .06f) : new Color(.18f, .21f, .19f);
            }
            bool pressureWarning = boiler.Pressure >= session.BoilerSettings.WarningPressure;
            bool strained = !boiler.MaintenanceInProgress && boiler.CapacityModelEnabled && CapacityBands.AtLeast(boiler.CapacityBand, CapacityBand.Strained);
            bool alarm = !boiler.MaintenanceInProgress && (boiler.Failed || pressureWarning || boiler.CapacityModelEnabled && CapacityBands.AtLeast(boiler.CapacityBand, CapacityBand.Overloaded));
            bool critical = !boiler.MaintenanceInProgress && (boiler.Failed || boiler.CapacityModelEnabled && boiler.CapacityBand == CapacityBand.Critical);
            float pulse = alarm ? .65f + .35f * Mathf.Sin(Time.time * 8) : .15f;
            if (warningLight) warningLight.intensity = alarm ? pulse * 2 : strained ? .25f : 0;
            Color signal = boiler.MaintenanceInProgress ? new Color(.18f, .45f, .78f) : critical || !boiler.CapacityModelEnabled && alarm ? new Color(.95f, .16f, .045f) :
                alarm || strained ? new Color(.95f, .57f, .08f) : new Color(.20f, .35f, .18f);
            if (warningLight) warningLight.color = signal;
            if (lensMaterial)
            {
                lensMaterial.SetColor("_BaseColor", signal);
                lensMaterial.SetColor("_EmissionColor", alarm || strained ? signal * pulse : Color.black);
            }
        }
        void OnDestroy() { if (lensMaterial) Destroy(lensMaterial); }
    }
}
