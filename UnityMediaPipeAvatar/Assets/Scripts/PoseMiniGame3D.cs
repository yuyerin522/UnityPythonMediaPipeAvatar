using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PoseMiniGame3D : MonoBehaviour
{
    [Header("Refs")]
    public PipeServer server;

    [Tooltip("지시 이미지용 Quad의 MeshRenderer")]
    public MeshRenderer targetPoseQuad;

    [Tooltip("O/X 표시용 Quad의 MeshRenderer")]
    public MeshRenderer resultQuad;

    [Header("Materials (Unlit/Transparent 권장)")]
    public Material poseSideMat;    // 양팔 가로
    public Material poseUpMat;      // 만세
    public Material poseCircleMat;  // 머리 위 동그라미
    public Material correctMat;     // O
    public Material wrongMat;       // X

    [Header("Gameplay")]
    public float holdDuration = 3.0f;      // 유지해야 하는 시간
    public float resultDisplaySec = 1.2f;  // O/X 표시 시간
    public float restBetweenPosesSec = 2.0f; // 포즈 간 휴식 시간 (요청사항)
    public bool advanceOnFail = true;      // 실패해도 다음 포즈로 넘어갈지
    public bool billboardToCamera = true;  // 카메라를 항상 바라보게

    // 내부 상태
    private string currentTarget = "";
    private float holdTimer = 0f;
    private bool checking = false;

    // 순서 관리
    private readonly string[] allPoses = new[] { "MOON", "SMALLBALLS", "BIGBALL" };
    private Queue<string> orderQueue = new Queue<string>();

    // 현재 감지된 포즈(프레임마다 업데이트)
    private string currentPose = "NONE";

    private Dictionary<string, Material> poseMatMap;

    void Awake()
    {
        poseMatMap = new Dictionary<string, Material>()
        {
            { "MOON",       poseSideMat },    // 양팔 가로
            { "SMALLBALLS", poseUpMat },      // 만세
            { "BIGBALL",    poseCircleMat }   // 머리 위 동그라미
        };

        if (resultQuad != null) resultQuad.gameObject.SetActive(false);
    }

    void Start()
    {
        // 랜덤 순서 세팅
        RefillAndShuffle();
        NextPose();
    }

    void Update()
    {
        // 메시지 읽기 (POSE_XXX 라벨만 사용)
        string msg = server != null ? server.GetLatestMessage() : "";
        if (!string.IsNullOrEmpty(msg))
        {
            if (msg.StartsWith("POSE_"))
            {
                currentPose = msg.Substring("POSE_".Length).Trim();
            }
        }

        if (!checking) return;

        // 목표와 같으면 홀드 타이머 증가
        if (currentPose == currentTarget)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdDuration)
            {
                StartCoroutine(ShowResultAndIntermission(true));
            }
        }
        else
        {
            // NONE이 아니면서 목표와 다르면 실패 처리
            if (currentPose != "NONE")
            {
                StartCoroutine(ShowResultAndIntermission(false));
            }
            holdTimer = 0f;
        }

        // 빌보딩(옵션)
        if (billboardToCamera)
        {
            var cam = Camera.main;
            if (cam)
            {
                if (targetPoseQuad) targetPoseQuad.transform.LookAt(
                    targetPoseQuad.transform.position + cam.transform.rotation * Vector3.forward,
                    cam.transform.rotation * Vector3.up);

                if (resultQuad && resultQuad.gameObject.activeSelf)
                    resultQuad.transform.LookAt(
                        resultQuad.transform.position + cam.transform.rotation * Vector3.forward,
                        cam.transform.rotation * Vector3.up);
            }
        }
    }

    private void NextPose()
    {
        if (orderQueue.Count == 0)
            RefillAndShuffle();

        currentTarget = orderQueue.Dequeue();
        ApplyTargetMaterial(currentTarget);

        holdTimer = 0f;
        checking = true;
    }

    private void ApplyTargetMaterial(string poseKey)
    {
        if (targetPoseQuad == null) return;

        if (poseMatMap.TryGetValue(poseKey, out var mat) && mat != null)
        {
            targetPoseQuad.sharedMaterial = mat;
            targetPoseQuad.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("해당 포즈 머티리얼이 설정되지 않았습니다: " + poseKey);
        }
    }

    private IEnumerator ShowResultAndIntermission(bool success)
    {
        checking = false;

        // 결과 표시
        if (resultQuad != null)
        {
            resultQuad.sharedMaterial = success ? correctMat : wrongMat;
            resultQuad.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(resultDisplaySec);

        if (resultQuad != null)
            resultQuad.gameObject.SetActive(false);

        // 타겟 잠시 숨기고 휴식
        if (targetPoseQuad) targetPoseQuad.gameObject.SetActive(false);
        yield return new WaitForSeconds(restBetweenPosesSec);

        // 실패 후 같은 포즈 재시도 모드
        if (!success && !advanceOnFail)
        {
            ApplyTargetMaterial(currentTarget); // 같은 포즈 다시 표시
            holdTimer = 0f;
            checking = true;
            yield break;
        }

        // 다음 포즈로 진행
        NextPose();
    }

    private void RefillAndShuffle()
    {
        // 3개 포즈를 셔플해서 큐에 채움
        List<string> list = new List<string>(allPoses);
        for (int i = 0; i < list.Count; ++i)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
        foreach (var s in list) orderQueue.Enqueue(s);
    }
}
