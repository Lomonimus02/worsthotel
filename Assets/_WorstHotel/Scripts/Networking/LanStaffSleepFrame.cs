using System;

namespace WorstHotel
{
    /// <summary>Bounded presentation of host-owned physical bed consent; never an action request.</summary>
    [Serializable]
    public sealed class LanStaffSleepFrame
    {
        public HotelAdvanceMode mode;
        public StaffWakeReason wakeReason;
        public long revision;
        public float until;
        public int[] bedIds = { -1, -1 };
        public bool[] ready = new bool[2];

        public static LanStaffSleepFrame FromView(StaffSleepView view) => new LanStaffSleepFrame
        {
            mode = view.Mode, revision = view.Revision, until = view.Until, wakeReason = view.WakeReason,
            bedIds = new[] { view.Staff0.BedId, view.Staff1.BedId },
            ready = new[] { view.Staff0.Ready, view.Staff1.Ready }
        };

        public StaffSleepView ToView() => new StaffSleepView(mode, revision, until, wakeReason,
            new StaffSleepConsent(0, bedIds[0], ready[0]), new StaffSleepConsent(1, bedIds[1], ready[1]));
    }
}
