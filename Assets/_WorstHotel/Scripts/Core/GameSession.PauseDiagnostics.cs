#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace WorstHotel
{
    public sealed partial class GameSession
    {
        internal float DiagnosticAccumulator => accumulator;
    }
}
#endif
