using UnityEngine;

namespace WorstHotel
{
    /// <summary>The fan runs only when this actual tool delivers heat; pitch is independent of hotel time.</summary>
    [RequireComponent(typeof(PortableHeater))]
    public sealed class PortableHeaterAudio : MonoBehaviour
    {
        [Range(0, 1)] public float volume = .16f;
        [Min(1)] public float audibleRange = 12;
        PortableHeater heater;
        PortableHeaterState previousState;
        AudioSource fan;
        AudioClip clip;
        bool wasSwitchedOn, paused;

        void Awake()
        {
            heater = GetComponent<PortableHeater>();
            fan = gameObject.AddComponent<AudioSource>();
            fan.playOnAwake = false; fan.loop = true; fan.spatialBlend = 0;
            fan.volume = 0; fan.pitch = 1; fan.priority = 210;
            clip = MakeFanClip(); fan.clip = clip;
        }

        void LateUpdate()
        {
            var state = heater != null && heater.isActiveAndEnabled ? heater.State : null;
            if (!ReferenceEquals(state, previousState))
            {
                fan.Stop(); fan.volume = 0;
                previousState = state; wasSwitchedOn = state != null && state.SwitchedOn;
            }
            bool pause = LocalCoopBootstrap.Instance != null && LocalCoopBootstrap.Instance.IsPaused;
            if (pause != paused)
            {
                paused = pause;
                if (paused) fan.Pause(); else fan.UnPause();
            }
            if (paused) return;
            if (state != null && wasSwitchedOn != state.SwitchedOn)
                HotelFeedback.PlayHeaterSwitch(transform.position + Vector3.up * .6f);
            wasSwitchedOn = state != null && state.SwitchedOn;
            bool operating = state != null && state.EffectiveHeatOutput > 0 && state.RoomId.HasValue && !heater.IsCarried;
            if (!operating) { fan.Stop(); fan.volume = 0; return; }
            float target = volume * HotelFeedback.Audibility(transform.position + Vector3.up * .6f, Mathf.Max(1, audibleRange));
            fan.volume = Mathf.MoveTowards(fan.volume, target, Time.deltaTime * .45f);
            fan.pitch = 1;
            if (fan.volume > .001f && !fan.isPlaying) fan.Play();
            else if (fan.volume <= .001f && fan.isPlaying) fan.Stop();
        }

        static AudioClip MakeFanClip()
        {
            const int rate = 22050;
            var samples = new float[rate * 2];
            var random = new System.Random(7215);
            float low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                low += ((float)random.NextDouble() * 2 - 1 - low) * .06f;
                float hum = Mathf.Sin(t * Mathf.PI * 2 * 120) * .14f + Mathf.Sin(t * Mathf.PI * 2 * 240) * .035f;
                samples[i] = (hum + low * .30f) * Mathf.Clamp01(Mathf.Min(t, 2 - t) / .01f);
            }
            var result = AudioClip.Create("Original portable heater fan", samples.Length, 1, rate, false);
            result.SetData(samples, 0); return result;
        }

        void OnDisable()
        {
            if (fan != null) { fan.Stop(); fan.volume = 0; }
            previousState = null; paused = false;
        }

        void OnDestroy()
        {
            if (clip != null) Destroy(clip);
            if (fan != null) Destroy(fan);
        }
    }
}
