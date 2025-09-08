using System.Collections.Generic;
using UnityEngine;

public class Avatar : MonoBehaviour
{
    public Animator animator;
    public LayerMask ground;

    // Calibration
    public bool autoCalibrateWhenStreamReady = true;
    // Compatibility for external scripts (e.g., CalibrationTimer.cs)
    public bool useCalibrationData = false;
    public PersistentCalibrationData calibrationData;

    // Lower body lock / root helpers
    public bool lockLowerBody = true;
    public bool keepRootGrounded = false;
    public bool rotateRootWithHips = true;   // turn ON in Inspector
    public float footGroundOffset = 0.1f;
    public float smoothSpeed = 12f;

    public bool Calibrated { get; private set; }

    private PipeServer server;
    private Quaternion initialRotation;
    private Vector3 initialPosition;

    private float _nextAutoCalibTime = 0f;
    private bool _didSnapRootYawAfterCalib = false;

    // ===== Upper body (local delta) =====
    private struct LocalCal
    {
        public Transform root, parent, tA, tB;
        public Quaternion initLocalRot;
        public Vector3 initDirLocal;

        public LocalCal(Transform root, Transform parent, Transform tA, Transform tB)
        {
            this.root = root; this.parent = parent; this.tA = tA; this.tB = tB;
            initLocalRot = parent ? parent.localRotation : Quaternion.identity;

            Vector3 a = ToLocal(root, tA.position);
            Vector3 b = ToLocal(root, tB.position);
            Vector3 d = b - a;
            initDirLocal = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        public Quaternion Solve()
        {
            if (!parent || !tA || !tB) return initLocalRot;
            Vector3 a = ToLocal(root, tA.position);
            Vector3 b = ToLocal(root, tB.position);
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-8f) return parent.localRotation;
            Quaternion delta = Quaternion.FromToRotation(initDirLocal, d.normalized);
            return delta * initLocalRot;
        }

        public static Vector3 ToLocal(Transform root, Vector3 w)
        {
            return root ? root.InverseTransformPoint(w) : w;
        }
    }

    // ===== Arms: swing + twist split =====
    private struct SwingTwistCal
    {
        public Transform root, parent;
        public Transform p0, p1, pTwist; // p0=start, p1=end, pTwist=twist ref
        public Quaternion initLocalRot;
        public Vector3 f0, up0;
        public bool invertRoll; // left arm usually true

        public SwingTwistCal(Transform root, Transform parent, Transform p0, Transform p1, Transform pTwist, bool invertRoll)
        {
            this.root = root; this.parent = parent;
            this.p0 = p0; this.p1 = p1; this.pTwist = pTwist;
            this.invertRoll = invertRoll;

            initLocalRot = parent ? parent.localRotation : Quaternion.identity;

            Vector3 a = LocalCal.ToLocal(root, p0.position);
            Vector3 b = LocalCal.ToLocal(root, p1.position);
            Vector3 c = LocalCal.ToLocal(root, pTwist.position);

            Vector3 f = (b - a);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.forward;
            f.Normalize();

            Vector3 upRef = (c - b);
            upRef -= Vector3.Dot(upRef, f) * f;
            if (upRef.sqrMagnitude < 1e-8f) upRef = Vector3.up;
            upRef.Normalize();

            f0 = f;
            up0 = upRef;
        }

        public Quaternion Solve()
        {
            if (!parent || !p0 || !p1 || !pTwist) return initLocalRot;

            Vector3 a = LocalCal.ToLocal(root, p0.position);
            Vector3 b = LocalCal.ToLocal(root, p1.position);
            Vector3 c = LocalCal.ToLocal(root, pTwist.position);

            Vector3 f = (b - a);
            if (f.sqrMagnitude < 1e-8f) return parent.localRotation;
            f.Normalize();

            Vector3 upRef = (c - b);
            upRef -= Vector3.Dot(upRef, f) * f;
            if (upRef.sqrMagnitude < 1e-8f) upRef = up0;
            upRef.Normalize();

            // 1) swing
            Quaternion swing = Quaternion.FromToRotation(f0, f);

            // 2) twist around forward
            Vector3 upAfterSwing = swing * up0;
            float angle = Vector3.SignedAngle(upAfterSwing, upRef, f);
            if (invertRoll) angle = -angle;
            Quaternion twist = Quaternion.AngleAxis(angle, f);

            return twist * swing * initLocalRot;
        }
    }

