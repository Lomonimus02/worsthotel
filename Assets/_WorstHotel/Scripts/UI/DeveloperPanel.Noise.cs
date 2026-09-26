#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        void DrawRoomNoiseDebug(RoomState room)
        {
            var simulation = Session.Simulation;
            if (simulation.Noise == null) return;
            GUILayout.Label("Noise: own source " + room.SourceNoise.ToString("F3") + " / received " + room.ReceivedNoise.ToString("F3") +
                " / ambient " + room.Profile.Noise.ToString("F3") + " / evaluated " + room.Noise.ToString("F3") +
                (simulation.Noise.GetNoiseOverride(room.Profile.Id).HasValue ? " [OVERRIDE]" : ""), body);
            foreach (var source in simulation.Noise.Sources.Where(s => s.SourceRoomId == room.Profile.Id))
                GUILayout.Label("SOURCE " + source.SourceEntityId + " / " + source.Label + " " + source.NoiseOutput.ToString("F3"), body);
            foreach (var source in simulation.Noise.GetContributions(room.Profile.Id))
                GUILayout.Label("RECEIVES " + source.ReceivedNoise.ToString("F3") + " from room " + source.SourceRoomId + " / " + source.Label, body);
            bool show = GUILayout.Toggle(NoisePropagationDebug.Visible, "Show noise propagation in the world (developer only)");
            if (show != NoisePropagationDebug.Visible)
            {
                NoisePropagationDebug.Visible = show;
                if (show && !GetComponent<NoisePropagationDebug>()) gameObject.AddComponent<NoisePropagationDebug>();
            }
            foreach (var link in simulation.Noise.Graph.Links.Where(l => l.RoomA == room.Profile.Id || l.RoomB == room.Profile.Id))
                GUILayout.Label(link.Kind + " → " + (link.RoomA == room.Profile.Id ? link.RoomB : link.RoomA), body);
            GUILayout.Label("A diagnostic noise override is not a source and cannot create a guest complaint.", body);
            if (Button("Clear selected room noise override")) Apply(() => simulation.ClearRoomNoiseOverride(room.Profile.Id));
        }
    }
}
#endif
