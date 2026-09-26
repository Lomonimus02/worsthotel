using System.Linq;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void AddLivingFeedback(GameObject gameplay, HotelFeedback feedback)
        {
            feedback.receptionBellAnchor = GameObject.Find("Service bell").transform;
            var servicePhone = gameplay.GetComponentInChildren<ReceptionPhoneInteraction>();
            var phone = servicePhone ? servicePhone.gameObject : Group("Reception complaint phone", gameplay.transform, new Vector3(-3.65f, 1.42f, 2.55f));
            feedback.receptionPhoneAnchor = phone.transform;
            Box("Phone enamel base", phone.transform, new Vector3(0, .06f, 0), new Vector3(.66f, .13f, .44f), "Boiler enamel", true, false);
            Cylinder("Phone brass dial", phone.transform, new Vector3(0, .142f, -.035f), .12f, .025f, "Aged brass");
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Sphere("Dial finger opening", phone.transform, new Vector3(Mathf.Cos(angle) * .085f, .16f, -.035f + Mathf.Sin(angle) * .085f),
                    new Vector3(.033f, .008f, .033f), "Ink");
            }
            feedback.phoneReceiver = Group("Ringing handset", phone.transform, new Vector3(0, .27f, .12f)).transform;
            Box("Handset bridge", feedback.phoneReceiver, Vector3.zero, new Vector3(.49f, .09f, .095f), "Ink", true, false);
            foreach (int side in new[] { -1, 1 })
                Sphere("Handset ear piece", feedback.phoneReceiver, new Vector3(side * .24f, -.02f, 0), new Vector3(.18f, .13f, .19f), "Ink");
            Pipe("Curled receiver lead", phone.transform, new Vector3(.28f, .18f, .1f), new Vector3(.38f, .065f, -.02f), .024f, "Ink");
            feedback.phoneLens = Sphere("Complaint lamp", phone.transform, new Vector3(-.26f, .143f, -.12f), Vector3.one * .052f, "Warm lamp").GetComponent<Renderer>();
            foreach (var child in phone.GetComponentsInChildren<Transform>()) child.gameObject.isStatic = false;

            for (int i = 0; i < 6; i++)
            {
                var radiatorObject = GameObject.Find("Radiator" + (101 + i));
                var view = radiatorObject.AddComponent<RadiatorHeatFeedback>(); view.roomId = 101 + i;
                view.fins = radiatorObject.GetComponentsInChildren<Renderer>().Where(renderer =>
                    renderer.gameObject.name == "Radiator fin" || renderer.gameObject.name.Contains("manifold")).ToArray();
            }
            foreach (var heater in gameplay.GetComponentsInChildren<PortableHeater>()) heater.gameObject.AddComponent<PortableHeaterAudio>();
        }
    }
}
