using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Original activity loops at authored room sources, audible to either local staff member.</summary>
    public sealed class LivingGuestAudio : MonoBehaviour
    {
        public GuestPresentation presentation;
        [Range(0, 1)] public float masterVolume = .65f;
        [Min(1)] public float audibleRange = 14;

        sealed class RoomChannel
        {
            public GuestRoomMarkers Markers;
            public RoomState Room;
            public GuestStay Guest;
            public AudioSource Source;
        }

        readonly List<RoomChannel> channels = new List<RoomChannel>(6);
        readonly Dictionary<int, RoomChannel> rooms = new Dictionary<int, RoomChannel>();
        AudioClip showerClip, musicClip, phoneClip, rehearsalClip;
        GameSession session;
        HotelSimulation simulation;
        bool dirty = true, paused;

        void Awake()
        {
            if (presentation == null) presentation = GetComponent<GuestPresentation>();
            showerClip = Synthesize(false);
            musicClip = Synthesize(true);
            phoneClip = SynthesizePhone();
            rehearsalClip = SynthesizeRehearsal();
            if (presentation == null || presentation.roomMarkers == null) return;
            foreach (var markers in presentation.roomMarkers)
            {
                if (markers == null || rooms.ContainsKey(markers.roomId)) continue;
                var child = new GameObject("Room " + markers.roomId + " activity sound");
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                // Unity has one listener. Distance is measured against both player cameras below.
                source.spatialBlend = 0; source.playOnAwake = false; source.loop = true;
                source.volume = 0; source.priority = 190;
                var channel = new RoomChannel { Markers = markers, Source = source };
                channels.Add(channel); rooms.Add(markers.roomId, channel);
            }
        }

        void OnEnable() => Bind();

        void Bind()
        {
            if (session == GameSession.Instance) return;
            if (session != null) session.Changed -= MarkDirty;
            session = GameSession.Instance;
            if (session != null) session.Changed += MarkDirty;
            dirty = true;
        }

        void MarkDirty() => dirty = true;

        void Refresh()
        {
            dirty = false;
            var next = session != null ? session.Simulation : null;
            if (!ReferenceEquals(simulation, next)) { StopAll(); simulation = next; }
            foreach (var channel in channels) { channel.Room = null; channel.Guest = null; }
            if (session == null || simulation == null || !simulation.LivingEnabled) return;
            foreach (var room in session.Rooms)
            {
                if (rooms.TryGetValue(room.Profile.Id, out var channel)) channel.Room = room;
            }
            foreach (var guest in simulation.Guests)
            {
                if (guest.Agent != null && guest.Agent.InAssignedRoom && rooms.TryGetValue(guest.RoomId, out var channel))
                    channel.Guest = guest;
            }
        }

        void LateUpdate()
        {
            Bind();
            if (dirty) Refresh();
            var coop = LocalCoopBootstrap.Instance;
            bool pause = coop != null && coop.IsPaused;
            if (pause != paused)
            {
                paused = pause;
                foreach (var channel in channels)
                    if (paused) channel.Source.Pause(); else channel.Source.UnPause();
            }
            if (paused) return;
            bool service = session != null && session.Phase == DayPhase.Service && simulation != null;
            foreach (var channel in channels)
            {
                var agent = channel.Guest?.Agent;
                bool active = service && agent != null && agent.InAssignedRoom && channel.Room != null;
                bool staged = active && agent.ActivityStaged;
                bool shower = staged && agent.Activity == GuestActivity.Shower;
                bool music = staged && (agent.Activity == GuestActivity.LoudRoom || agent.Activity == GuestActivity.WatchTV) && channel.Room.HasPower;
                bool phone = staged && agent.Activity == GuestActivity.PhoneCall;
                var amplifier = staged && channel.Room.HasPower ? simulation.Services?.ActiveAmplifier(channel.Guest) : null;
                var equipmentAnchor = amplifier == null ? null : ServiceSupplyItem.FindLuggageTransform(amplifier.Id);
                var anchor = equipmentAnchor ? equipmentAnchor : shower ? channel.Markers.shower : phone ? channel.Markers.phoneAnchor : channel.Markers.loud;
                var clip = equipmentAnchor ? rehearsalClip : shower ? showerClip : phone ? phoneClip : music ? musicClip : null;
                if (anchor == null) clip = null;
                var source = channel.Source;
                if (source.clip != clip)
                {
                    source.Stop(); source.volume = 0; source.clip = clip;
                }
                if (clip == null) continue;
                source.transform.position = anchor.position + Vector3.up;
                float level = Mathf.Clamp01(channel.Room.SourceNoise);
                float volume = .42f * masterVolume * level * Audibility(source.transform.position, coop);
                // A closed room muffles, rather than erases, its real shower/radio source. Loud
                // music is therefore discoverable from the corridor before a complaint arrives.
                if (channel.Markers.door != null && !channel.Markers.door.IsOpen) volume *= .68f;
                source.volume = Mathf.MoveTowards(source.volume, volume, Time.deltaTime * 1.5f);
                if (source.volume > .001f && !source.isPlaying) source.Play();
                else if (source.volume <= .001f && source.isPlaying) source.Stop();
            }
        }

        float Audibility(Vector3 origin, LocalCoopBootstrap coop)
        {
            if (coop == null) return 0;
            float range = Mathf.Max(1, audibleRange), distance = range;
            foreach (var player in coop.Players)
                if (player != null && player.PlayerCamera != null && coop.IsLocalActor(player.ActorId))
                    distance = Mathf.Min(distance, Vector3.Distance(origin, player.PlayerCamera.transform.position));
            float near = 1 - Mathf.Clamp01(distance / range);
            return near * near;
        }

        void StopAll()
        {
            foreach (var channel in channels)
            {
                if (channel.Source == null) continue;
                channel.Source.Stop(); channel.Source.volume = 0; channel.Source.clip = null;
            }
        }

        static AudioClip Synthesize(bool music)
        {
            const int rate = 22050;
            const float duration = 4;
            var samples = new float[(int)(rate * duration)];
            var random = new System.Random(music ? 4705 : 4706);
            float low = 0;
            // An original eight-beat toy-radio phrase; no recorded or downloaded sound assets.
            float[] notes = { 196, 233.0819f, 261.6256f, 233.0819f, 196, 293.6648f, 261.6256f, 174.6141f };
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float noise = (float)random.NextDouble() * 2 - 1;
                low += (noise - low) * .16f;
                float value;
                if (music)
                {
                    float beat = t % .5f;
                    float note = notes[Mathf.Min(7, Mathf.FloorToInt(t * 2))];
                    float melody = (Mathf.Sin(note * t * Mathf.PI * 2) + .22f * Mathf.Sin(note * t * Mathf.PI * 4)) *
                        Mathf.Exp(-beat * 9) * Mathf.Clamp01(beat / .006f);
                    float kick = Mathf.Sin(60 * beat * Mathf.PI * 2) * Mathf.Exp(-beat * 28);
                    value = melody * .28f + kick * .20f + (noise - low) * Mathf.Exp(-beat * 55) * .10f;
                }
                else value = (noise - low) * .22f + low * .09f * (1 + .15f * Mathf.Sin(t * Mathf.PI * 8));
                float edge = Mathf.Clamp01(Mathf.Min(t, duration - t) / .008f);
                samples[i] = Mathf.Clamp(value * edge, -.8f, .8f);
            }
            var clip = AudioClip.Create(music ? "Original guest radio phrase" : "Original guest shower water", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip SynthesizeRehearsal()
        {
            const int rate = 22050;
            var samples = new float[rate * 8];
            float[] riff = { 110, 164.8138f, 196, 220, 196, 164.8138f, 146.8324f, 0 };
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate, beat = t % .5f, note = riff[(int)(t * 2) % riff.Length];
                float pick = Mathf.Clamp01(beat / .006f) * Mathf.Exp(-beat * 8);
                float stringSound = note == 0 ? 0 : (Mathf.Sin(2 * Mathf.PI * note * t) + .35f * Mathf.Sin(4 * Mathf.PI * note * t) +
                    .12f * Mathf.Sin(6 * Mathf.PI * note * t)) * pick;
                samples[i] = (Mathf.Clamp(stringSound * 1.3f, -.65f, .65f) * .38f + .008f * Mathf.Sin(2 * Mathf.PI * 50 * t)) *
                    Mathf.Clamp01(Mathf.Min(t, 8 - t) / .02f);
            }
            var clip = AudioClip.Create("Original touring guitar rehearsal", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }

        static AudioClip SynthesizePhone()
        {
            // Deliberately nonverbal, original low-pass voiced syllables: the cadence suggests
            // a conversation through a wall without recorded dialogue or another request system.
            const int rate = 22050;
            var samples = new float[rate * 6];
            float smoothed = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate, phrase = t % 3;
                float pause = phrase < 1.9f ? 1 : 0;
                float syllable = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * 17.8f)), .65f);
                float pitch = 125 + 19 * Mathf.Sin(t * 4.2f) + 8 * Mathf.Sin(t * 11.1f);
                float voiced = Mathf.Sin(t * pitch * Mathf.PI * 2) * .36f + Mathf.Sin(t * pitch * Mathf.PI * 4) * .17f;
                smoothed += (voiced - smoothed) * .11f;
                samples[i] = smoothed * syllable * pause * Mathf.Clamp01(Mathf.Min(t, 6 - t) / .02f);
            }
            var clip = AudioClip.Create("Original muffled guest phone conversation", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }

        void OnDisable()
        {
            if (session != null) session.Changed -= MarkDirty;
            session = null; simulation = null; paused = false; dirty = true;
            StopAll();
        }

        void OnDestroy()
        {
            if (showerClip != null) Destroy(showerClip);
            if (musicClip != null) Destroy(musicClip);
            if (phoneClip != null) Destroy(phoneClip);
            if (rehearsalClip != null) Destroy(rehearsalClip);
            foreach (var channel in channels) if (channel.Source != null) Destroy(channel.Source.gameObject);
            channels.Clear(); rooms.Clear();
        }
    }
}
