using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>
    /// Original placeholder sound and restrained effects driven by authoritative hotel state.
    /// Shared-screen sound is attenuated using the nearer staff member, so player 2 is not deaf
    /// to a machine merely because the single Unity AudioListener belongs to player 1.
    /// </summary>
    public sealed partial class HotelFeedback : MonoBehaviour
    {
        public static HotelFeedback Instance { get; private set; }
        public Transform steamAnchor, sparkAnchor, vibratingPipe;
        public Material particleMaterialTemplate;
        [Range(0, 1)] public float masterVolume = .65f;

        enum Sound { Hum, Hiss, Knock, Door, Step, Complaint, Click, Restart, Arrival }
        readonly Dictionary<Sound, AudioClip> clips = new Dictionary<Sound, AudioClip>();
        readonly Dictionary<DoorInteractable, bool> doorStates = new Dictionary<DoorInteractable, bool>();
        readonly List<AudioSource> pausedSources = new List<AudioSource>();
        readonly float[] stepDistance = new float[2];
        AudioSource hum, hiss, effects, ui, roomKnock;
        AudioSource[] feet;
        ParticleSystem steam, drops, sparks;
        Material particleMaterial;
        Texture2D particleTexture;
        HotelSimulation simulation;
        Vector3 pipeRest;
        float knockTimer, lastComplaintTime = -10;
        bool audioPaused;

        void Awake()
        {
            Instance = this;
            if (vibratingPipe != null) pipeRest = vibratingPipe.localPosition;
            foreach (Sound sound in System.Enum.GetValues(typeof(Sound))) clips.Add(sound, Synthesize(sound));
            hum = Source("Boiler hum", true); hum.clip = clips[Sound.Hum];
            hiss = Source("Pressure hiss", true); hiss.clip = clips[Sound.Hiss];
            effects = Source("Hotel mechanical and notification sounds", false);
            ui = Source("Reception UI clicks", false);
            roomKnock = Source("Door knock", false); roomKnock.minDistance = 1; roomKnock.maxDistance = 12; roomKnock.rolloffMode = AudioRolloffMode.Linear;
            pausedSources.Add(roomKnock);
            feet = new[] { Source("Staff 1 footsteps", false), Source("Staff 2 footsteps", false) };
            feet[0].panStereo = -.16f; feet[1].panStereo = .16f;
            pausedSources.AddRange(new[] { hum, hiss, effects, feet[0], feet[1] });
            CreateLivingCues();
            foreach (var door in FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None)) doorStates.Add(door, door.IsOpen);
            doorSnapshot = new DoorInteractable[doorStates.Count];
            doorStates.Keys.CopyTo(doorSnapshot, 0);
            CreateEffects();
            BindSimulation();
        }

        public static void PlayUIClick()
        {
            if (Instance == null || !Instance.isActiveAndEnabled || Instance.ui == null) return;
            Instance.ui.PlayOneShot(Instance.clips[Sound.Click], Instance.masterVolume * .20f);
        }

        public static void PlayRoomKnock(Vector3 position)
        {
            if (!Instance || !Instance.CanEmitGameplayCue() || !Instance.effects) return;
            var coop = LocalCoopBootstrap.Instance;
            bool split = coop && !coop.IsSolo && coop.LanRole == LanRole.Offline;
            Instance.roomKnock.transform.position = position;
            Instance.roomKnock.spatialBlend = split ? 0 : 1;
            Instance.roomKnock.PlayOneShot(Instance.clips[Sound.Knock], Instance.masterVolume * .55f * (split ? Audibility(position, 10) : 1));
        }

        AudioSource Source(string label, bool loop)
        {
            var child = new GameObject(label); child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.spatialBlend = 0;
            source.volume = loop ? 0 : 1; source.priority = loop ? 180 : 100;
            return source;
        }

        void BindSimulation()
        {
            var next = GameSession.Instance != null ? GameSession.Instance.Simulation : null;
            if (ReferenceEquals(next, simulation)) return;
            UnbindSimulation();
            ResetTransientFeedback();
            simulation = next;
            if (simulation == null) return;
            simulation.Boiler.OnFailureStarted += FailureStarted;
            simulation.Boiler.OnFailureResolved += FailureResolved;
            simulation.Incidents.OnIncidentStarted += ComplaintStarted;
        }

        void UnbindSimulation()
        {
            if (simulation == null) return;
            simulation.Boiler.OnFailureStarted -= FailureStarted;
            simulation.Boiler.OnFailureResolved -= FailureResolved;
            simulation.Incidents.OnIncidentStarted -= ComplaintStarted;
            simulation = null;
        }

        void Update()
        {
            BindSimulation();
            var coop = LocalCoopBootstrap.Instance;
            bool paused = coop != null && coop.IsPaused;
            if (paused != audioPaused)
            {
                audioPaused = paused;
                foreach (var source in pausedSources)
                    if (paused) source.Pause(); else source.UnPause();
            }
            if (paused) return;
            UpdateLivingCues();
            var session = GameSession.Instance;
            bool active = session != null && session.Phase == DayPhase.Service && simulation != null;
            float pressure = active ? Mathf.Clamp01(simulation.Boiler.Pressure / session.BoilerSettings.MaxPressure) : 0;
            bool maintaining = active && simulation.Boiler.MaintenanceInProgress;
            bool failed = active && simulation.Boiler.Failed && !maintaining;
            bool relief = active && simulation.Boiler.ReliefActorId >= 0;
            bool strained = active && !maintaining && simulation.Boiler.CapacityModelEnabled && CapacityBands.AtLeast(simulation.Boiler.CapacityBand, CapacityBand.Strained);
            bool overloaded = active && !maintaining && simulation.Boiler.CapacityModelEnabled && CapacityBands.AtLeast(simulation.Boiler.CapacityBand, CapacityBand.Overloaded);
            float audibility = Audibility(steamAnchor ? steamAnchor.position : new Vector3(0, 1.5f, 37), 31);
            float output = active ? simulation.Boiler.HeatingOutput : 0;
            // Near-capacity operation already has a distinct note before overload raises
            // pressure. One band offset, recomputed from live state, never stacks or changes
            // the pressure model; failure/maintenance retain their own existing sound cues.
            float strainNote = strained && !failed ? .065f : 0;
            SetLoop(hum, active && !maintaining ? (.13f + output * .16f) * audibility * masterVolume : 0,
                .87f + pressure * .22f + strainNote);
            SetLoop(hiss, failed || relief ? (.11f + pressure * .25f) * audibility * masterVolume : 0, relief ? 1.12f : .93f);
            SetEmission(steam, active && (failed || relief) ? (relief ? 19 : 8 + pressure * 12) : 0);
            SetEmission(drops, failed ? 3.5f : 0);
            if (vibratingPipe != null)
            {
                float amount = failed ? .013f : active && !maintaining && (pressure > .65f || strained) ? .004f : 0;
                vibratingPipe.localPosition = pipeRest + new Vector3(Mathf.Sin(Time.time * 24), 0, Mathf.Sin(Time.time * 19)) * amount;
            }
            if (active && !maintaining && (failed || overloaded || simulation.Boiler.Pressure >= session.BoilerSettings.WarningPressure))
            {
                knockTimer -= Time.deltaTime;
                if (knockTimer <= 0)
                {
                    effects.PlayOneShot(clips[Sound.Knock], audibility * masterVolume * .55f);
                    knockTimer = Mathf.Lerp(1.8f, .82f, pressure);
                }
            }
            else knockTimer = .4f;
            UpdateDoors();
            UpdateFootsteps(coop);
        }

        static void SetLoop(AudioSource source, float targetVolume, float pitch)
        {
            source.pitch = pitch;
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, Time.deltaTime * .9f);
            if (source.volume > .002f && !source.isPlaying) source.Play();
            else if (source.volume <= .002f && source.isPlaying) source.Stop();
        }

        void UpdateDoors()
        {
            // Six doors: fixed storage, no scene search or per-frame temporary collections.
            foreach (var door in doorStates.Keys)
            {
                if (door == null || doorStates[door] == door.IsOpen) continue;
                effects.PlayOneShot(clips[Sound.Door], masterVolume * .30f * Audibility(door.transform.position, 15));
            }
            // Updating values while a Dictionary enumerator is live is invalid on some Unity runtimes.
            foreach (var door in doorSnapshot)
                if (door != null) doorStates[door] = door.IsOpen;
        }

        DoorInteractable[] doorSnapshot;

        void UpdateFootsteps(LocalCoopBootstrap coop)
        {
            if (coop == null) return;
            for (int i = 0; i < 2; i++)
            {
                var player = coop.Players[i];
                if (player == null || player.BodyCollider == null) continue;
                Vector3 velocity = player.PresentationVelocity; velocity.y = 0;
                if (!player.PresentationGrounded || velocity.sqrMagnitude < .09f) { stepDistance[i] = .9f; continue; }
                stepDistance[i] += velocity.magnitude * Time.deltaTime;
                if (stepDistance[i] < 1.55f) continue;
                stepDistance[i] -= 1.55f;
                feet[i].pitch = player.transform.position.z > 29 ? 1.12f : .88f;
                feet[i].PlayOneShot(clips[Sound.Step], masterVolume * .23f * (coop.LanRole == LanRole.Offline ? 1 :
                    Audibility(player.transform.position + Vector3.up, 12)));
            }
        }

        public static float Audibility(Vector3 origin, float range)
        {
            var coop = LocalCoopBootstrap.Instance;
            if (coop == null) return 0;
            float distance = range;
            foreach (var player in coop.Players)
                if (player != null && player.PlayerCamera != null && coop.IsLocalActor(player.ActorId))
                    distance = Mathf.Min(distance, Vector3.Distance(origin, player.PlayerCamera.transform.position));
            float near = 1 - Mathf.Clamp01(distance / range);
            return near * near;
        }

        void FailureStarted()
        {
            if (!CanEmitGameplayCue()) return;
            if (sparks != null) sparks.Emit(9);
            knockTimer = 0;
        }

        void FailureResolved()
        {
            if (effects != null && CanEmitGameplayCue())
                effects.PlayOneShot(clips[Sound.Restart], masterVolume * .32f);
        }

        void ComplaintStarted(HotelIncident incident)
        {
            if (!CanEmitGameplayCue()) return;
            if (Time.unscaledTime - lastComplaintTime < .6f) return;
            lastComplaintTime = Time.unscaledTime;
            RingReceptionComplaint();
        }

        bool CanEmitGameplayCue() => isActiveAndEnabled && GameSession.Instance != null &&
            GameSession.Instance.Phase == DayPhase.Service &&
            (LocalCoopBootstrap.Instance == null || !LocalCoopBootstrap.Instance.IsPaused);

        void ResetTransientFeedback()
        {
            foreach (var source in pausedSources) if (source != null) source.Stop();
            if (hum != null) hum.volume = 0;
            if (hiss != null) hiss.volume = 0;
            SetEmission(steam, 0); SetEmission(drops, 0);
            if (steam != null) steam.Clear();
            if (drops != null) drops.Clear();
            if (sparks != null) sparks.Clear();
            knockTimer = .4f;
            lastComplaintTime = -10;
            stepDistance[0] = stepDistance[1] = .9f;
            ResetLivingCues();
        }

        void CreateEffects()
        {
            particleTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Original soft boiler particle", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var point = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f);
                float alpha = Mathf.Pow(Mathf.Clamp01(1 - point.sqrMagnitude), 2);
                particleTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            particleTexture.Apply(false, true);
            particleMaterial = particleMaterialTemplate != null ? new Material(particleMaterialTemplate) : new Material(Shader.Find("WorstHotel/World Text"));
            particleMaterial.name = "Boiler soft particles";
            particleMaterial.mainTexture = particleTexture;
            particleMaterial.SetColor("_Color", Color.white);
            steam = Particles("Pressure steam", steamAnchor, new Color(.88f, .93f, 1, .23f), .32f, 1.5f, .82f, 55);
            var size = steam.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .40f, 1, 1.6f));
            var velocity = steam.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(.26f);
            drops = Particles("Small pressure leak", steamAnchor, new Color(.56f, .79f, .94f, .70f), .045f, 1.0f, .18f, 12);
            drops.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var dropMain = drops.main; dropMain.gravityModifier = .6f;
            sparks = Particles("Failure contact sparks", sparkAnchor, new Color(1, .64f, .18f, .9f), .045f, .21f, 2.1f, 14);
            sparks.transform.localPosition = new Vector3(.48f, .15f, -.22f);
            sparks.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var sparkMain = sparks.main; sparkMain.gravityModifier = .7f;
        }

        ParticleSystem Particles(string label, Transform parent, Color color, float size, float lifetime, float speed, int maximum)
        {
            var obj = new GameObject(label); obj.transform.SetParent(parent != null ? parent : transform, false);
            var particles = obj.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = true; main.startLifetime = lifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .75f, size * 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .8f, speed * 1.2f);
            main.startColor = color; main.maxParticles = maximum; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15; shape.radius = .025f;
            var emission = particles.emission; emission.rateOverTime = 0;
            var fade = particles.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            return particles;
        }

        static void SetEmission(ParticleSystem particles, float rate)
        {
            if (particles == null) return;
            var emission = particles.emission;
            emission.rateOverTime = rate;
        }

        static AudioClip Synthesize(Sound kind)
        {
            const int sampleRate = 22050;
            float seconds = kind == Sound.Hum || kind == Sound.Hiss ? 2f : kind == Sound.Complaint ? .76f :
                kind == Sound.Restart ? .82f : kind == Sound.Step ? .14f : kind == Sound.Click ? .065f : .26f;
            int length = Mathf.CeilToInt(seconds * sampleRate);
            var samples = new float[length];
            var random = new System.Random(711 + (int)kind * 37);
            float filteredNoise = 0;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)sampleRate;
                float noise = (float)random.NextDouble() * 2 - 1;
                filteredNoise += (noise - filteredNoise) * .23f;
                float value;
                switch (kind)
                {
                    case Sound.Hum:
                        value = (.24f * Sine(70, t) + .1f * Sine(140, t) + .045f * Sine(210, t)) * (.88f + .12f * Sine(3, t));
                        break;
                    case Sound.Hiss:
                        value = (noise - filteredNoise) * .33f * Mathf.Min(1, Mathf.Min(t / .018f, (seconds - t) / .018f));
                        break;
                    case Sound.Knock:
                        value = (.62f * Sine(105, t) + .25f * Sine(246, t) + filteredNoise * .25f) * Mathf.Exp(-t * 23);
                        break;
                    case Sound.Door:
                        value = (.40f * Sine(91, t) + .32f * filteredNoise) * Mathf.Exp(-t * 20);
                        break;
                    case Sound.Step:
                        value = (.55f * filteredNoise + .26f * Sine(82, t)) * Mathf.Exp(-t * 25);
                        break;
                    case Sound.Complaint:
                        value = (.27f * Sine(740, t) + .17f * Sine(1180, t)) *
                            (.45f + .55f * Mathf.Abs(Sine(19, t))) * Mathf.Exp(-t * 2.2f);
                        break;
                    case Sound.Arrival:
                        value = (.36f * Sine(1320, t) + .15f * Sine(1980, t)) * Mathf.Exp(-t * 15);
                        break;
                    case Sound.Restart:
                        value = .30f * Sine(t < .22f ? 440 : t < .44f ? 554 : 659, t) * Mathf.Exp(-t * 3);
                        break;
                    default:
                        value = (.32f * Sine(1200, t) + noise * .17f) * Mathf.Exp(-t * 70);
                        break;
                }
                float fadeIn = kind == Sound.Hum || kind == Sound.Hiss ? 1 : Mathf.Clamp01(t / .002f);
                float fadeOut = kind == Sound.Hum || kind == Sound.Hiss ? 1 : Mathf.Clamp01((seconds - t) / .015f);
                samples[i] = Mathf.Clamp(value * fadeIn * fadeOut, -.85f, .85f);
            }
            var clip = AudioClip.Create("Original hotel " + kind, length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static float Sine(float frequency, float t) => Mathf.Sin(frequency * t * Mathf.PI * 2);

        void OnDisable()
        {
            UnbindSimulation();
            ResetTransientFeedback();
            if (ui != null) ui.Stop();
            if (vibratingPipe != null) vibratingPipe.localPosition = pipeRest;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var clip in clips.Values) if (clip != null) Destroy(clip);
            if (particleMaterial != null) Destroy(particleMaterial);
            if (particleTexture != null) Destroy(particleTexture);
            if (steam != null) Destroy(steam.gameObject);
            if (drops != null) Destroy(drops.gameObject);
            if (sparks != null) Destroy(sparks.gameObject);
            foreach (var source in pausedSources) if (source != null) Destroy(source.gameObject);
            if (ui != null) Destroy(ui.gameObject);
        }
    }
}
