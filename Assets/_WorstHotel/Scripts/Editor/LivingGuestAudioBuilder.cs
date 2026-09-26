using UnityEngine;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>Call after the authored guest presentation and room markers have been built.</summary>
        public static void AddLivingGuestAudio(GameObject gameplay)
        {
            var audio = gameplay.GetComponent<LivingGuestAudio>();
            if (audio == null) audio = gameplay.AddComponent<LivingGuestAudio>();
            audio.presentation = gameplay.GetComponent<GuestPresentation>();
        }
    }
}
