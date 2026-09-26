using UnityEngine;

namespace WorstHotel
{
    /// <summary>Paint tint visualizes delivered central heat, not an invented radiator-water temperature.</summary>
    public sealed class RadiatorHeatFeedback : MonoBehaviour
    {
        public int roomId;
        public Renderer[] fins;
        public Color coldMultiplier = new Color(.53f, .72f, .84f);
        public Color warmMultiplier = new Color(1.04f, .98f, .88f);
        MaterialPropertyBlock properties;
        Color[] originalColors;
        GameSession session;
        HotelSimulation simulation;
        RoomState room;
        int previousKey = -1;

        void Awake()
        {
            properties = new MaterialPropertyBlock();
            originalColors = new Color[fins != null ? fins.Length : 0];
            for (int i = 0; i < originalColors.Length; i++)
                if (fins[i] != null && fins[i].sharedMaterial != null) originalColors[i] = fins[i].sharedMaterial.GetColor("_BaseColor");
        }

        void Update()
        {
            var next = GameSession.Instance;
            if (session != next || (session != null && !ReferenceEquals(simulation, session.Simulation)))
            {
                session = next; simulation = session != null ? session.Simulation : null;
                room = session != null ? System.Array.Find(session.Rooms, state => state.Profile.Id == roomId) : null;
                previousKey = -1;
            }
            if (simulation == null || room == null) return;
            if (LocalCoopBootstrap.Instance != null && LocalCoopBootstrap.Instance.IsPaused) return;
            float gain = Mathf.Max(.01f, session.Settings.HeatTemperatureGain);
            float warmth = Mathf.Clamp01(simulation.Boiler.HeatingOutput * simulation.InfrastructureSettings.HeatMultiplier(room.RadiatorSetting) - room.Profile.HeatLoss / gain);
            int key = Mathf.RoundToInt(warmth * 100);
            if (key == previousKey) return;
            previousKey = key;
            Color tint = Color.Lerp(coldMultiplier, warmMultiplier, warmth);
            for (int i = 0; i < originalColors.Length; i++)
            {
                if (fins[i] == null) continue;
                fins[i].GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", originalColors[i] * tint);
                fins[i].SetPropertyBlock(properties);
            }
        }

        void OnDisable()
        {
            if (properties != null)
                for (int i = 0; i < originalColors.Length; i++)
                {
                    if (fins[i] == null) continue;
                    fins[i].GetPropertyBlock(properties);
                    properties.SetColor("_BaseColor", originalColors[i]); fins[i].SetPropertyBlock(properties);
                }
            session = null; simulation = null; room = null; previousKey = -1;
        }
    }
}
