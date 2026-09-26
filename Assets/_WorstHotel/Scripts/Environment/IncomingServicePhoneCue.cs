using UnityEngine;

namespace WorstHotel
{
    /// <summary>A local audible reception telephone. Ring eligibility comes only from the host model.</summary>
    public sealed class IncomingServicePhoneCue : MonoBehaviour
    {
        public GameObject ringIndicator;
        [Range(0, 1)] public float volume = .45f;
        [Min(1)] public float range = 22;
        public bool IsRinging { get; private set; }
        AudioSource source;
        AudioClip clip;
        string responseId;
        bool paused;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.spatialBlend = 0; source.priority = 90;
            // Original two-bell tone with a quiet gap, rather than an event/toast per ring.
            const int rate = 16000;
            var samples = new float[rate * 4];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float pulse = t < .7f ? t : t > 1 && t < 1.7f ? t - 1 : -1;
                if (pulse < 0) continue;
                float envelope = Mathf.Clamp01(pulse * 45) * Mathf.Clamp01((.7f - pulse) * 18);
                samples[i] = envelope * (.27f * Mathf.Sin(2 * Mathf.PI * 790 * t) + .19f * Mathf.Sin(2 * Mathf.PI * 1050 * t));
            }
            clip = AudioClip.Create("Original reception incoming telephone", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); source.clip = clip;
        }

        void Update()
        {
            var session = GameSession.Instance;
            var coop = LocalCoopBootstrap.Instance;
            var call = session && session.Phase == DayPhase.Service ? session.Simulation?.Services?.IncomingCall : null;
            IsRinging = call != null;
            bool pause = coop && coop.IsPaused;
            if (!IsRinging)
            {
                responseId = null; if (source.isPlaying || paused) source.Stop(); paused = false;
                if (ringIndicator) ringIndicator.SetActive(false);
                return;
            }
            float distance = range;
            if (coop)
                foreach (var player in coop.Players)
                    if (player && player.PlayerCamera && coop.IsLocalActor(player.ActorId))
                        distance = Mathf.Min(distance, Vector3.Distance(transform.position, player.PlayerCamera.transform.position));
            source.volume = volume * Mathf.Pow(1 - Mathf.Clamp01(distance / Mathf.Max(1, range)), 1.4f);
            if (responseId != call.Id)
            { source.Stop(); responseId = call.Id; if (!pause) source.Play(); }
            if (pause != paused) { paused = pause; if (pause) source.Pause(); else source.UnPause(); }
            if (!pause && !source.isPlaying) source.Play();
            if (ringIndicator) ringIndicator.SetActive(!pause && Mathf.Repeat(Time.unscaledTime, 1.0f) < .6f);
        }

        void OnDisable()
        { if (source) source.Stop(); paused = false; responseId = null; IsRinging = false; if (ringIndicator) ringIndicator.SetActive(false); }
        void OnDestroy() { if (clip) Destroy(clip); }
    }
}
