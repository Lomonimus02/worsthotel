using System;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Physical display and room luminaires follow the authoritative circuits, never inventing an outage.</summary>
    public sealed class ElectricalPanelPresentation : MonoBehaviour
    {
        [Serializable]
        public sealed class CircuitView
        {
            public string circuitId;
            public Transform lever, meterNeedle;
            public TextMesh readout;
            public TextMesh consumers;
            public Renderer warningLens;
            public Light warningLight;
            [NonSerialized] internal bool initialized, wasTripped, wasWarning;
            [NonSerialized] internal string lastReadout;
        }

        [Serializable]
        public sealed class RoomPowerBinding
        {
            public int roomId;
            public string circuitId;
            public Light[] lights;
            public Renderer[] luminousSurfaces;
            [NonSerialized] internal RoomState room;
            [NonSerialized] internal ElectricalCircuit circuit;
            [NonSerialized] internal float[] originalIntensities;
            [NonSerialized] internal float lastDim = -1;
            [NonSerialized] internal bool initialized, lastPower;
        }

        public DoorInteractable cover;
        public ReflectionProbe lobbyReflection;
        public CircuitView[] circuits = Array.Empty<CircuitView>();
        public RoomPowerBinding[] roomLights = Array.Empty<RoomPowerBinding>();
        GameSession session;
        HotelSimulation simulation;
        MaterialPropertyBlock properties;
        AudioSource hum, effects;
        AudioClip humClip, warningClip, relayClip;
        bool paused;
        int reflectedPowerState = -1;

        void Awake()
        {
            properties = new MaterialPropertyBlock();
            humClip = Synthesize(0); warningClip = Synthesize(1); relayClip = Synthesize(2);
            hum = gameObject.AddComponent<AudioSource>();
            hum.playOnAwake = false; hum.loop = true; hum.spatialBlend = 0; hum.volume = 0; hum.clip = humClip; hum.priority = 210;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false; effects.spatialBlend = 0; effects.priority = 100;
            foreach (var binding in roomLights)
            {
                binding.originalIntensities = new float[binding.lights.Length];
                for (int i = 0; i < binding.lights.Length; i++)
                    if (binding.lights[i] != null) binding.originalIntensities[i] = binding.lights[i].intensity;
            }
        }

        void Update()
        {
            var authority = GameSession.Instance;
            var next = authority != null ? authority.Simulation : null;
            if (session != authority || !ReferenceEquals(simulation, next))
            {
                session = authority; simulation = next;
                foreach (var view in circuits) { view.initialized = false; view.lastReadout = null; }
                foreach (var binding in roomLights)
                {
                    binding.initialized = false;
                    binding.room = session != null ? Array.Find(session.Rooms, room => room.Profile.Id == binding.roomId) : null;
                    binding.circuit = string.IsNullOrEmpty(binding.circuitId) ? simulation?.Electrical?.CircuitForRoom(binding.roomId) : simulation?.Electrical?.Find(binding.circuitId);
                    binding.lastDim = -1;
                }
            }
            var coop = LocalCoopBootstrap.Instance;
            bool pause = coop != null && coop.IsPaused;
            if (pause != paused)
            {
                paused = pause;
                if (paused) { hum.Pause(); effects.Pause(); }
                else { hum.UnPause(); effects.UnPause(); }
            }
            if (simulation?.Electrical == null) return;
            float audible = Audibility(coop);
            bool anyPower = false;
            int powerState = 0;
            foreach (var view in circuits)
            {
                var circuit = simulation.Electrical.Find(view.circuitId);
                if (circuit == null) continue;
                anyPower |= circuit.HasPower;
                if (circuit.HasPower) powerState |= circuit.Id == "A" ? 1 : 2;
                if (view.initialized && !paused)
                {
                    if (view.wasTripped != circuit.Tripped) effects.PlayOneShot(relayClip, audible * .65f);
                    else if (!view.wasWarning && circuit.Warning) effects.PlayOneShot(warningClip, audible * .30f);
                }
                view.wasTripped = circuit.Tripped; view.wasWarning = circuit.Warning; view.initialized = true;
                if (view.lever != null) view.lever.localRotation = Quaternion.Euler(circuit.Tripped ? 28 : -28, 0, 0);
                string readout = circuit.Tripped ? "TRIPPED" : circuit.Warning ? "OVERLOAD" :
                    CapacityBands.AtLeast(circuit.CapacityBand, CapacityBand.Strained) ? "HIGH" : "NORMAL";
                readout += "\n" + circuit.RequestedLoad.ToString("F2") + " / " + circuit.Capacity.ToString("F2") + " u" +
                    (simulation.Electrical.IsCapacityUpgraded(circuit.Id) ? "\nUPGRADED" : "");
                if (view.meterNeedle) view.meterNeedle.localRotation = Quaternion.Euler(0, 0,
                    Mathf.Lerp(70, -70, Mathf.Clamp01(circuit.RequestedLoad / Mathf.Max(.1f, circuit.Capacity * 1.4f))));
                if (view.lastReadout != readout && view.readout != null)
                { view.lastReadout = readout; view.readout.text = readout; }
                if (view.consumers != null) view.consumers.text = view.circuitId == "A" ? "101 · 103 · 105\n107 · 109\nWEST HALL" : "102 · 104 · 106\n108 · 110\nEAST HALL / SERVICE";
                bool capacityWarning = simulation.ContinuousOperations && CapacityBands.AtLeast(circuit.CapacityBand, CapacityBand.Strained);
                bool severe = simulation.ContinuousOperations && circuit.CapacityBand == CapacityBand.Critical;
                bool pulse = (circuit.Warning || severe) && !circuit.Tripped && Mathf.Sin(Time.time * 7) > 0;
                Color signal = circuit.Tripped || severe ? new Color(.95f, .08f, .025f) : circuit.Warning || capacityWarning ?
                    new Color(1, pulse ? .65f : .28f, .02f) : new Color(.12f, .62f, .23f);
                // A tripped circuit leaves a readable red lens, not a self-powered glowing lamp.
                SetSurface(view.warningLens, signal, circuit.HasPower ? signal * (pulse ? 1.3f : .35f) : Color.black);
                if (view.warningLight != null)
                {
                    view.warningLight.color = signal;
                    view.warningLight.intensity = circuit.Tripped ? 0 : pulse ? .06f : capacityWarning ? .03f : 0;
                }
            }
            foreach (var binding in roomLights)
            {
                if (binding.circuit == null) continue;
                bool power = binding.circuit.HasPower && (binding.room == null || binding.room.Operational);
                bool warning = power && binding.circuit != null && binding.circuit.Warning && !binding.circuit.Tripped;
                // Gentle 18% sag every2.4 real seconds. It never fabricates a blackout or accelerates in WAIT.
                float dim = warning ? 1 - .18f * (.5f + .5f * Mathf.Sin(Time.time * Mathf.PI * 2 / 2.4f)) : 1;
                if (binding.initialized && binding.lastPower == power && Mathf.Abs(binding.lastDim - dim) < .005f) continue;
                binding.initialized = true; binding.lastPower = power; binding.lastDim = dim;
                for (int i = 0; i < binding.lights.Length; i++)
                    if (binding.lights[i] != null)
                    { binding.lights[i].enabled = power; binding.lights[i].intensity = binding.originalIntensities[i] * dim; }
                foreach (var surface in binding.luminousSurfaces)
                {
                    if (surface == null || surface.sharedMaterial == null) continue;
                    var material = surface.sharedMaterial;
                    SetSurface(surface, material.GetColor("_BaseColor") * (power ? 1 : .22f),
                        power ? material.GetColor("_EmissionColor") * dim : Color.black);
                }
            }
            // The former OnAwake probe retained the powered lobby in brass/glass during an outage.
            // Refresh only on a real A/B transition, after their lights and materials have changed.
            if (lobbyReflection && reflectedPowerState != powerState)
            {
                reflectedPowerState = powerState;
                lobbyReflection.RenderProbe();
            }
            if (paused) return;
            hum.volume = Mathf.MoveTowards(hum.volume, anyPower ? audible * .055f : 0, Time.deltaTime * .4f);
            if (hum.volume > .001f && !hum.isPlaying) hum.Play();
            else if (hum.volume <= .001f && hum.isPlaying) hum.Stop();
        }

        public static string ConsumerBreakdown(ElectricalSystem electrical, string circuitId)
        {
            if (electrical == null) return "NO REGISTERED LOAD";
            var text = new StringBuilder();
            float rooms = 0, entertainment = 0;
            int occupied = 0;
            foreach (var item in electrical.Consumers)
            {
                if (item.CircuitId != circuitId || !item.Id.StartsWith("guest:", StringComparison.Ordinal)) continue;
                occupied++;
                float baseline = Math.Min(item.RequestedLoad, electrical.Settings.OccupiedRoomLoad);
                rooms += baseline; entertainment += Math.Max(0, item.RequestedLoad - baseline);
            }
            text.Append("ROOMS (").Append(occupied).Append(") ").Append(rooms.ToString("F2"));
            if (entertainment > 0) text.Append("\nTV / MUSIC ").Append(entertainment.ToString("F2"));
            int heaters = 0;
            foreach (var item in electrical.Consumers)
            {
                if (item.CircuitId != circuitId || !item.Id.StartsWith("heater:", StringComparison.Ordinal) || item.RequestedLoad <= 0) continue;
                heaters++;
                text.Append("\nHEATER ").Append(item.RoomId).Append("  ").Append(item.RequestedLoad.ToString("F2"));
            }
            if (heaters == 0) text.Append("\nHEATERS OFF");
            text.Append("\nREMOVE LOAD BEFORE RESET");
            return text.ToString();
        }

        void SetSurface(Renderer surface, Color color, Color emission)
        {
            if (surface == null) return;
            surface.GetPropertyBlock(properties); properties.SetColor("_BaseColor", color); properties.SetColor("_EmissionColor", emission);
            surface.SetPropertyBlock(properties);
        }

        float Audibility(LocalCoopBootstrap coop)
        {
            if (coop == null) return 0;
            float distance = 24;
            foreach (var player in coop.Players)
                if (player != null && player.PlayerCamera != null && coop.IsLocalActor(player.ActorId))
                    distance = Mathf.Min(distance, Vector3.Distance(transform.position + Vector3.up * 1.8f, player.PlayerCamera.transform.position));
            float near = 1 - distance / 24;
            return near * near;
        }

        static AudioClip Synthesize(int kind)
        {
            const int rate = 22050;
            float seconds = kind == 0 ? 2 : kind == 1 ? .42f : .18f;
            var samples = new float[Mathf.CeilToInt(rate * seconds)];
            var random = new System.Random(6107 + kind);
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float value = kind == 0 ? .18f * Mathf.Sin(100 * t * Mathf.PI * 2) + .035f * Mathf.Sin(200 * t * Mathf.PI * 2) :
                    kind == 1 ? Mathf.Sin(880 * t * Mathf.PI * 2) * .3f * (t < .14f || t > .24f ? 1 : 0) :
                    ((float)random.NextDouble() * 2 - 1 + Mathf.Sin(160 * t * Mathf.PI * 2)) * .34f * Mathf.Exp(-t * 35);
                samples[i] = value * Mathf.Clamp01(Mathf.Min(t, seconds - t) / .006f);
            }
            var clip = AudioClip.Create(kind == 0 ? "Original electrical panel hum" : kind == 1 ? "Original overload warning" : "Original breaker relay", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }

        void OnDisable()
        {
            if (hum != null) hum.Stop();
            if (effects != null) effects.Stop();
            foreach (var binding in roomLights)
            {
                if (binding.originalIntensities == null) continue;
                for (int i = 0; i < binding.lights.Length; i++)
                    if (binding.lights[i] != null) binding.lights[i].intensity = binding.originalIntensities[i];
                foreach (var surface in binding.luminousSurfaces)
                    if (surface != null && surface.sharedMaterial != null)
                        SetSurface(surface, surface.sharedMaterial.GetColor("_BaseColor") * (binding.lastPower ? 1 : .22f),
                            binding.lastPower ? surface.sharedMaterial.GetColor("_EmissionColor") : Color.black);
                binding.lastDim = -1;
            }
            simulation = null; session = null; paused = false; reflectedPowerState = -1;
        }

        void OnDestroy()
        {
            if (humClip != null) Destroy(humClip);
            if (warningClip != null) Destroy(warningClip);
            if (relayClip != null) Destroy(relayClip);
            if (hum != null) Destroy(hum);
            if (effects != null) Destroy(effects);
        }
    }
}
