using System.Collections.Generic;
using UnityEngine;

public class Avatar : MonoBehaviour
{
    public Camera previewCamera; // OPTIONAL
    public Animator animator;

    [Header("Calibration")]
    public bool useCalibrationData = false;
    public PersistentCalibrationData calibrationData;

    public bool Calibrated { get; private set; }

    private PipeServer server;

    private Quaternion initialRotation;
    private Vector3 initialPosition;

    private Dictionary<HumanBodyBones, CalibrationData> parentCalibrationData = new Dictionary<HumanBodyBones, CalibrationData>();
    private CalibrationData spineUpDown, chest, head;

    // 하체 고정용 캐시
    private struct BoneLock
    {
        public Transform t;
        public Vector3 localPos;
        public Quaternion localRot;
    }
    private readonly HumanBodyBones[] lowerBodyBones = new HumanBodyBones[]
    {
        HumanBodyBones.Hips,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
    };
    private List<BoneLock> lowerLocks = new List<BoneLock>();

    private void Start()
    {
        initialRotation = transform.rotation;
        initialPosition = transform.position;

        server = FindObjectOfType<PipeServer>();
        if (server == null)
        {
            Debug.LogError("You must have a PipeServer in the scene!");
        }

        // Animator 영향 제거
        if (animator != null) animator.enabled = false;

        // 하체 뼈 초기 포즈를 저장 (고정용)
        CacheLowerBodyLocks();

        if (calibrationData && useCalibrationData)
        {
            CalibrateFromPersistent();
        }
    }

    private void CacheLowerBodyLocks()
    {
        lowerLocks.Clear();
        if (animator == null) return;

        foreach (var hb in lowerBodyBones)
        {
            var tf = animator.GetBoneTransform(hb);
            if (tf == null) continue;
            lowerLocks.Add(new BoneLock
            {
                t = tf,
                localPos = tf.localPosition,
                localRot = tf.localRotation
            });
        }
    }

    public void CalibrateFromPersistent()
    {
        parentCalibrationData.Clear();

        if (calibrationData)
        {
            foreach (PersistentCalibrationData.CalibrationEntry d in calibrationData.parentCalibrationData)
            {
                parentCalibrationData.Add(d.bone, d.data.ReconstructReferences());
            }
            spineUpDown = calibrationData.spineUpDown.ReconstructReferences();
            chest = calibrationData.chest.ReconstructReferences();
            head = calibrationData.head.ReconstructReferences();
        }

        Calibrated = true;
    }

    public void Calibrate()
    {
        if (animator == null || server == null)
        {
            Debug.LogError("Animator or PipeServer is missing.");
            return;
        }

        parentCalibrationData.Clear();

        // 상체만 Calibration (골반/하체 제외)
        spineUpDown = new CalibrationData(animator.transform,
            animator.GetBoneTransform(HumanBodyBones.Spine),
            animator.GetBoneTransform(HumanBodyBones.Neck),
            server.GetVirtualHip(), server.GetVirtualNeck());

        chest = new CalibrationData(animator.transform,
            animator.GetBoneTransform(HumanBodyBones.Chest),
            animator.GetBoneTransform(HumanBodyBones.Chest),
            server.GetLandmark(Landmark.RIGHT_HIP), server.GetLandmark(Landmark.LEFT_HIP));

        head = new CalibrationData(animator.transform,
            animator.GetBoneTransform(HumanBodyBones.Neck),
            animator.GetBoneTransform(HumanBodyBones.Head),
            server.GetVirtualNeck(), server.GetLandmark(Landmark.NOSE));

        // 팔만 추적
        AddCalibration(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            server.GetLandmark(Landmark.RIGHT_SHOULDER), server.GetLandmark(Landmark.RIGHT_ELBOW));
        AddCalibration(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            server.GetLandmark(Landmark.RIGHT_ELBOW), server.GetLandmark(Landmark.RIGHT_WRIST));

        AddCalibration(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            server.GetLandmark(Landmark.LEFT_SHOULDER), server.GetLandmark(Landmark.LEFT_ELBOW));
        AddCalibration(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            server.GetLandmark(Landmark.LEFT_ELBOW), server.GetLandmark(Landmark.LEFT_WRIST));

        // 하체 잠금 정보 갱신
        CacheLowerBodyLocks();

        Calibrated = true;
    }

    public void StoreCalibration()
    {
        if (!calibrationData)
        {
            Debug.LogError("Optional calibration data must be assigned to store into.");
            return;
        }

        var calibrations = new List<PersistentCalibrationData.CalibrationEntry>();
        foreach (KeyValuePair<HumanBodyBones, CalibrationData> k in parentCalibrationData)
        {
            calibrations.Add(new PersistentCalibrationData.CalibrationEntry() { bone = k.Key, data = k.Value });
        }
        calibrationData.parentCalibrationData = calibrations.ToArray();

        calibrationData.spineUpDown = spineUpDown;
        calibrationData.chest = chest;
        calibrationData.head = head;

        calibrationData.Dirty();

        Debug.Log("Completed storing calibration data " + calibrationData.name);
    }

    private void AddCalibration(HumanBodyBones parent, HumanBodyBones child, Transform trackParent, Transform trackChild)
    {
        parentCalibrationData.Add(parent,
            new CalibrationData(animator.transform, animator.GetBoneTransform(parent), animator.GetBoneTransform(child),
            trackParent, trackChild));
    }

    private void Update()
    {
        // 루트 위치/회전은 고정
        transform.position = initialPosition;
        transform.rotation = initialRotation;

        // 팔/상체만 포즈 적용
        if (parentCalibrationData.Count > 0)
        {
            foreach (var i in parentCalibrationData)
            {
                Quaternion deltaRotTracked = Quaternion.FromToRotation(i.Value.initialDir, i.Value.CurrentDirection);
                i.Value.parent.rotation = deltaRotTracked * i.Value.initialRotation;
            }

            // 척추/머리 부드럽게
            Vector3 hd = head.CurrentDirection;
            Quaternion headr = Quaternion.FromToRotation(head.initialDir, hd);
            Quaternion updown = Quaternion.FromToRotation(spineUpDown.initialDir,
                Vector3.Slerp(spineUpDown.initialDir, spineUpDown.CurrentDirection, .25f));

            float speed = 10f;
            spineUpDown.Tick(updown * spineUpDown.initialRotation, speed);
            chest.Tick(updown * chest.initialRotation, speed);
            head.Tick(updown * headr * head.initialRotation, speed);
        }
    }

    private void LateUpdate()
    {
        // 마지막에 하체를 강제로 초기 포즈로 되돌려 고정
        for (int i = 0; i < lowerLocks.Count; i++)
        {
            if (lowerLocks[i].t == null) continue;
            lowerLocks[i].t.localPosition = lowerLocks[i].localPos;
            lowerLocks[i].t.localRotation = lowerLocks[i].localRot;
        }
    }
}
