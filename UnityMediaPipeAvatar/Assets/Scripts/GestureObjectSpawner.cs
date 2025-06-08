using UnityEngine;
using System.Collections;

public class GestureObjectActivator : MonoBehaviour
{
    public GameObject shieldObject;              // 방패 오브젝트
    public GameObject[] sphereObjects;           // 구 3개
    public Transform headTransform;              // 플레이어 머리
    public float activeDuration = 2f;            // 활성화 지속 시간 (초)

    private PipeServer server;

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
            Debug.Log("받은 메시지: " + msg);

        if (msg.Contains("CREATE_SHIELD"))
        {
            ActivateShield();
        }
        else if (msg.Contains("CREATE_SPHERES"))
        {
            ActivateSpheres();
        }
    }

    void ActivateShield()
    {
        if (shieldObject != null)
        {
            shieldObject.SetActive(true);
            Debug.Log("방패 활성화됨");
            StartCoroutine(DeactivateAfterTime(shieldObject, activeDuration));
        }
    }

    void ActivateSpheres()
    {
        foreach (GameObject s in sphereObjects)
        {
            if (s != null)
            {
                s.SetActive(true);
                StartCoroutine(DeactivateAfterTime(s, activeDuration));
            }
        }
        Debug.Log("스피어 3개 활성화됨");
    }

    IEnumerator DeactivateAfterTime(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        obj.SetActive(false);
    }
}
