#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        string blanketStock = "3", radiatorSetting = "1";

        void DrawRoomServicesDebug(RoomState room)
        {
            GUILayout.Label("LOCAL ROOM SERVICE / FIXTURES", heading);
            GUILayout.Label("Radiator setting " + room.RadiatorSetting + "/3 · Lamp " + room.LampCondition.ToString("F1") +
                "%" + (room.LampBroken ? " · burnt out" : " · working"), body);
            NumericRow("Radiator (0–3)", ref radiatorSetting, "Set radiator", value => Session.DebugSetRadiator(room.Profile.Id, Mathf.RoundToInt(value)));
            if (Button("Break selected room lamp")) Apply(() => Session.DebugBreakLamp(room.Profile.Id));
        }

        void DrawServiceDebug()
        {
            var services = Session.Simulation.Services;
            if (services == null) return;
            GUILayout.Space(8);
            GUILayout.Label("GUEST SERVICES / PROMISES", heading);
            GUILayout.Label("Blankets on shelf " + services.BlanketsAvailable + " / spare bulbs " + services.BulbsAvailable +
                " · " + services.Cases.Count(c => c.Active) + " active cases", body);
            NumericRow("Blanket stock (0–6)", ref blanketStock, "Set shelf stock", value => Session.DebugSetBlanketStock(Mathf.RoundToInt(value)));
            if (Button("Advance to next wake-up promise due", Session.Phase == DayPhase.Service && services.Promises.Any(p => p.Status == PromiseStatus.Accepted)))
                Apply(() => { Session.AdvanceToNextPromise(); return CommandResult.Ok("Advanced to the agreed call time. Use the physical reception phone."); });
            foreach (var item in services.Cases)
                GUILayout.Label("SERVICE " + item.Id + " / " + item.Kind + " / " + item.Status + "\nGuest " + item.GuestId + " / room " + item.RoomId +
                    " / " + item.CommunicationState + " / known " + item.IsKnownToHotel +
                    " / created " + item.CreatedAt.ToString("F1") + "s / due " + item.DueTime.ToString("F1") + "s\nSource " + item.SourceEntityId +
                    " / room " + item.SourceRoomId + " / " + item.Description, body);
            foreach (var promise in services.Promises)
                GUILayout.Label("PROMISE " + promise.Id + " / guest " + promise.GuestId + " / room " + promise.RoomId +
                    " / due " + promise.DueTime.ToString("F1") + "s / " + promise.Status, body);
        }

        void DrawGuestServicesDebug(GuestStay guest)
        {
            if (Session.Simulation.Services == null) return;
            GUILayout.Label("SERVICE MEMORY: requested " + guest.Memory.ServicesRequested + " / fulfilled " + guest.Memory.ServicesFulfilled +
                " / declined " + guest.Memory.ServicesDeclined + "\nPromises kept " + guest.Memory.PromisesKept + " / broken " + guest.Memory.PromisesBroken +
                "\nBlanket comfort +" + guest.BlanketComfortBonus.ToString("F1") + "°C / perceived " + guest.Perception.PerceivedTemperature.ToString("F1") + "°C", body);
            if (Button("Set mild cold for selected guest", guest.Agent.InAssignedRoom)) Apply(() => Session.DebugSetMildCold(guest.GuestId));
            DrawNaturalResponseDebug(guest);
            GUILayout.Label("Create service intent (debug; natural mode keeps it private until a conversation):", body);
            foreach (ServiceKind kind in Enum.GetValues(typeof(ServiceKind)))
                if (Button(GuestLabels.Service(kind, Session.Simulation), Session.Phase == DayPhase.Service)) Apply(() => Session.DebugForceService(guest.GuestId, kind));
        }
    }
}
#endif
