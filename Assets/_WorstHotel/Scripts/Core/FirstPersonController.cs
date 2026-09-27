using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WorstHotel
{
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-200)]
    public sealed class FirstPersonController : MonoBehaviour
    {
        public int ActorId { get; private set; }
        public Camera PlayerCamera { get; private set; }
        public PlayerInteractor Interactor { get; private set; }
        public LocalPlayerInput Input { get; } = new LocalPlayerInput();
        public bool DeviceReady => Input.DeviceReady;
        public bool IsUIBlocked { get; private set; }
        public CharacterController BodyCollider => body;
        public bool HasWorldAuthority { get; private set; } = true;
        public bool IsLocallyControlled { get; private set; } = true;
        public Vector3 PresentationVelocity => HasWorldAuthority && body ? body.velocity : replicaVelocity;
        public bool PresentationGrounded => HasWorldAuthority && body ? body.isGrounded : true;
        [Range(1, 6)] public float walkSpeed = 3.5f;
        [Range(0.01f, 0.5f)] public float mouseSensitivity = 0.085f;
        [Range(50, 260)] public float controllerLookSpeed = 130;

        private CharacterController body;
        private float pitch;
        private float verticalSpeed;
        private float stride;
        private Transform leftArm, rightArm, leftLeg, rightLeg, head;
        private Vector3 headRest;
        Vector3 replicaVelocity;
        private readonly Dictionary<Rigidbody, Vector3> pushes = new Dictionary<Rigidbody, Vector3>();
        private readonly List<Material> materials = new List<Material>();

        public void Initialize(int actorId)
        {
            ActorId = actorId;
            body = GetComponent<CharacterController>();
            body.height = 1.78f;
            body.radius = 0.3f;
            body.center = new Vector3(0, 0.89f, 0);
            body.stepOffset = 0.28f;
            body.slopeLimit = 48;
            body.skinWidth = 0.035f;
            var cameraObject = new GameObject("Staff " + (actorId + 1) + " first person camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.61f, 0.035f);
            PlayerCamera = cameraObject.AddComponent<Camera>();
            PlayerCamera.rect = new Rect(actorId * 0.5f, 0, 0.5f, 1);
            PlayerCamera.fieldOfView = 76;
            PlayerCamera.nearClipPlane = 0.065f;
            PlayerCamera.farClipPlane = 120;
            PlayerCamera.depth = actorId;
            PlayerCamera.backgroundColor = new Color(0.16f, 0.22f, 0.29f);
            PlayerCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            PlayerCamera.cullingMask = ~(1 << (8 + actorId));
            if (actorId == 0) cameraObject.AddComponent<AudioListener>();
            Interactor = gameObject.AddComponent<PlayerInteractor>();
            Interactor.Initialize(this);
            gameObject.AddComponent<InteractionHUD>().Initialize(this);
            BuildStaffBody();
        }

        public void SetUIBlocked(bool blocked)
        {
            if (IsUIBlocked == blocked) return;
            IsUIBlocked = blocked;
            if (blocked && Interactor != null) Interactor.CancelInteraction();
        }

        public void ConfigureAuthority(bool authority, bool local, Rect viewport)
        {
            HasWorldAuthority = authority; IsLocallyControlled = local;
            pushes.Clear();
            if (Interactor) Interactor.SetWorldAuthority(authority);
            if (body) body.enabled = authority;
            if (PlayerCamera)
            {
                PlayerCamera.rect = viewport; PlayerCamera.enabled = local;
                var listener = PlayerCamera.GetComponent<AudioListener>();
                bool wantsListener = local && (LocalCoopBootstrap.Instance == null ||
                    LocalCoopBootstrap.Instance.LanRole != LanRole.Offline || ActorId == 0);
                if (wantsListener && !listener) listener = PlayerCamera.gameObject.AddComponent<AudioListener>();
                if (listener) listener.enabled = wantsListener;
            }
        }

        public void ApplyReplicaPose(Vector3 position, Quaternion rotation, Quaternion cameraRotation,
            Vector2 movement, bool usingHands)
        {
            if (HasWorldAuthority) return;
            transform.SetPositionAndRotation(position, rotation);
            PlayerCamera.transform.localRotation = cameraRotation;
            replicaVelocity = (transform.right * movement.x + transform.forward * movement.y) * walkSpeed;
            AnimateStaff(movement, usingHands);
        }
        public void StopReplicaMotion() { if (!HasWorldAuthority) replicaVelocity = Vector3.zero; }

        public void ResetToSpawn(Transform spawn)
        {
            if (!spawn || !body) return;
            Interactor.CancelInteraction();
            body.enabled = false;
            transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            pitch = verticalSpeed = stride = 0;
            PlayerCamera.transform.localRotation = Quaternion.identity;
            pushes.Clear();
            body.enabled = HasWorldAuthority;
        }

        private void Update()
        {
            if (body == null || !HasWorldAuthority) return;
            bool paused = LocalCoopBootstrap.Instance && LocalCoopBootstrap.Instance.IsPaused;
            if (paused || !DeviceReady)
            {
                AnimateStaff(Vector2.zero);
                return;
            }
            var movement = IsUIBlocked ? Vector2.zero : Input.Move;
            if (!IsUIBlocked)
            {
                var look = Input.Look * (Input.LookIsDegrees ? 1 : Input.IsMouseLook ? mouseSensitivity : controllerLookSpeed * Time.deltaTime);
                transform.Rotate(0, look.x, 0);
                pitch = Mathf.Clamp(pitch - look.y, -78, 78);
                PlayerCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            else verticalSpeed = Mathf.Max(verticalSpeed - 22 * Time.deltaTime, -30);
            float speed = walkSpeed * (Input.SprintHeld && !IsUIBlocked ? 1.25f : 1);
            if (LuggageCart.IsGuiding(ActorId)) speed = Input.SprintHeld ? 2.7f : 1.75f;
            Vector3 velocity = (transform.right * movement.x + transform.forward * movement.y) * speed;
            velocity.y = verticalSpeed;
            body.Move(velocity * Time.deltaTime);
            AnimateStaff(movement);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var guest = hit.collider.GetComponentInParent<GuestPhysicalReaction>();
            if (guest && HasWorldAuthority && Input.Move.sqrMagnitude > .05f && Mathf.Abs(hit.normal.y) < .65f)
                guest.StaffContact(hit.moveDirection, Input.SprintHeld && Input.Move.sqrMagnitude > .6f, walkSpeed * Input.Move.magnitude);
            var rigidbody = hit.rigidbody;
            if (rigidbody && !rigidbody.isKinematic && (rigidbody.mass <= 45 || rigidbody.GetComponent<LuggageCart>()) && hit.moveDirection.y > -0.4f)
                pushes[rigidbody] = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
        }

        private void FixedUpdate()
        {
            if (!HasWorldAuthority) { pushes.Clear(); return; }
            foreach (var entry in pushes)
                if (entry.Key) entry.Key.AddForce(entry.Value * (entry.Key.GetComponent<LuggageCart>() ? 90 : 18), ForceMode.Force);
            pushes.Clear();
        }

        private Material Material(string label, Color color, float smoothness = 0.25f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = label, color = color };
            material.SetFloat("_Smoothness", smoothness);
            materials.Add(material);
            return material;
        }

        private Transform Shape(PrimitiveType primitive, string label, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = label;
            part.layer = 8 + ActorId;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            return part.transform;
        }

        private Transform Joint(string label, Vector3 position)
        {
            var joint = new GameObject(label).transform;
            joint.SetParent(transform, false);
            joint.localPosition = position;
            return joint;
        }

        private void BuildStaffBody()
        {
            bool manager = ActorId == 0;
            var coat = Material("Staff coat", manager ? new Color(0.16f, 0.42f, 0.45f) : new Color(0.73f, 0.3f, 0.15f));
            var trousers = Material("Charcoal trousers", new Color(0.12f, 0.14f, 0.18f));
            var cream = Material("Ivory shirt", new Color(0.91f, 0.85f, 0.67f));
            var brass = Material("Staff brass", new Color(0.82f, 0.63f, 0.25f), 0.5f);
            var skin = Material("Staff skin", manager ? new Color(0.62f, 0.37f, 0.23f) : new Color(0.87f, 0.62f, 0.41f));
            var hair = Material("Staff hair", manager ? new Color(0.10f, 0.065f, 0.045f) : new Color(0.24f, 0.10f, 0.035f));
            var eye = Material("Eye white", new Color(0.97f, 0.95f, 0.86f));
            Shape(PrimitiveType.Capsule, "Rounded uniform torso", transform, new Vector3(0, 1.04f, 0), new Vector3(manager ? 0.69f : 0.76f, 0.42f, 0.4f), coat);
            Shape(PrimitiveType.Cube, "Shirt front", transform, new Vector3(0, 1.12f, 0.202f), new Vector3(0.2f, 0.37f, 0.025f), cream);
            Shape(PrimitiveType.Cube, "Apron waist", transform, new Vector3(0, 0.91f, 0.2f), new Vector3(manager ? 0.33f : 0.58f, 0.25f, 0.04f), manager ? coat : cream);
            Shape(PrimitiveType.Cube, "Large name badge", transform, new Vector3(-0.22f, 1.22f, 0.205f), new Vector3(0.125f, 0.073f, 0.02f), brass);
            for (int i = 0; i < 3; i++) Shape(PrimitiveType.Sphere, "Brass button", transform, new Vector3(0.055f, 1.23f - 0.11f * i, 0.225f), Vector3.one * 0.04f, brass);
            head = Shape(PrimitiveType.Sphere, "Friendly oversized head", transform, new Vector3(0, 1.60f, 0), new Vector3(0.56f, 0.53f, 0.49f), skin);
            headRest = head.localPosition;
            Shape(PrimitiveType.Sphere, "Hair", transform, new Vector3(0, 1.80f, -0.05f), new Vector3(0.56f, 0.23f, 0.45f), hair);
            if (manager)
            {
                Shape(PrimitiveType.Cylinder, "Bell staff cap", transform, new Vector3(0, 1.87f, -0.025f), new Vector3(0.57f, 0.06f, 0.48f), coat);
                Shape(PrimitiveType.Cube, "Cap stripe", transform, new Vector3(0, 1.845f, 0.215f), new Vector3(0.42f, 0.045f, 0.035f), brass);
            }
            else
            {
                Shape(PrimitiveType.Cube, "Work cap brim", transform, new Vector3(0, 1.84f, 0.15f), new Vector3(0.55f, 0.05f, 0.44f), coat);
                Shape(PrimitiveType.Sphere, "Work cap", transform, new Vector3(0, 1.85f, -0.03f), new Vector3(0.56f, 0.2f, 0.43f), coat);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Shape(PrimitiveType.Sphere, "Eye", transform, new Vector3(side * 0.105f, 1.64f, 0.218f), new Vector3(0.15f, 0.16f, 0.07f), eye);
                Shape(PrimitiveType.Sphere, "Pupil", transform, new Vector3(side * 0.105f, 1.64f, 0.254f), new Vector3(0.049f, 0.065f, 0.025f), hair);
                var arm = Joint(side < 0 ? "Left arm pivot" : "Right arm pivot", new Vector3(side * 0.39f, 1.27f, 0));
                Shape(PrimitiveType.Capsule, "Chunky sleeve", arm, new Vector3(0, -0.17f, 0), new Vector3(0.23f, 0.25f, 0.25f), coat);
                Shape(PrimitiveType.Sphere, "Large hand", arm, new Vector3(0, -0.40f, 0.025f), new Vector3(0.26f, 0.25f, 0.23f), skin);
                var leg = Joint(side < 0 ? "Left leg pivot" : "Right leg pivot", new Vector3(side * 0.18f, 0.75f, 0));
                Shape(PrimitiveType.Capsule, "Trouser leg", leg, new Vector3(0, -0.25f, 0), new Vector3(0.26f, 0.29f, 0.27f), trousers);
                Shape(PrimitiveType.Cube, "Wide work shoe", leg, new Vector3(0, -0.62f, 0.085f), new Vector3(0.29f, 0.17f, 0.42f), hair);
                if (side < 0) { leftArm = arm; leftLeg = leg; }
                else { rightArm = arm; rightLeg = leg; }
            }
            Shape(PrimitiveType.Sphere, "Nose", transform, new Vector3(0, 1.57f, 0.245f), new Vector3(0.10f, 0.10f, 0.10f), skin);
            Shape(PrimitiveType.Cube, "Simple smile", transform, new Vector3(0, 1.47f, 0.23f), new Vector3(0.16f, 0.025f, 0.02f), hair);
        }

        private void AnimateStaff(Vector2 movement, bool replicaHands = false)
        {
            if (!leftArm) return;
            float intensity = movement.magnitude;
            stride += Time.deltaTime * (intensity > 0.05f ? 9 : 2);
            float swing = Mathf.Sin(stride) * 25 * intensity;
            bool usingHands = replicaHands || Interactor.IsInteracting || Interactor.HeldBody;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, Quaternion.Euler(usingHands ? -65 : -swing, 0, -6), Time.deltaTime * 12);
            rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, Quaternion.Euler(usingHands ? -65 : swing, 0, 6), Time.deltaTime * 12);
            head.localPosition = headRest + Vector3.up * (Mathf.Sin(stride * 2) * 0.008f * intensity);
        }

        private void OnDestroy()
        {
            foreach (var material in materials) if (material) Destroy(material);
        }
    }
}
