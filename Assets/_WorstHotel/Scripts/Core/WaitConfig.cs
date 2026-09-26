using UnityEngine;

namespace WorstHotel
{
    public readonly struct WaitSettings
    {
        public float Speed { get; }
        public float HoldSeconds { get; }
        public WaitSettings(float speed, float holdSeconds)
        {
            Speed = speed == 4 || speed == 8 ? speed : 8;
            HoldSeconds = Number.IsFinite(holdSeconds) ? Mathf.Clamp(holdSeconds, 0.2f, 5) : 1;
        }
    }

    [CreateAssetMenu(menuName = "Worst Hotel/Wait configuration")]
    public sealed class WaitConfig : ScriptableObject
    {
        [Tooltip("Hotel simulation speed only. Supported wait speeds are 4 or 8.")]
        public float waitSpeed = 8;
        [Range(0.2f, 5)] public float holdSeconds = 1;
        public WaitSettings ToSettings() => new WaitSettings(waitSpeed, holdSeconds);
        public static WaitSettings Resolve(WaitConfig config) => config ? config.ToSettings() : new WaitSettings(8, 1);
    }
}
