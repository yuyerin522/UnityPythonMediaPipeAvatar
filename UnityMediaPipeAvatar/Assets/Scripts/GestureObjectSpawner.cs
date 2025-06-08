using UnityEngine;

public class GestureObjectActivator : MonoBehaviour
{
    public GameObject shieldObject;              // 방패 오브젝트
    public GameObject[] sphereObjects;           // 구 3개
    public float deactivateDelay = 1f;           // 포즈 멈춘 뒤 몇 초 후 비활성화

    private PipeServer server;
    private float lastShieldTime = -999f;
    private float lastSphereTime = -999f;

    void Start()
    {
        server = FindObjectOfType<PipeServer>();

        if (shieldObject != null) shieldObject.SetActive(false);
        foreach (var s in sphereObjects)
        {
            if (s != null) s.SetActive(false);
        }
    }

    void Update()
    {
        if (server == null) return;

        string msg = server.GetLatestMessage();
        if (!string.IsNullOrEmpty(msg))
        {
            Debug.Log("받은 메시지: " + msg);

            if (msg.Contains("CREATE_SHIELD"))
            {
                lastShieldTime = Time.time;
                if (!shieldObject.activeSelf)
                {
                    shieldObject.SetActive(true);
                    Debug.Log("방패 활성화됨");
                }
            }

            if (msg.Contains("CREATE_SPHERES"))
            {
                lastSphereTime = Time.time;
                foreach (GameObject s in sphereObjects)
                {
                    if (s != null && !s.activeSelf)
                    {
                        s.SetActive(true);
                    }
                }
                Debug.Log("스피어 3개 활성화됨");
            }
        }

        // 일정 시간 경과 후 비활성화
        if (Time.time - lastShieldTime > deactivateDelay)
        {
            if (shieldObject.activeSelf)
                shieldObject.SetActive(false);
        }

        if (Time.time - lastSphereTime > deactivateDelay)
        {
            foreach (GameObject s in sphereObjects)
            {
                if (s != null && s.activeSelf)
                    s.SetActive(false);
            }
        }
    }
}
