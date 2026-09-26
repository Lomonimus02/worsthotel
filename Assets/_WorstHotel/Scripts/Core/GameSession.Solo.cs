namespace WorstHotel
{
    public sealed partial class GameSession
    {
        public bool IsSolo => LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.IsSolo && !IsLanReplica;
        public SoloAssistSettings SoloAssist => config && config.soloAssist ? config.soloAssist.ToData() : new SoloAssistSettings();

        public void RefreshSoloConfiguration()
        {
            if (Simulation == null || IsLanReplica) return;
            Simulation.Boiler.ConfigureSoloAssist(IsSolo ? SoloAssist : null);
            Simulation.Services?.SetStaffCount(IsSolo ? 1 : 2);
            if (Wait) Wait.Stop(IsSolo ? "Hold WAIT to advance to the next hotel event." : "Both staff hold WAIT to advance to the next hotel event.");
        }
    }
}
