using UnityEngine;

namespace WorstHotel
{
    [DefaultExecutionOrder(1200)]
    public sealed class BookCameraFocus : MonoBehaviour
    {
        Camera cameraView;
        Transform book;
        Vector3 homePosition, returnPosition;
        Quaternion homeRotation, returnRotation;
        float homeFov, started, returnFov;
        bool returning;
        public bool Settled => book && Time.unscaledTime - started > .3f;

        public void Focus(Transform target)
        {
            cameraView = GetComponent<Camera>();
            if (!book && !returning)
            { homePosition = transform.localPosition; homeRotation = transform.localRotation; homeFov = cameraView.fieldOfView; }
            book = target; returning = false; started = Time.unscaledTime;
        }
        public void Release()
        {
            if (!book) return;
            book = null; returning = true; started = Time.unscaledTime;
            returnPosition = transform.localPosition; returnRotation = transform.localRotation; returnFov = cameraView.fieldOfView;
        }
        void LateUpdate()
        {
            if (book)
            {
                float factor = 1 - Mathf.Exp(-16 * Time.unscaledDeltaTime);
                // Keep the complete spread inside a narrow local co-op viewport too.
                float distance = Mathf.Max(1.17f, .64f / (Mathf.Tan(22.5f * Mathf.Deg2Rad) * Mathf.Max(.3f, cameraView.aspect)));
                transform.position = Vector3.Lerp(transform.position, book.TransformPoint(new Vector3(0, 0, -distance)), factor);
                transform.rotation = Quaternion.Slerp(transform.rotation, book.rotation, factor);
                cameraView.fieldOfView = Mathf.Lerp(cameraView.fieldOfView, 45, factor);
            }
            else if (returning)
            {
                float t = Mathf.SmoothStep(0, 1, (Time.unscaledTime - started) / .25f);
                transform.localPosition = Vector3.Lerp(returnPosition, homePosition, t);
                transform.localRotation = Quaternion.Slerp(returnRotation, homeRotation, t);
                cameraView.fieldOfView = Mathf.Lerp(returnFov, homeFov, t);
                if (t >= 1) returning = false;
            }
        }
        void OnDisable()
        {
            if (!book && !returning) return;
            transform.localPosition = homePosition; transform.localRotation = homeRotation;
            if (cameraView) cameraView.fieldOfView = homeFov;
            book = null; returning = false;
        }
    }
}
