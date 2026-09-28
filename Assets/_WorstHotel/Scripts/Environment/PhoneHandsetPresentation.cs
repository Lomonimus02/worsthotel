using UnityEngine;
namespace WorstHotel
{
    [DefaultExecutionOrder(600)]
    public sealed class PhoneHandsetPresentation : MonoBehaviour
    {
        public Transform handset;
        Vector3 restPosition;
        Quaternion restRotation;
        void Start() { if (handset) { restPosition = handset.localPosition; restRotation = handset.localRotation; } }
        void LateUpdate()
        {
            if (!handset) return;
            var session = GameSession.Instance;
            var ui = ManagementUI.Instance;
            var coop = LocalCoopBootstrap.Instance;
            int actor = session ? session.PhoneHolder : -1;
            if (ui && ui.IsWakePhoneOpen) actor = ui.Owner;
            // Other owners' handset poses arrive with the authoritative world frame.
            if (session && session.IsLanReplica && actor < 0) return;
            Vector3 position = handset.parent.TransformPoint(restPosition);
            Quaternion rotation = handset.parent.rotation * restRotation;
            if (actor >= 0 && coop && coop.Players[actor])
            {
                var view = coop.Players[actor].PlayerCamera.transform;
                position = view.TransformPoint(new Vector3(.30f, -.25f, .44f));
                rotation = view.rotation * Quaternion.Euler(15, 20, 105);
            }
            float blend = 1 - Mathf.Exp(-15 * Time.unscaledDeltaTime);
            handset.position = Vector3.Lerp(handset.position, position, blend);
            handset.rotation = Quaternion.Slerp(handset.rotation, rotation, blend);
        }
    }
}
