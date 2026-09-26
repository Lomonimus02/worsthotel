using UnityEngine;

namespace WorstHotel
{
    public sealed partial class GuestPresentation
    {
        bool SynchronizeResponseRoute(VisualGuest guest)
        {
            var agent = guest.Stay.Agent;
            bool responseActivity = agent.InAssignedRoom &&
                (agent.Activity == GuestActivity.AdjustRadiator || agent.Activity == GuestActivity.CallReception);
            if (string.IsNullOrEmpty(agent.ResponseActionId) || (!agent.IsServiceReceptionTrip && !responseActivity))
            {
                guest.ResponseActionId = null;
                return false;
            }
            bool changed = guest.ResponseActionId != agent.ResponseActionId || guest.ResponseActionVersion != agent.ResponseActionVersion ||
                guest.State != agent.State || guest.Activity != agent.Activity;
            if (!changed) return true;
            guest.ResponseActionId = agent.ResponseActionId; guest.ResponseActionVersion = agent.ResponseActionVersion;
            guest.ResponseArrivalReported = false; guest.ResponseRetryAfter = 0;
            guest.State = agent.State; guest.Activity = agent.Activity;
            switch (agent.State)
            {
                case GuestAgentState.GoingToServiceReception:
                    SetRoute(guest, AuthoredGuestRoute.ToServiceReception(guest.Root.position, guest.Room,
                        receptionPlaces[guest.AppearanceIndex % receptionPlaces.Length].position, guest.InsideRoom), RoutePurpose.ServiceReception);
                    break;
                case GuestAgentState.WaitingAtServiceReception:
                    guest.Route = null; guest.RouteComplete = true; guest.InsideRoom = false;
                    guest.PathStatus = "Waiting to speak at reception";
                    guest.ResponseArrivalReported = true;
                    break;
                case GuestAgentState.ReturningFromServiceReception:
                    SetRoute(guest, AuthoredGuestRoute.ToRoom(guest.Root.position, guest.Room, guest.InsideRoom), RoutePurpose.ServiceReturn);
                    break;
                default:
                    if (guest.InsideRoom)
                        SetRoute(guest, AuthoredGuestRoute.Activity(guest.Root.position, guest.Room, agent.Activity), RoutePurpose.Activity);
                    break;
            }
            return true;
        }

        void ReportResponseArrival(VisualGuest guest, GuestResponseAnchor anchor)
        {
            var agent = guest.Stay.Agent;
            if (guest.ResponseArrivalReported || simulation.Elapsed < guest.ResponseRetryAfter ||
                string.IsNullOrEmpty(guest.ResponseActionId)) return;
            // Capture/version identity belongs to the route that actually completed, not to
            // a newer action that might have replaced it while the guest was walking.
            if (guest.ResponseActionId != agent.ResponseActionId || guest.ResponseActionVersion != agent.ResponseActionVersion)
            { guest.ResponseArrivalReported = true; return; }
            var result = simulation.SignalGuestResponseAnchorReached(guest.Id, guest.ResponseActionId,
                guest.ResponseActionVersion, anchor);
            guest.ResponseArrivalReported = result.Success;
            guest.ResponseRetryAfter = simulation.Elapsed + 1;
            guest.PathStatus = result.Success ? "Reached " + anchor : "At " + anchor + "; waiting for valid contact";
            if (result.Success) session.RaiseChanged();
        }

        void RetryCompletedResponseRoute(VisualGuest guest)
        {
            if (!guest.RouteComplete || guest.ResponseArrivalReported) return;
            if (guest.Purpose == RoutePurpose.ServiceReception && guest.State == GuestAgentState.GoingToServiceReception)
                ReportResponseArrival(guest, GuestResponseAnchor.Reception);
            else if (guest.Purpose == RoutePurpose.ServiceReturn && guest.State == GuestAgentState.ReturningFromServiceReception)
                ReportResponseArrival(guest, GuestResponseAnchor.AssignedRoom);
        }
    }
}
