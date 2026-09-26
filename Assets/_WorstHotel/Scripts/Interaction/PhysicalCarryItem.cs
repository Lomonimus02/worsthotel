using UnityEngine;

namespace WorstHotel
{
    /// <summary>Item-specific ownership hooks around the existing physical grab joint.</summary>
    public class PhysicalCarryItem : MonoBehaviour
    {
        public virtual bool TryBeginCarry(PlayerInteractor player) => player != null;
        public virtual void EndCarry(PlayerInteractor player) { }
    }
}
