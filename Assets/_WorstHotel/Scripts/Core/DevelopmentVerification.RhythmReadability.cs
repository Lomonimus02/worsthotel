#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        IEnumerator CaptureRhythmReadability()
        {
            ManagementUI.Instance.Close();
            var panel = FindAnyObjectByType<DeveloperPanel>();
            Require(panel && !panel.IsVisible, "production F2 starts closed for readability capture");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scrollField = typeof(DeveloperPanel).GetField("scroll", flags);
            var historyField = typeof(DeveloperPanel).GetField("showHistory", flags);
            var thermalTop = typeof(DeveloperPanel).GetField("thermalDebugTop", flags);
            var historyTop = typeof(DeveloperPanel).GetField("historyDebugTop", flags);
            Require(scrollField != null && historyField != null && thermalTop != null && historyTop != null,
                "read-only real-layout markers for F2 capture");
            scrollField.SetValue(panel, Vector2.zero); historyField.SetValue(panel, true);
            panel.SendMessage("Toggle");
            try
            {
                yield return CaptureOperationsPage("17-debug-boiler", "Actual F2 / measured boiler condition, demand, stress and paid service controls");
                float thermalY = (float)thermalTop.GetValue(panel);
                Require(thermalY > 0, "actual IMGUI layout measured thermal section");
                scrollField.SetValue(panel, new Vector2(0, Mathf.Max(0, thermalY - 8)));
                yield return CaptureOperationsPage("18-debug-thermal", "Actual F2 / single real thermal calculation / no separate display model");
                float historyY = (float)historyTop.GetValue(panel);
                Require(historyY > thermalY && session.Simulation.InfrastructureHistory.Count > 0,
                    "actual causal entries and history layout exist");
                scrollField.SetValue(panel, new Vector2(0, Mathf.Max(0, historyY - 8)));
                yield return CaptureOperationsPage("19-debug-history", "Actual F2 / bounded infrastructure history / diagnostic events labelled");
                facts.Add("ThermalAndHistoryCaptured=True ActualIMGUI=True CaptureOnlyScrollChanges=True");
            }
            finally
            {
                scrollField.SetValue(panel, Vector2.zero); historyField.SetValue(panel, false);
                if (panel.IsVisible) panel.SendMessage("Toggle");
            }
        }
    }
}
#endif
