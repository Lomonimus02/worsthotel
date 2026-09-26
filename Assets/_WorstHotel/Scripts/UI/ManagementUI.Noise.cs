using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        string RoomNoiseCause(RoomState room)
        {
            var simulation = Session.Simulation;
            if (simulation.Noise == null) return "Quiet is judged from conditions in the assigned room.";
            if (simulation.Noise.GetNoiseOverride(room.Profile.Id).HasValue) return "Developer noise override is active for this room.";
            string source = "";
            float loudest = 0;
            foreach (var link in simulation.Noise.Graph.Links)
            {
                int other = link.RoomA == room.Profile.Id ? link.RoomB : link.RoomB == room.Profile.Id ? link.RoomA : 0;
                if (other == 0) continue;
                var neighbor = Session.Rooms.First(r => r.Profile.Id == other);
                float contribution = neighbor.SourceNoise * (link.Kind == RoomNoiseLinkKind.SharedWall ?
                    simulation.NoiseSettings.SharedWallTransmission : simulation.NoiseSettings.CorridorTransmission);
                if (contribution <= loudest) continue;
                loudest = contribution;
                source = link.Kind == RoomNoiseLinkKind.SharedWall ? "Loud activity through a shared wall. Check next door." :
                    "Loud activity carries through the corridor. Listen near the nearby rooms.";
            }
            return loudest > .06f ? source : "No prominent neighbouring activity is audible here.";
        }

        string PlanningNoiseLabel(RoomState room)
        {
            if (Session.Simulation.Noise == null) return room.Noise > .35f ? "Background noise" : "Quiet tendency";
            var booking = Session.Plan.Assignments.FirstOrDefault(a => a.RoomId == room.Profile.Id);
            var guest = Session.Plan.Applications.FirstOrDefault(a => a.Id == (booking != null ? booking.BookingId : selectedBooking));
            float peak = room.Profile.Noise;
            foreach (var link in Session.Simulation.Noise.Graph.Links)
            {
                int other = link.RoomA == room.Profile.Id ? link.RoomB : link.RoomB == room.Profile.Id ? link.RoomA : 0;
                if (other == 0) continue;
                var assigned = Session.Plan.Assignments.FirstOrDefault(a => a.RoomId == other);
                var neighbor = assigned != null ? Session.Plan.Applications.FirstOrDefault(a => a.Id == assigned.BookingId) : null;
                if (neighbor == null || (neighbor.Archetype.Traits & GuestTraits.Noisy) == 0) continue;
                peak += Session.Simulation.LivingSettings.LoudNoiseOutput * (link.Kind == RoomNoiseLinkKind.SharedWall ?
                    Session.Simulation.NoiseSettings.SharedWallTransmission : Session.Simulation.NoiseSettings.CorridorTransmission);
            }
            if (guest != null && Session.Simulation.NeedEvaluator.NoiseSeverity(peak, guest.Archetype.Needs) >= .25f && peak > room.Profile.Noise)
                return "Noisy neighbour risk";
            return room.Profile.Noise > .35f ? "Background noise" : "Quiet tendency";
        }

        void DrawPlanningNeighbours()
        {
            if (Session.Simulation.Noise == null) return;
            var links = Session.Simulation.Noise.Graph.Links.Where(l => l.RoomA == selectedRoom || l.RoomB == selectedRoom).ToArray();
            string wall = string.Join(", ", links.Where(l => l.Kind == RoomNoiseLinkKind.SharedWall).Select(l => (l.RoomA == selectedRoom ? l.RoomB : l.RoomA).ToString()));
            string corridor = string.Join(", ", links.Where(l => l.Kind == RoomNoiseLinkKind.Corridor).Select(l => (l.RoomA == selectedRoom ? l.RoomB : l.RoomA).ToString()));
            Label(new Rect(450, 674, 655, 44), "ROOM " + selectedRoom + "  /  Shared wall: " + (wall.Length > 0 ? wall : "none") +
                "\nAcross corridor / nearby doors: " + (corridor.Length > 0 ? corridor : "none"), Small, Muted);
        }
    }
}
