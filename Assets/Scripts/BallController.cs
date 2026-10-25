using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// A/D 또는 ←/→로 힘을 줘서 좌우 이동(관성 있음)
// Space: 땅(또는 착지 여유시간)이면 점프, 공중이면 2단 점프(1회). 1단 점프는 일찍 떼면 낮게
// ↓ 꾹: 땅에서 차지 → 떼면 슈퍼 점프 (쿨타임)
// 중력: 올라갈 때 약하게, 떨어질 때 강하게
[RequireComponent(typeof(Rigidbody2D))]
public class BallController : MonoBehaviour
{
    [Header("이동")]
    [Tooltip("좌우로 미는 힘 (클수록 빨리 가속)")]
    public float moveForce = 15f;
    [Tooltip("좌우 최고 속도 (이 속도 이상이면 더 밀지 않음)")]
    public float maxSpeed = 6f;
    [Tooltip("공중에서 조작이 먹히는 비율 (0 = 조작 불가, 1 = 땅과 같음)")]
    [Range(0f, 1f)] public float airControl = 0.3f;

    [Header("점프 (Space)")]
    [Tooltip("점프 속도 (1단, 2단 모두)")]
    public float jumpSpeed = 10f;
    [Tooltip("1단 점프로 올라가는 중에 Space를 떼면 남은 위쪽 속도에 곱하는 값 (작을수록 낮게)")]
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.5f;
    [Tooltip("공중에서 추가로 할 수 있는 점프 횟수 (1 = 2단 점프)")]
    public int maxAirJumps = 1;
    [Tooltip("땅에서 떨어진 뒤에도 이 시간(초) 동안은 땅 점프 가능 (착지 여유)")]
    public float coyoteTime = 0.1f;

    [Header("중력")]
    [Tooltip("점프로 올라갈 때 중력 배율 (튕겨 오를 땐 떨어질 때 중력 사용: 튕김이 점점 커지지 않게)")]
    public float riseGravity = 1.5f;
    [Tooltip("떨어질 때 중력 배율")]
    public float fallGravity = 3f;

    [Header("슈퍼 점프 (↓ 꾹)")]
    [Tooltip("최대 차지까지 걸리는 시간(초)")]
    public float maxChargeTime = 1f;
    [Tooltip("차지 없이 바로 뗐을 때 일반 점프 속도의 몇 배")]
    public float minSuperMultiplier = 1f;
    [Tooltip("최대 차지 시 일반 점프 속도의 몇 배 (높이는 대략 이 값의 제곱배, 1.7 ≈ 3배)")]
    public float superJumpMultiplier = 1.7f;
    [Tooltip("최대 차지 시 공 색")]
    public Color chargeColor = Color.red;
    [Tooltip("차지 중 위로 튀는 최대 속도 (작을수록 덜 튐)")]
    public float chargeBounceLimit = 1f;
    [Tooltip("다시 쓰기까지 기다리는 시간(초)")]
    public float superJumpCooldown = 3f;
    [Tooltip("쿨타임 중 공 밝기 (0 = 검정, 1 = 원래 색)")]
    [Range(0f, 1f)] public float cooldownBrightness = 0.4f;

    [Header("땅 판정")]
    [Tooltip("이 값보다 위쪽을 향한 면에 닿아야 땅으로 인정 (0~1)")]
    [Range(0f, 1f)] public float groundNormalThreshold = 0.5f;
    [Tooltip("공 아래쪽 끝에서 바닥까지 이 거리 안이면 땅으로 인정")]
    public float groundCheckDistance = 0.3f;
    [Tooltip("땅으로 인정할 레이어")]
    public LayerMask groundLayers = ~0;
    [Tooltip("바닥에 닿아 있을 때 위쪽 속도가 이 값 이하면 착지로 인정 (가만히 있을 때 미세하게 떨리는 것 무시)")]
    public float landingMaxUpSpeed = 0.5f;

    private Rigidbody2D rb;
    private CircleCollider2D circle;
    private SpriteRenderer sr;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[4];
    private Color baseColor = Color.white;
    private float moveInput;

    private float lastGroundedTime = -999f; // 마지막으로 착지 상태였던 시각
    private int airJumpsLeft;
    private bool isFirstJumpRising;         // 1단 점프로 올라가는 중 (Space 떼면 끊김)
    private bool isJumpRising;              // 점프(1단/2단/슈퍼)로 올라가는 중 (약한 중력 적용)
    private bool isCharging;
    private float chargeTime;
    private float cooldownLeft;