    // Upper body
    private LocalCal spineLC, chestLC, headLC, hipsLC;
    // Arms
    private SwingTwistCal rUpperArm, rLowerArm, lUpperArm, lLowerArm;

    // ===== Lower body lock =====
    private struct BoneLock { public Transform t; public Vector3 lp; public Quaternion lr; }
    private readonly HumanBodyBones[] lowerBones = new HumanBodyBones[]
    {
        HumanBodyBones.Hips,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
    };
    private List<BoneLock> lowerLocks = new List<BoneLock>();

    private Transform Bone(HumanBodyBones b) { return animator ? animator.GetBoneTransform(b) : null; }
    private Transform ChestBone() { return Bone(HumanBodyBones.Chest) ? Bone(HumanBodyBones.Chest) : Bone(HumanBodyBones.UpperChest); }

    private void Start()
    {
        initialRotation = transform.rotation;
        initialPosition = transform.position;

        server = FindObjectOfType<PipeServer>();
        if (!server) Debug.LogError("PipeServer is required in the scene.");

        if (animator) animator.enabled = false; // avoid Animator overriding bones
        CacheLowerBodyLocks();
    }

    private void Update()
    {
        // Auto-calibrate when stream is ready (retry every 0.5s)
        if (!Calibrated && autoCalibrateWhenStreamReady && Time.time >= _nextAutoCalibTime)
        {
            if (HasStream()) Calibrate();
            _nextAutoCalibTime = Time.time + 0.5f;
        }

        // Optional: keep root grounded
        if (Calibrated && keepRootGrounded && animator)
        {
            float displacement = 0f;
            RaycastHit h1;
            var lf = Bone(HumanBodyBones.LeftFoot);
            var rf = Bone(HumanBodyBones.RightFoot);
            if (lf && Physics.Raycast(lf.position, Vector3.down, out h1, 100f, ground, QueryTriggerInteraction.Ignore))
                displacement = (h1.point - lf.position).y;
            if (rf && Physics.Raycast(rf.position, Vector3.down, out h1, 100f, ground, QueryTriggerInteraction.Ignore))
            {
                float d2 = (h1.point - rf.position).y;
                if (Mathf.Abs(d2) < Mathf.Abs(displacement)) displacement = d2;
            }
            transform.position = Vector3.Lerp(
                transform.position,
                initialPosition + Vector3.up * (displacement + footGroundOffset),
                Time.deltaTime * 5f
            );
        }

        if (!Calibrated) return;

        // Upper body smoothing
        ApplySmooth(spineLC);
        ApplySmooth(chestLC);
        ApplySmooth(headLC);

        // Arms (swing + twist) smoothing
        if (rUpperArm.parent) rUpperArm.parent.localRotation = Quaternion.Slerp(rUpperArm.parent.localRotation, rUpperArm.Solve(), Time.deltaTime * smoothSpeed);
        if (rLowerArm.parent) rLowerArm.parent.localRotation = Quaternion.Slerp(rLowerArm.parent.localRotation, rLowerArm.Solve(), Time.deltaTime * smoothSpeed);
        if (lUpperArm.parent) lUpperArm.parent.localRotation = Quaternion.Slerp(lUpperArm.parent.localRotation, lUpperArm.Solve(), Time.deltaTime * smoothSpeed);
        if (lLowerArm.parent) lLowerArm.parent.localRotation = Quaternion.Slerp(lLowerArm.parent.localRotation, lLowerArm.Solve(), Time.deltaTime * smoothSpeed);

        // Root rotation follow with hips (smooth) and one-time yaw snap after calibration
        if (rotateRootWithHips && hipsLC.parent)
        {
            // smooth follow
            Vector3 a = LocalCal.ToLocal(hipsLC.root, hipsLC.tA.position);
            Vector3 b = LocalCal.ToLocal(hipsLC.root, hipsLC.tB.position);
            Vector3 dir = b - a;
            if (dir.sqrMagnitude > 1e-8f)
            {
                dir.y *= 0.5f;
                Quaternion delta = Quaternion.FromToRotation(hipsLC.initDirLocal, dir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, delta * initialRotation, Time.deltaTime * smoothSpeed);
            }

            // one-time yaw snap right after calibration
            if (!_didSnapRootYawAfterCalib)
            {
                Vector3 curr = (b - a);
                Vector3 init = hipsLC.initDirLocal;
                curr.y = 0f; init.y = 0f;
                if (curr.sqrMagnitude > 1e-8f && init.sqrMagnitude > 1e-8f)
                {
                    Quaternion yawDelta = Quaternion.FromToRotation(init.normalized, curr.normalized);
                    transform.rotation = yawDelta * initialRotation;
                    _didSnapRootYawAfterCalib = true;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (!lockLowerBody) return;
        for (int i = 0; i < lowerLocks.Count; i++)
        {
            if (!lowerLocks[i].t) continue;
            lowerLocks[i].t.localPosition = lowerLocks[i].lp;
            lowerLocks[i].t.localRotation = lowerLocks[i].lr;
        }
    }

    // ===== Calibrate =====
    public void Calibrate()
    {
        if (!animator || !server)
        {
            Debug.LogError("Animator or PipeServer is missing.");
            return;
        }
        if (!HasStream())
        {
            Debug.LogWarning("Stream not ready. Will retry soon.");
            return;
        }

        var root = animator.transform;

        // Upper body
        spineLC = new LocalCal(root, Bone(HumanBodyBones.Spine), server.GetVirtualHip(), server.GetVirtualNeck());
        chestLC = new LocalCal(root, ChestBone(), server.GetLandmark(Landmark.RIGHT_HIP), server.GetLandmark(Landmark.LEFT_HIP));
        headLC = new LocalCal(root, Bone(HumanBodyBones.Neck), server.GetVirtualNeck(), server.GetLandmark(Landmark.NOSE));
        hipsLC = new LocalCal(root, Bone(HumanBodyBones.Hips), server.GetLandmark(Landmark.RIGHT_HIP), server.GetLandmark(Landmark.LEFT_HIP));

        // Arms: right invertRoll=false, left invertRoll=true (flip if your model twists)
        rUpperArm = new SwingTwistCal(root, Bone(HumanBodyBones.RightUpperArm),
            server.GetLandmark(Landmark.RIGHT_SHOULDER),
            server.GetLandmark(Landmark.RIGHT_ELBOW),
            server.GetLandmark(Landmark.RIGHT_WRIST),
            false);

        rLowerArm = new SwingTwistCal(root, Bone(HumanBodyBones.RightLowerArm),
            server.GetLandmark(Landmark.RIGHT_ELBOW),
            server.GetLandmark(Landmark.RIGHT_WRIST),
            server.GetLandmark(Landmark.RIGHT_INDEX),
            false);

        lUpperArm = new SwingTwistCal(root, Bone(HumanBodyBones.LeftUpperArm),
            server.GetLandmark(Landmark.LEFT_SHOULDER),
            server.GetLandmark(Landmark.LEFT_ELBOW),
            server.GetLandmark(Landmark.LEFT_WRIST),
            true);

        lLowerArm = new SwingTwistCal(root, Bone(HumanBodyBones.LeftLowerArm),
            server.GetLandmark(Landmark.LEFT_ELBOW),
            server.GetLandmark(Landmark.LEFT_WRIST),
            server.GetLandmark(Landmark.LEFT_INDEX),
            true);

        CacheLowerBodyLocks();
        Calibrated = true;
        _didSnapRootYawAfterCalib = false; // enable one-time yaw snap on next Update
        Debug.Log("Avatar Calibrated (swing+twist).");
    }

    // Optional stub for compatibility
    public void StoreCalibration() { }

    // ===== Helpers =====
    private bool HasStream()
    {
        var ls = server ? server.GetLandmark(Landmark.LEFT_SHOULDER) : null;
        var le = server ? server.GetLandmark(Landmark.LEFT_ELBOW) : null;
        if (!ls || !le) return false;
        return Vector3.Distance(ls.position, le.position) > 0.01f;
    }

    private void ApplySmooth(LocalCal lc)
    {
        if (!lc.parent) return;
        lc.parent.localRotation = Quaternion.Slerp(lc.parent.localRotation, lc.Solve(), Time.deltaTime * smoothSpeed);
    }

    private void CacheLowerBodyLocks()
    {
        lowerLocks.Clear();
        if (!animator) return;
        foreach (var hb in lowerBones)
        {
            var tf = animator.GetBoneTransform(hb);
            if (!tf) continue;
            lowerLocks.Add(new BoneLock { t = tf, lp = tf.localPosition, lr = tf.localRotation });
        }
    }
}
