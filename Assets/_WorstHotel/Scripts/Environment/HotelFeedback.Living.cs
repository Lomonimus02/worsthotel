using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelFeedback
    {
        public Transform receptionBellAnchor, receptionPhoneAnchor, phoneReceiver;
        public Renderer phoneLens;
        public bool ReceptionPhoneRinging => phoneRingSeconds > 0;
        AudioSource guestFeet, receptionArrival, receptionPhone;
        MaterialPropertyBlock phoneProperties;
        Quaternion phoneRest;
        float phoneRingSeconds, nextGuestStep, nextArrivalBell;

        void CreateLivingCues()
        {
            guestFeet = Source("Guest footsteps", false); guestFeet.pitch = .93f; guestFeet.priority = 190;
            receptionArrival = Source("Reception arrival bell", false); receptionArrival.priority = 120;
            receptionPhone = Source("Reception complaint phone", false); receptionPhone.priority = 110;
            pausedSources.AddRange(new[] { guestFeet, receptionArrival, receptionPhone });
            phoneProperties = new MaterialPropertyBlock();
            if (phoneReceiver != null) phoneRest = phoneReceiver.localRotation;
        }

        bool CanEmitWorldCue() => isActiveAndEnabled && simulation != null &&
            (LocalCoopBootstrap.Instance == null || !LocalCoopBootstrap.Instance.IsPaused);

        public static void PlayGuestFootstep(Vector3 origin)
        {
            var feedback = Instance;
            if (feedback == null || !feedback.CanEmitWorldCue() || Time.unscaledTime < feedback.nextGuestStep) return;
            feedback.nextGuestStep = Time.unscaledTime + .055f;
            // Pitch and cap use real time. WAIT moves the actors faster without pitching up their audio.
            float volume = feedback.masterVolume * .18f * Audibility(origin + Vector3.up * .2f, 12);
            if (volume > .001f) feedback.guestFeet.PlayOneShot(feedback.clips[Sound.Step], volume);
        }

        public static void PlayReceptionArrival()
        {
            var feedback = Instance;
            if (feedback == null || !feedback.CanEmitGameplayCue() || feedback.receptionBellAnchor == null ||
                Time.unscaledTime < feedback.nextArrivalBell) return;
            feedback.nextArrivalBell = Time.unscaledTime + .45f;
            feedback.receptionArrival.PlayOneShot(feedback.clips[Sound.Arrival], feedback.masterVolume * .36f *
                Audibility(feedback.receptionBellAnchor.position, 22));
        }

        public static void PlayHeaterSwitch(Vector3 origin)
        {
            var feedback = Instance;
            if (feedback == null || !feedback.CanEmitWorldCue()) return;
            feedback.effects.PlayOneShot(feedback.clips[Sound.Click], feedback.masterVolume * .28f * Audibility(origin, 10));
        }

        void RingReceptionComplaint()
        {
            if (receptionPhoneAnchor == null || receptionPhone == null) return;
            phoneRingSeconds = 1.6f;
            receptionPhone.PlayOneShot(clips[Sound.Complaint], masterVolume * .55f * Audibility(receptionPhoneAnchor.position, 26));
        }

        void UpdateLivingCues()
        {
            phoneRingSeconds = Mathf.Max(0, phoneRingSeconds - Time.deltaTime);
            float wobble = phoneRingSeconds > 0 ? Mathf.Sin(Time.time * 38) * 2.3f : 0;
            if (phoneReceiver != null) phoneReceiver.localRotation = phoneRest * Quaternion.Euler(0, 0, wobble);
            SetPhoneLens(phoneRingSeconds > 0 && Mathf.Sin(Time.time * 12) > 0);
        }

        void SetPhoneLens(bool lit)
        {
            if (phoneLens == null || phoneProperties == null) return;
            phoneLens.GetPropertyBlock(phoneProperties);
            phoneProperties.SetColor("_BaseColor", lit ? new Color(1, .51f, .12f) : new Color(.18f, .09f, .035f));
            phoneProperties.SetColor("_EmissionColor", lit ? new Color(1, .32f, .04f) * 1.2f : Color.black);
            phoneLens.SetPropertyBlock(phoneProperties);
        }

        void ResetLivingCues()
        {
            phoneRingSeconds = nextGuestStep = nextArrivalBell = 0;
            if (phoneReceiver != null) phoneReceiver.localRotation = phoneRest;
            SetPhoneLens(false);
        }
    }
}