    private bool CanGroundJump => Time.time - lastGroundedTime <= coyoteTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        circle = GetComponent<CircleCollider2D>();
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
        airJumpsLeft = maxAirJumps;
    }

    void Update()
    {
        bool left, right, jumpDown, jumpUp, superDown, superHeld, superUp;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        left = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
        right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
        jumpDown = kb.spaceKey.wasPressedThisFrame;
        jumpUp = kb.spaceKey.wasReleasedThisFrame;
        superDown = kb.downArrowKey.wasPressedThisFrame;
        superHeld = kb.downArrowKey.isPressed;
        superUp = kb.downArrowKey.wasReleasedThisFrame;
#else
        left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        jumpDown = Input.GetKeyDown(KeyCode.Space);
        jumpUp = Input.GetKeyUp(KeyCode.Space);
        superDown = Input.GetKeyDown(KeyCode.DownArrow);
        superHeld = Input.GetKey(KeyCode.DownArrow);
        superUp = Input.GetKeyUp(KeyCode.DownArrow);
#endif
        moveInput = (right ? 1f : 0f) - (left ? 1f : 0f);

        if (jumpDown) OnJumpPressed();
        if (jumpUp) OnJumpReleased();

        // 슈퍼 점프 차지: 땅에서 쿨타임이 끝났을 때만 시작
        if (superDown && CanGroundJump && cooldownLeft <= 0f) { isCharging = true; chargeTime = 0f; }
        if (isCharging && superHeld) chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
        if (isCharging && superUp) OnSuperReleased();

        if (cooldownLeft > 0f) cooldownLeft -= Time.deltaTime;
        UpdateColor();
    }

    // Space 누름: 땅이면 1단, 공중이면 남은 횟수로 2단. 둘 다 세로 속도를 덮어씀
    void OnJumpPressed()
    {
        if (CanGroundJump)
        {
            Jump(jumpSpeed);
            isFirstJumpRising = true;
        }
        else if (airJumpsLeft > 0)
        {
            airJumpsLeft--;
            Jump(jumpSpeed);
            isFirstJumpRising = false; // 2단은 가변 점프 없음
        }
    }

    // Space 뗌: 1단 점프로 올라가는 중이면 낮게 끊음
    void OnJumpReleased()
    {
        if (isFirstJumpRising && rb.linearVelocity.y > 0f)
        {
            Vector2 v = rb.linearVelocity;
            v.y *= jumpCutMultiplier;
            rb.linearVelocity = v;
        }
        isFirstJumpRising = false;
    }

    // ↓ 뗌: 땅이면 차지량만큼 슈퍼 점프
    void OnSuperReleased()
    {
        if (CanGroundJump)
        {
            float ratio = maxChargeTime > 0f ? chargeTime / maxChargeTime : 1f;
            Jump(jumpSpeed * Mathf.Lerp(minSuperMultiplier, superJumpMultiplier, ratio));
            isFirstJumpRising = false;
            cooldownLeft = superJumpCooldown;
        }
        isCharging = false;
        chargeTime = 0f;
    }

    void Jump(float speed)
    {
        Vector2 v = rb.linearVelocity;
        v.y = speed;
        rb.linearVelocity = v;
        rb.gravityScale = riseGravity;
        isJumpRising = true;
        lastGroundedTime = -999f; // 점프하면 착지 여유 끝
    }

    void FixedUpdate()
    {
        // 착지: 땅에 닿아 있고 올라가는 중이 아니면 2단 횟수 초기화
        if (rb.linearVelocity.y <= landingMaxUpSpeed && CheckGround())
        {
            lastGroundedTime = Time.time;
            airJumpsLeft = maxAirJumps;
        }

        // 중력: 점프로 올라갈 때만 약하게, 그 외(떨어질 때, 튕겨 오를 때)는 강하게
        if (rb.linearVelocity.y <= 0f)
        {
            isJumpRising = false;
            isFirstJumpRising = false;
        }
        rb.gravityScale = isJumpRising ? riseGravity : fallGravity;

        // 최고 속도를 넘는 방향으로는 더 밀지 않음 (반대 방향 감속은 허용)
        float vx = rb.linearVelocity.x;
        bool overSpeed = Mathf.Abs(vx) >= maxSpeed && Mathf.Sign(vx) == Mathf.Sign(moveInput);
        if (moveInput != 0f && !overSpeed)
        {
            float control = CanGroundJump ? 1f : airControl;
            rb.AddForce(Vector2.right * moveInput * moveForce * control, ForceMode2D.Force);
        }

        ClampChargeBounce();
    }

    // 차지 중엔 위로 덜 튀게 (충돌 직후에도 바로 적용)
    void OnCollisionEnter2D(Collision2D collision) => ClampChargeBounce();
    void OnCollisionStay2D(Collision2D collision) => ClampChargeBounce();

    void ClampChargeBounce()
    {
        if (!isCharging || rb.linearVelocity.y <= chargeBounceLimit) return;
        Vector2 v = rb.linearVelocity;
        v.y = chargeBounceLimit;
        rb.linearVelocity = v;
    }

    // 공 반지름(실제 크기)
    float WorldRadius()
    {
        if (circle == null) return 0.5f;
        Vector3 s = transform.lossyScale;
        return circle.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
    }

    // 공 중심에서 아래로 레이를 쏴서, 공 아래 끝 + groundCheckDistance 안에 위를 향한 바닥이 있으면 땅
    bool CheckGround()
    {
        Vector2 origin = circle != null ? (Vector2)transform.TransformPoint(circle.offset) : rb.position;
        float dist = WorldRadius() + groundCheckDistance;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(groundLayers);
        filter.useTriggers = false;

        int count = Physics2D.Raycast(origin, Vector2.down, filter, hits, dist);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].rigidbody == rb) continue; // 자기 자신 제외
            if (hits[i].normal.y > groundNormalThreshold) return true;
        }
        return false;
    }

    // Scene 뷰에서 땅 판정 레이를 초록 선으로 표시 (Ball 선택 시)
    void OnDrawGizmosSelected()
    {
        if (circle == null) circle = GetComponent<CircleCollider2D>();
        Vector3 origin = circle != null ? transform.TransformPoint(circle.offset) : transform.position;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + Vector3.down * (WorldRadius() + groundCheckDistance));
    }

    // 차지 중이면 점점 빨갛게, 슈퍼 점프 쿨타임 중이면 어둡게
    void UpdateColor()
    {
        if (sr == null) return;
        float k = cooldownLeft > 0f ? cooldownBrightness : 1f;
        Color c = new Color(baseColor.r * k, baseColor.g * k, baseColor.b * k, baseColor.a);
        if (isCharging && maxChargeTime > 0f) c = Color.Lerp(c, chargeColor, chargeTime / maxChargeTime);
        sr.color = c;
    }
}
