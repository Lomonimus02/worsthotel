using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        // Validate before ApplySnapshot: rejecting an outer controller view must not consume
        // the model sequence, replace hotel state or close an already open local journal.
        bool ValidLanSleepFrame(LanHotelFrame frame, HotelSimulation target, bool fresh)
        {
            var sleep = frame.sleep;
            if (sleep == null || !Enum.IsDefined(typeof(HotelAdvanceMode), sleep.mode) ||
                !Enum.IsDefined(typeof(StaffWakeReason), sleep.wakeReason) || sleep.revision < 0 ||
                !Number.IsFinite(sleep.until) || sleep.until < 0 ||
                sleep.bedIds == null || sleep.bedIds.Length != 2 || sleep.ready == null || sleep.ready.Length != 2)
                return false;
            for (int actor = 0; actor < 2; actor++)
                if (sleep.ready[actor] ? sleep.bedIds[actor] < 0 || sleep.bedIds[actor] > 1 : sleep.bedIds[actor] != -1)
                    return false;
            bool any = sleep.ready[0] || sleep.ready[1], both = sleep.ready[0] && sleep.ready[1];
            if (both && sleep.bedIds[0] == sleep.bedIds[1]) return false;
            if (any)
            {
                if (!frame.model.HasOperations || target.Calendar == null || frame.phase != DayPhase.Service ||
                    !frame.model.Running || frame.hostPaused || sleep.revision == 0 || sleep.wakeReason != StaffWakeReason.None ||
                    sleep.mode != (both ? HotelAdvanceMode.Sleep : HotelAdvanceMode.None) ||
                    frame.model.Speed != (both ? WaitController.SleepSpeed : 1) ||
                    frame.waitVotes.Any(value => value) || frame.waitProgress.Any(value => value != 0) ||
                    sleep.until <= frame.model.Time) return false;
                try
                {
                    float expected = WaitController.NextMorningAt(target.Calendar, frame.model.Time);
                    if (sleep.until != expected) return false;
                }
                catch (ArgumentException) { return false; }
                catch (OverflowException) { return false; }
            }
            else if (sleep.until != 0 || sleep.mode == HotelAdvanceMode.Sleep) return false;
            if (sleep.mode == HotelAdvanceMode.Wait &&
                (sleep.revision == 0 || sleep.wakeReason != StaffWakeReason.None || frame.hostPaused ||
                 frame.model.Speed <= 1 || !frame.waitVotes[0] || !frame.waitVotes[1])) return false;
            if (!fresh && Wait)
            {
                var current = Wait.CaptureSleepView();
                if (sleep.revision < current.Revision) return false;
                if (sleep.revision == current.Revision &&
                    (sleep.mode != current.Mode || sleep.until != current.Until || sleep.wakeReason != current.WakeReason ||
                     sleep.bedIds[0] != current.Staff0.BedId || sleep.bedIds[1] != current.Staff1.BedId ||
                     sleep.ready[0] != current.Staff0.Ready || sleep.ready[1] != current.Staff1.Ready)) return false;
            }
            return true;
        }
    }
}
