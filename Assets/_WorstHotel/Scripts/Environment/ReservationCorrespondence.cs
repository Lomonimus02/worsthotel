using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    // A letter on the actual reservations book. Mirrors derive visibility from host state;
    // it is neither a notification popup nor a second booking interface.
    public sealed class ReservationCorrespondence : MonoBehaviour
    {
        GameObject paper;
        TextMesh label;
        Material paperMaterial;
        void Start()
        {
            paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            paper.name = "Reservation enquiry letter"; paper.transform.SetParent(transform, false);
            paper.transform.localPosition = new Vector3(-.77f, .08f, -.065f);
            paper.transform.localScale = new Vector3(.38f, .25f, .016f);
            // Reuse a material already referenced by the book so player shader stripping
            // cannot remove a shader found only by its runtime string name.
            var page = GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "Cream paper pages");
            paperMaterial = new Material(page ? page.sharedMaterial : paper.GetComponent<Renderer>().sharedMaterial)
                { color = new Color(.93f, .86f, .65f) };
            paper.GetComponent<Renderer>().sharedMaterial = paperMaterial;
            var text = new GameObject("Letter address"); text.transform.SetParent(paper.transform, false);
            text.transform.localPosition = new Vector3(0, 0, -.6f); text.transform.localRotation = Quaternion.identity;
            text.transform.localScale = new Vector3(1 / .38f, 1 / .25f, 1 / .016f);
            label = text.AddComponent<TextMesh>(); label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 36; label.characterSize = .032f * 10 / 36;
            var title = GetComponentsInChildren<TextMesh>().FirstOrDefault(t => t.name == "Printed book title");
            label.color = new Color(.15f, .09f, .04f);
            label.GetComponent<Renderer>().sharedMaterial = title ? title.GetComponent<Renderer>().sharedMaterial : label.font.material;
            label.gameObject.AddComponent<WorldTextFontBinding>().RefreshAtlas();
            paper.SetActive(false);
        }
        void LateUpdate()
        {
            if (!paper) return;
            var enquiry = GameSession.Instance?.Simulation?.SpecialEnquiries.FirstOrDefault(e => e.Status == SpecialOfferStatus.Pending);
            paper.SetActive(enquiry != null);
            if (enquiry != null) label.text = "NEW ENQUIRY\n$" + enquiry.Definition.Payment + " / NIGHT";
        }
        void OnDestroy() { if (paper) Destroy(paper); if (paperMaterial) Destroy(paperMaterial); }
    }
}
