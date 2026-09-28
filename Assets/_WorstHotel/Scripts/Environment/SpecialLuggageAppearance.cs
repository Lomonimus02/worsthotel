using UnityEngine;

namespace WorstHotel
{
    /// <summary>All meshes are authored once and remain in the host's physical replication registry.</summary>
    public sealed class SpecialLuggageAppearance : MonoBehaviour
    {
        public Transform suitcase, instrumentCase, amplifier;
        LuggagePayload? shaped;
        Renderer[] suitcaseMeshes, caseMeshes, amplifierMeshes;
        void Awake()
        {
            suitcaseMeshes = suitcase.GetComponentsInChildren<Renderer>(true);
            caseMeshes = instrumentCase.GetComponentsInChildren<Renderer>(true);
            amplifierMeshes = amplifier.GetComponentsInChildren<Renderer>(true);
        }
        public void Show(LuggagePayload payload, bool visible)
        {
            Set(suitcaseMeshes, visible && payload == LuggagePayload.Suitcase);
            Set(caseMeshes, visible && payload == LuggagePayload.InstrumentCase);
            Set(amplifierMeshes, visible && payload == LuggagePayload.Amplifier);
        }
        static void Set(Renderer[] meshes, bool enabled)
        { if (meshes != null) foreach (var mesh in meshes) if (mesh) mesh.enabled = enabled; }

        public void ConfigureShape(ServiceItemState state)
        {
            if (state == null || shaped == state.Payload) return;
            shaped = state.Payload;
            GetComponent<BoxCollider>().size = state.Payload == LuggagePayload.InstrumentCase ? new Vector3(1.34f, .65f, .35f) :
                state.Payload == LuggagePayload.Amplifier ? new Vector3(.78f, .78f, .45f) : new Vector3(.71f, .68f, .38f);
            var body = GetComponent<Rigidbody>();
            body.ResetCenterOfMass(); body.ResetInertiaTensor();
        }
    }
}
