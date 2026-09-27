using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed class StaffBedInteraction : HotelInteractable
    {
        public int bedId;
        public Transform standingAnchor;
        public Transform interactionTarget;
        public Vector3 InteractionPoint => interactionTarget ? interactionTarget.position : transform.position + Vector3.up * .55f;
        static readonly List<StaffBedInteraction> beds = new List<StaffBedInteraction>();

        void OnEnable() { if (!beds.Contains(this)) beds.Add(this); }
        void OnDisable() { beds.Remove(this); }
        internal static bool TryFind(int id, out StaffBedInteraction bed)
        {
            bed = null;
            foreach (var candidate in beds)
            {
                if (!candidate || !candidate.isActiveAndEnabled || candidate.bedId != id) continue;
                if (bed) { bed = null; return false; }
                bed = candidate;
            }
            return bed && (id == 0 || id == 1);
        }

        public override string GetPrompt(PlayerInteractor actor)
        {
            var wait = GameSession.Instance ? GameSession.Instance.Wait : null;
            if (!wait || actor == null) return "Staff bed";
            if (wait.HasSleepConsent(actor.ActorId))
                return wait.IsSleeping ? "Sleeping until 06:00 · press again to wake" : "Ready for sleep · waiting for your colleague";
            var allowed = wait.CanUseBed(actor.ActorId, bedId);
            return allowed.Success ? "Sleep until 06:00 · hotel time continues" : allowed.Message;
        }

        public override bool CanInteract(PlayerInteractor actor) => actor != null && GameSession.Instance &&
            GameSession.Instance.Wait && GameSession.Instance.Wait.CanUseBed(actor.ActorId, bedId).Success;
        public override void Interact(PlayerInteractor actor)
        {
            if (actor == null || !GameSession.Instance || !GameSession.Instance.Wait) return;
            GameSession.Instance.Wait.TrySleep(actor.ActorId, bedId);
        }
        // Releasing the initiating button is rearming, not cancellation of latched consent.
    }
}
