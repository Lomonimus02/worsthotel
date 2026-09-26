#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class LanDevelopmentVerification
    {
        IEnumerator ReceiveNaturalConcernHost(ServiceCase request, string stage)
        {
            Require(session.Simulation.Services.Settings.NaturalCommunicationEnabled && !request.IsKnownToHotel,
                "new concern remains private before actual remote telephone interaction");
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            var aim = phone.GetComponent<Collider>().bounds.center;
            PositionEmptyServiceActor(new Vector3(aim.x, .08f, aim.z - 1.45f), aim);
            session.RaiseChanged(); WriteStage(stage + "-approach");
            yield return Stage(stage + "-focused", 20);
            var response = request.Response;
            yield return Until(() => response.Phase == GuestResponsePhase.Contacting || serviceGuest.Agent.ResponseActionId == null,
                25, "an existing physical self-help action finishes before diagnostic phone selection");
            if (response.Phase != GuestResponsePhase.Contacting)
                Require(session.Simulation.DebugBeginGuestContact(serviceGuest.GuestId, GuestContactChannel.Phone).Success,
                    "diagnostic contact intent starts an actual room-phone route");
            yield return Until(() => session.Simulation.Services.IncomingCall == response, 20,
                "guest physically reaches room phone before reception rings");
            Require(!request.IsKnownToHotel && serviceGuest.Memory.ServicesRequested ==
                session.Simulation.Services.Cases.Count(c => c.IsKnownToHotel), "ringing does not publish an undisclosed service");
            Require(!session.AnswerIncomingServiceCall(0, response.Id).Success,
                "employee without physical telephone grant cannot answer another employee's incoming call");
            session.RaiseChanged(); WriteStage(stage + "-ringing");
            yield return Until(() => response.CommunicatedAt >= 0, 12,
                "actual client phone menu sends authoritative incoming-answer command");
            Require(request.IsKnownToHotel && response.Channel == GuestContactChannel.Phone &&
                response.ContactAttempts >= 1 && request.Status != ServiceStatus.InProgress && request.Status != ServiceStatus.Fulfilled,
                "remote answer reveals the same request without accepting or resolving it");
            yield return Stage(stage + "-closed", 8);
            yield return Until(() => !coop.Players[1].IsUIBlocked, 5, "remote telephone menu closes before subsequent interaction");
            facts.Add("NATURAL LAN PHONE: case=" + request.Id + "; response=" + response.Id +
                "; actualRoomPhoneRoute=True; privateUntilAnswer=True; remotePhysicalAnswer=True; answerDoesNotAccept=True.");
        }

        IEnumerator ReceiveNaturalConcernClient(ServiceKind kind, string stage)
        {
            yield return Stage(stage + "-approach", kind == ServiceKind.WakeUpCall ? 180 : 110);
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            var aim = phone.GetComponent<Collider>().bounds.center;
            yield return Until(() => Horizontal(coop.Players[1].transform.position,
                new Vector3(aim.x, .08f, aim.z - 1.45f)) < .2f, 6, "empty telephone approach arrives in world snapshot");
            yield return ServicesAim(() => aim, "Reception telephone");
            var request = session.Simulation.Services.Cases.Single(c => c.Kind == kind);
            Require(!request.IsKnownToHotel && !GuestLabels.IsKnownOpenService(request),
                "client retains private response data without advertising a task");
            WriteStage(stage + "-focused");
            yield return Stage(stage + "-ringing", 22);
            yield return Until(() => session.Simulation.Services.IncomingCall?.Id == request.Response.Id, 5,
                "same ringing identity arrives by normal model snapshots");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => ManagementUI.Instance.IsWakePhoneOpen, 5, "remote physical phone grants scoped incoming menu");
            Require(!session.Simulation.Services.FindCase(request.Id).IsKnownToHotel, "opening phone does not disclose the concern");
            if (capture) yield return Capture("client-" + stage + "-ringing");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => session.Simulation.Services.FindCase(request.Id).IsKnownToHotel, 6,
                "host-approved answer returns communicated response to replica");
            var known = session.Simulation.Services.FindCase(request.Id);
            Require(known.Status != ServiceStatus.InProgress && known.Status != ServiceStatus.Fulfilled && known.Response.Id == request.Response.Id,
                "remote incoming answer is separate from an agreement and preserves case identity");
            if (capture) yield return Capture("client-" + stage + "-heard");
            yield return TapButton(GamepadButton.East);
            yield return Until(() => !ManagementUI.Instance.IsOpen, 3, "remote incoming conversation closes");
            WriteStage(stage + "-closed");
        }
    }
}
#endif
