using UnityEngine;

namespace WorstHotel
{
    /// <summary>Physical interaction surface. Commands retain the originating gameplay player identity.</summary>
    public class HotelInteractable : MonoBehaviour
    {
        public string displayName = "Hotel fixture";
        [TextArea] public string instruction = "Use";

        public virtual string GetPrompt(PlayerInteractor actor) => instruction;
        public virtual bool CanInteract(PlayerInteractor actor) => isActiveAndEnabled;
        public virtual bool AllowsHeldItem(PlayerInteractor actor) => false;
        public virtual void Interact(PlayerInteractor actor) { }
        public virtual void HoldInteract(PlayerInteractor actor, float deltaTime) { }
        public virtual void EndInteract(PlayerInteractor actor) { }
        public virtual void SecondaryInteract(PlayerInteractor actor) { }
    }
}
