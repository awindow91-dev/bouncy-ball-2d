using UnityEngine;

// 카메라가 목표(공)를 따라감
// 가로: 부드럽게 / 세로: 데드존 밖으로 나갈 때만, 화면 끝 근처면 빠르게
// 공이 빠르면 줌아웃, 올라갈 땐 위쪽을 미리 보여줌
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Tooltip("따라갈 대상 (Ball)")]
    public Transform target;
    [Tooltip("가로 따라가는 속도 (클수록 빨리 붙음)")]
    public float smoothSpeed = 5f;
    [Tooltip("대상 기준 카메라 위치 (Z는 -10 유지)")]
    public Vector3 offset = new Vector3(0f, 1f, -10f);

    [Header("세로 데드존")]
    [Tooltip("카메라 중심 위아래로 이 높이 안에서는 세로로 안 움직임 (전체 높이)")]
    public float deadZoneHeight = 3f;
    [Tooltip("데드존을 벗어났을 때 세로 따라가는 속도")]
    public float verticalSpeed = 3f;

    [Header("화면 끝 보호")]
    [Tooltip("화면 위/아래 끝에서 이 비율(0~0.5) 안으로 들어오면 빠르게 따라감")]
    [Range(0f, 0.5f)] public float edgeMargin = 0.2f;
    [Tooltip("화면 끝 근처일 때 세로 따라가는 속도")]
    public float edgeFollowSpeed = 15f;

    [Header("줌 (Orthographic Size)")]
    [Tooltip("기본 카메라 크기 (클수록 넓게 보임)")]
    public float baseSize = 8f;
    [Tooltip("최대로 줌아웃했을 때 크기")]
    public float maxSize = 13f;
    [Tooltip("이 속도부터 줌아웃 시작")]
    public float zoomStartSpeed = 10f;
    [Tooltip("이 속도에서 최대 크기")]
    public float zoomMaxSpeed = 25f;
    [Tooltip("줌아웃(넓어질 때) 속도")]
    public float zoomOutSpeed = 4f;
    [Tooltip("원래 크기로 돌아올 때 속도 (작을수록 천천히)")]
    public float zoomInSpeed = 1f;

    [Header("위쪽 미리보기 (Look Ahead)")]
    [Tooltip("올라가는 속도에 곱해서 카메라를 위로 미리 올리는 양")]
    public float lookAheadFactor = 0.15f;
    [Tooltip("미리보기 최대 거리")]
    public float maxLookAhead = 3f;
    [Tooltip("미리보기 값이 바뀌는 속도")]
    public float lookAheadSpeed = 3f;

    private Camera cam;
    private Rigidbody2D targetRb;
    private float targetY;
    private float lookAhead;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographicSize = baseSize;
    }

    void Start()
    {
        targetY = transform.position.y;
        if (target != null) targetRb = target.GetComponent<Rigidbody2D>();
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;
        Vector2 vel = targetRb != null ? targetRb.linearVelocity : Vector2.zero;

        // 줌: 빠르면 넓게, 느려지면 천천히 원래 크기로
        float t = Mathf.InverseLerp(zoomStartSpeed, zoomMaxSpeed, vel.magnitude);
        float wantSize = Mathf.Lerp(baseSize, maxSize, t);
        float zSpeed = wantSize > cam.orthographicSize ? zoomOutSpeed : zoomInSpeed;
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, wantSize, zSpeed * dt);
        float size = cam.orthographicSize;

        // 올라갈 때만 위쪽을 미리 보여줌
        float wantLook = vel.y > 0f ? Mathf.Min(vel.y * lookAheadFactor, maxLookAhead) : 0f;
        lookAhead = Mathf.Lerp(lookAhead, wantLook, lookAheadSpeed * dt);

        Vector3 desired = target.position + offset;
        float half = deadZoneHeight * 0.5f;
        float lookY = desired.y + lookAhead;

        // 데드존: 벗어난 만큼만 목표 Y를 옮김
        if (lookY > targetY + half) targetY = lookY - half;
        else if (lookY < targetY - half) targetY = lookY + half;

        // 화면 끝 보호: 공이 위/아래 끝 근처면 그 안으로 들어오도록 빠르게
        float margin = size * edgeMargin * 2f;  // 화면 높이(2*size) 기준
        float minCamY = target.position.y - (size - margin);
        float maxCamY = target.position.y + (size - margin);
        Vector3 pos = transform.position;
        bool nearEdge = pos.y < minCamY || pos.y > maxCamY;
        targetY = Mathf.Clamp(targetY, minCamY, maxCamY);

        pos.x = Mathf.Lerp(pos.x, desired.x, smoothSpeed * dt);
        pos.y = Mathf.Lerp(pos.y, targetY, (nearEdge ? edgeFollowSpeed : verticalSpeed) * dt);

        // 어떤 경우에도 공이 화면 밖으로 나가지 않게 최종 보정
        float hardLimit = size * 0.9f;
        pos.y = Mathf.Clamp(pos.y, target.position.y - hardLimit, target.position.y + hardLimit);
        pos.z = desired.z;
        transform.position = pos;
    }

    // Scene 뷰에서 데드존 범위를 노란 선으로 표시
    void OnDrawGizmosSelected()
    {
        Vector3 c = Application.isPlaying ? new Vector3(transform.position.x, targetY, 0f) : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(new Vector3(c.x, c.y - offset.y, 0f), new Vector3(4f, deadZoneHeight, 0f));
    }
}
