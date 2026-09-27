using System.Collections.Generic;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void AddRepairControls(GameObject gameplay)
        {
            var controller = gameplay.AddComponent<RepairSequenceController>();
            var controls = new List<RepairControl>();
            var panel = GameObject.Find("PanelAnchor").transform;
            var hinge = Group("Panel hinge", panel, new Vector3(-.715f, 0, -.015f)).transform;
            foreach (string name in new[] { "Panel door", "Panel grip", "Panel label" })
            {
                var part = panel.Find(name); if (part) part.SetParent(hinge, true);
            }
            Add("ValveAnchor", RepairControlKind.ReliefValve, null);
            Add("PanelAnchor", RepairControlKind.Panel, hinge);
            Add("BreakerAnchor", RepairControlKind.Breaker, GameObject.Find("BreakerAnchor").transform.Find("Breaker handle"));
            Add("LatchAAnchor", RepairControlKind.LatchA, GameObject.Find("LatchAAnchor").transform.Find("Latch handle"));
            Add("LatchBAnchor", RepairControlKind.LatchB, GameObject.Find("LatchBAnchor").transform.Find("Latch handle"));
            Add("RestartAnchor", RepairControlKind.Restart, GameObject.Find("RestartAnchor").transform.Find("Restart button"));
            controller.controls = controls.ToArray();

            var gauge = GameObject.Find("GaugeAnchor").transform;
            var config = EnsureConfiguration().boiler;
            for (float pressure = config.repairSafeMin; pressure <= config.repairSafeMax; pressure += 2)
            {
                float angle = (210 - pressure / config.maxPressure * 240) * Mathf.Deg2Rad;
                var tick = Box("Safe repair band", gauge, new Vector3(Mathf.Cos(angle) * .385f, Mathf.Sin(angle) * .385f, -.160f), new Vector3(.025f, .105f, .012f), "Signal green", false, false);
                tick.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg - 90);
            }

            void Add(string name, RepairControlKind kind, Transform moving)
            {
                var anchor = GameObject.Find(name);
                var control = anchor.AddComponent<RepairControl>();
                control.controller = controller; control.kind = kind;
                if (kind == RepairControlKind.Breaker) control.displayName = "BOILER ISOLATION";
                control.movingPart = moving ? moving : anchor.transform;
                controls.Add(control);
            }
        }
    }
}
