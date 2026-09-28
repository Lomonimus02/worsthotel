using System;
using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        string spokenLine;
        float spokenUntil;

        bool DrawPhysicalConversation()
        {
            if (!guestContext && !wakePhone) return false;
            var staff = LocalCoopBootstrap.Instance.Players[owner];
            var viewport = staff.PlayerCamera.pixelRect;
            viewport.y = Screen.height - viewport.yMax;
            float scale = Mathf.Min(viewport.width / 900f, viewport.height / 900f);
            float width = viewport.width / scale;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(viewport.x, viewport.y, 0), Quaternion.identity, Vector3.one * scale);
            string speaker = "", line = "";
            if (guestContext)
            {
                var guest = Session.Simulation.Guests.FirstOrDefault(g => g.GuestId == selectedServiceGuest);
                if (guest == null) { pending = Close; GUI.matrix = old; return true; }
                speaker = guest.Name + " · Room " + guest.RoomId;
                var cases = GuestConversationSituations(guest).OrderByDescending(s => s.Stage).ToArray();
                var service = CurrentGuestService(guest);
                line = !ContextGuestAvailable(guest) ? "No answer; the guest is out." :
                    guest.Agent.State == GuestAgentState.Sleeping || guest.Agent.Activity == GuestActivity.Shower ? "I need some privacy. Please come back later." :
                    service != null ? GuestLabels.ServiceClue(service, Session.Simulation) : cases.Length > 0 ? GuestLabels.ComplaintClue(cases[0]) :
                    guest.Agent.State == GuestAgentState.WaitingForCheckIn ? "Hello. I have a reservation, under " + guest.Name + "." : "Yes? What is it?";
                if (contextHasResponse) { speaker = "Reception"; line = Session.LastMessage; }
            }
            else
            {
                var response = Session.Simulation.Services.FindResponse(phoneResponseId);
                if (response?.CommunicatedAt >= 0)
                { speaker = GuestName(response.GuestId) + " · Room " + response.RoomId; line = GuestLabels.ResponseClue(Session.Simulation, response); }
                else line = callingPromise != null ? "Calling the room…" : phoneResponseId != null ? "Hello, reception." : "Dial a promised wake-up call, or replace the handset.";
                if (serviceHasResponse) { speaker = "Reception"; line = Session.LastMessage; }
            }
            string identity = speaker + "\n" + line;
            if (identity != spokenLine)
            { spokenLine = identity; spokenUntil = Time.unscaledTime + Mathf.Clamp(line.Length * .075f, 6, 15); }
            int count = guestContext ? contextChoices.Count : serviceChoices.Count;
            var highlight = new Color(1f, .82f, .42f);
            float bottom = viewport.height / scale - 28;
            float choicesTop = bottom - count * 38;
            if (HotelAccessibility.Subtitles && Time.unscaledTime < spokenUntil)
            {
                var rect = new Rect(width / 2 - 330, choicesTop - 109, 660, 99);
                Fill(rect, new Color(.035f, .045f, .04f, .82f));
                Label(new Rect(rect.x + 16, rect.y + 7, 628, 27), speaker, Small, highlight);
                Label(new Rect(rect.x + 16, rect.y + 35, 628, 61), line, Body, LightPaper);
            }
            actions.Clear(); enabledActions.Clear();
            for (int i = 0; i < count; i++)
            {
                var title = guestContext ? contextChoices[i].title : serviceChoices[i].title;
                Action action = guestContext ? contextChoices[i].action : serviceChoices[i].action;
                bool enabled = guestContext || serviceChoices[i].enabled;
                int index = actions.Count; actions.Add(action); enabledActions.Add(enabled);
                var rect = new Rect(width / 2 - 285, choicesTop + i * 38, 570, 32);
                Fill(rect, new Color(.035f, .045f, .04f, .65f));
                Label(rect, (index == focus ? "› " : "") + title, Small, enabled ? (index == focus ? highlight : LightPaper) : new Color(.7f, .7f, .65f));
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && enabled && staff.Input.IsMouseLook)
                { focus = index; pending = action; }
            }
            GUI.matrix = old;
            return true;
        }
    }
}
