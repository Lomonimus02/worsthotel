using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public enum HotelVisitorState { Arriving, WaitingAtReception, GoingToRoom, Visiting, Leaving, Left }
    [Serializable] public sealed class HotelVisitor
    {
        public string Id, HostGuestId;
        public int RoomId;
        public HotelVisitorState State;
        public float CreatedAt, StateSince, LeaveAt;
        public bool Allowed;
        public HotelVisitor Copy() => (HotelVisitor)MemberwiseClone();
    }
    public sealed partial class HotelDirector
    {
        readonly List<HotelVisitor> visitors = new List<HotelVisitor>();
        public IReadOnlyList<HotelVisitor> Visitors => visitors.AsReadOnly();
        public HotelVisitor FindVisitor(string id) => visitors.FirstOrDefault(v => v.Id == id);
        internal HotelVisitor InviteVisitor(GuestStay host)
        {
            var visitor = new HotelVisitor { Id = "visitor/" + host.GuestId, HostGuestId = host.GuestId, RoomId = host.RoomId,
                CreatedAt = hotel.Elapsed, StateSince = hotel.Elapsed, LeaveAt = Math.Min(hotel.Elapsed + Settings.VisitorStaySeconds + 35,
                    Math.Min(host.Agent.Schedule.SleepTime, host.Agent.CheckoutTime) - 5) };
            visitors.Add(visitor); return visitor;
        }
        void TickVisitors()
        {
            visitors.RemoveAll(v => v.State == HotelVisitorState.Left && hotel.Elapsed - v.StateSince > 30);
            foreach (var v in visitors)
            {
                var host = hotel.Guests.FirstOrDefault(g => g.GuestId == v.HostGuestId && !g.ReceiptPosted);
                if (v.State == HotelVisitorState.Left || v.State == HotelVisitorState.Leaving) continue;
                if (host == null || host.RoomId != v.RoomId || host.Agent.State == GuestAgentState.Sleeping || !host.Agent.CheckedIn ||
                    host.Agent.State == GuestAgentState.CheckingOut || host.Agent.State == GuestAgentState.Leaving || host.Agent.State == GuestAgentState.Left || hotel.Elapsed >= v.LeaveAt)
                { TransitionVisitor(v, HotelVisitorState.Leaving); continue; }
                // Ignoring the arrival is possible: the visitor eventually goes upstairs,
                // remaining a discoverable physical person who can still be asked to leave.
                if (v.State == HotelVisitorState.WaitingAtReception && hotel.Elapsed - v.StateSince >= Settings.VisitorWaitSeconds)
                    TransitionVisitor(v, HotelVisitorState.GoingToRoom);
            }
        }
        void TransitionVisitor(HotelVisitor v, HotelVisitorState state) { v.State = state; v.StateSince = hotel.Elapsed; }
        public CommandResult DecideVisitor(int actorId, string id, bool allow)
        {
            if (hotel.IsReadOnlyMirror || !hotel.Running || actorId < 0 || actorId > 1) return CommandResult.Fail("Only hotel staff can speak to this visitor.");
            var v = FindVisitor(id);
            if (v == null || v.State == HotelVisitorState.Left || v.State == HotelVisitorState.Leaving) return CommandResult.Fail("The visitor has already left.");
            v.Allowed = allow;
            if (!allow) TransitionVisitor(v, HotelVisitorState.Leaving);
            else if (v.State == HotelVisitorState.WaitingAtReception || v.State == HotelVisitorState.Arriving) TransitionVisitor(v, HotelVisitorState.GoingToRoom);
            return CommandResult.Ok(allow ? "Thank you. I'll visit my friend in room " + v.RoomId + " for a little while." : "All right. I'll head out.");
        }
        public CommandResult SignalVisitorReached(string id, HotelVisitorState expected)
        {
            if (hotel.IsReadOnlyMirror || !hotel.Running) return CommandResult.Fail("Only the host can report visitor travel.");
            var v = FindVisitor(id);
            if (v == null || v.State != expected) return CommandResult.Fail("Visitor route changed.");
            if (expected == HotelVisitorState.Arriving) TransitionVisitor(v, HotelVisitorState.WaitingAtReception);
            else if (expected == HotelVisitorState.GoingToRoom) TransitionVisitor(v, HotelVisitorState.Visiting);
            else if (expected == HotelVisitorState.Leaving) TransitionVisitor(v, HotelVisitorState.Left);
            else return CommandResult.Fail("No visitor journey to complete.");
            return CommandResult.Ok();
        }
        public bool VisitorUsingRoom(HotelVisitor v) => v.State == HotelVisitorState.Visiting && hotel.Guests.Any(g =>
            g.GuestId == v.HostGuestId && g.RoomId == v.RoomId && !g.ReceiptPosted && g.Agent.InAssignedRoom && g.Agent.State != GuestAgentState.Sleeping);
        public float VisitorHeatFor(int roomId) => visitors.Count(v => v.RoomId == roomId && VisitorUsingRoom(v)) * Settings.VisitorHeat;
    }
}
