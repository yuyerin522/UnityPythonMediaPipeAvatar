using UnityEngine;

public class GestureObjectActivator : MonoBehaviour
{
    public GameObject shieldObject;       // 방패
    public GameObject[] sphereObjects;    // 작은 공 3개
    public GameObject bigBallObject;      // 거대한 공
    public GameObject moonAttackObject;   // 달 공격
    public float deactivateDelay = 1f;  // 자동으로 꺼지는 시간

    private PipeServer server;
    private float lastActivatedAt = -999f;
    private string lastKind = "";

    void Start()
    {
        server = FindObjectOfType<PipeServer>();
        DeactivateAll();
    }

    void Update()
    {
        if (server == null) return;

        string msg = server.GetLatestMessage();
        if (!string.IsNullOrEmpty(msg))
        {
            Debug.Log("받은 메시지: " + msg);

            if (msg.Contains("CREATE_BIGBALL")) ActivateOnly("BIGBALL");
            else if (msg.Contains("CREATE_SPHERES")) ActivateOnly("SPHERES");
            else if (msg.Contains("CREATE_MOON")) ActivateOnly("MOON");
            else if (msg.Contains("CREATE_SHIELD")) ActivateOnly("SHIELD");
        }

        // 시간 지나면 자동 종료
        if (lastKind != "" && Time.time - lastActivatedAt > deactivateDelay)
        {
            DeactivateAll();
            lastKind = "";
        }
    }

    private void ActivateOnly(string kind)
    {
        DeactivateAll();

        switch (kind)
        {
            case "BIGBALL":
                if (bigBallObject) bigBallObject.SetActive(true);
                break;
            case "SPHERES":
                if (sphereObjects != null)
                    foreach (var s in sphereObjects) if (s) s.SetActive(true);
                break;
            case "MOON":
                if (moonAttackObject) moonAttackObject.SetActive(true);
                break;
            case "SHIELD":
                if (shieldObject) shieldObject.SetActive(true);
                break;
        }

        lastKind = kind;
        lastActivatedAt = Time.time;
    }

    private void DeactivateAll()
    {
        if (shieldObject && shieldObject.activeSelf) shieldObject.SetActive(false);
        if (bigBallObject && bigBallObject.activeSelf) bigBallObject.SetActive(false);
        if (moonAttackObject && moonAttackObject.activeSelf) moonAttackObject.SetActive(false);
        if (sphereObjects != null)
            foreach (var s in sphereObjects) if (s && s.activeSelf) s.SetActive(false);
    }
}
