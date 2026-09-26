using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ShiftHUD : MonoBehaviour
    {
        RepairSequenceController repair;
        Transform repairLocation;
        void Start()
        {
            repair = GetComponent<RepairSequenceController>();
            var anchor = GameObject.Find("PanelAnchor"); if (anchor) repairLocation = anchor.transform;
        }
        void OnGUI()
        {
            var session = GameSession.Instance;
            if (!session || session.Phase != DayPhase.Service) return;
            Ensure();
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
            for (int p = 0; p < 2; p++)
            {
                var staff = LocalCoopBootstrap.Instance;
                if (staff && !staff.IsLocalActor(p)) continue;
                if (ManagementUI.Instance && ManagementUI.Instance.IsOpen && ManagementUI.Instance.Owner == p) continue;
                float x = 20 + (staff && (staff.IsSolo || staff.LanRole != LanRole.Offline) ? 0 : p * 800);
                Fill(new Rect(x, 58, 758, 91), new Color(.10f, .14f, .12f, .9f));
                int remaining = Mathf.CeilToInt(session.Simulation.Remaining);
                Label(new Rect(x + 14, 66, 730, 27), "DAY " + session.Day + "   •   " + remaining / 60 + ":" + (remaining % 60).ToString("D2") + " left   •   Cash $" + session.Cash.ToString("F0") + "   •   Complaints " + session.Simulation.Requests.ActiveCount, Body, LightPaper);
                var boiler = session.Simulation.Boiler;
                Label(new Rect(x + 14, 94, 730, 25), "Boiler " + boiler.Condition.ToString("F0") + "%   /   load " + boiler.Load.ToString("F2") + "   /   pressure " + boiler.Pressure.ToString("F0") + "   /   heat " + (boiler.HeatingOutput * 100).ToString("F0") + "%", Small, boiler.Failed ? new Color(1, .56f, .28f) : Paper);
                DrawElectricalStatus(session, x);
                if (session.Simulation.LivingEnabled)
                {
                    int reception = session.Simulation.Guests.Count(g => g.Agent.State == GuestAgentState.WaitingForCheckIn);
                    int arriving = session.Simulation.Guests.Count(g => g.Agent.State == GuestAgentState.Arriving);
                    int staying = session.Simulation.Guests.Count(g => g.Agent.CheckedIn && g.Agent.State != GuestAgentState.Left && g.Agent.State != GuestAgentState.Leaving);
                    Fill(new Rect(x, 152, 758, 25), new Color(.10f, .14f, .12f, .9f));
                    Label(new Rect(x + 14, 152, 730, 24), "ARRIVING " + arriving + "   /   AT RECEPTION " + reception + "   /   CHECKED IN " + staying, Small, reception > 0 ? new Color(1, .8f, .4f) : Paper);
                }
                DrawSituations(session, x, boiler.Failed ? 254 : 185);
                DrawServices(session, x);
                if (boiler.Failed)
                {
                    Fill(new Rect(x + 88, 183, 578, 63), Wine);
                    Label(new Rect(x + 101, 191, 552, 48), "BOILER FAILED  •  CENTRAL HEAT LOST\n" +
                        (session.Simulation.BoilerFailureAcknowledged ? "Consequences accepted; rooms can keep cooling" : "Rooms may cool; guest comfort is at risk"), Center, LightPaper);
                    var coop = LocalCoopBootstrap.Instance;
                    if (repair && repairLocation && coop && coop.Players[p] && Vector3.Distance(coop.Players[p].transform.position, repairLocation.position) < 7)
                    {
                        Fill(new Rect(x + 36, 640, 684, 89), new Color(.12f, .19f, .16f, .94f));
                        Label(new Rect(x + 49, 650, 658, 28), "REPAIR BAND " + session.BoilerSettings.RepairSafeMin + "–" + session.BoilerSettings.RepairSafeMax + "   /   NOW " + boiler.Pressure.ToString("F0"), Body, boiler.InRepairBand ? new Color(.7f, 1, .6f) : new Color(1, .75f, .4f));
                        Label(new Rect(x + 49, 682, 658, 42), session.IsLanReplica && LanSession.Instance ?
                            LanSession.Instance.RemoteRepairStatus : repair.Status, Small, LightPaper);
                    }
                }
            }
            GUI.matrix = matrix;
        }
    }
}

