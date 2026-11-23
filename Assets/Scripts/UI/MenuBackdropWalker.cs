using UnityEngine;

/// <summary>
/// Ambient offline background NPC walker for the menu scene.
/// Strolls along defined Gas Station waypoint loops without NetworkBehaviour or NavMesh dependencies.
/// </summary>
public class MenuBackdropWalker : MonoBehaviour
{
    private static readonly int HashWalking = Animator.StringToHash("Walking");
    private static readonly int HashRunning = Animator.StringToHash("Running");
    private static readonly int HashIsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int HashGreeting = Animator.StringToHash("Greeting");

    [SerializeField] private Vector3[] waypoints;
    [SerializeField] private float walkSpeed = 1.55f;
    [SerializeField] private float rotationSpeed = 8.5f;
    [SerializeField] private Vector2 pauseDurationRange = new Vector2(1.2f, 3.8f);
    [SerializeField] private float arrivalThreshold = 0.25f;
    [SerializeField] private bool loop = true;
    [SerializeField] private int startWaypointIndex = 0;

    private Animator animator;
    private int currentWaypoint;
    private int direction = 1;
    private float pauseTimer;
    private bool isPaused;

    public void Initialize(Vector3[] routeWaypoints, float speed, int initialIndex)
    {
        waypoints = routeWaypoints;
        walkSpeed = Mathf.Max(0.6f, speed);
        startWaypointIndex = initialIndex;
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.SetBool(HashIsGrounded, true);
            animator.SetBool(HashRunning, false);
        }

        if (waypoints == null || waypoints.Length == 0)
            return;

        currentWaypoint = Mathf.Clamp(startWaypointIndex, 0, waypoints.Length - 1);
        transform.position = waypoints[currentWaypoint];
        AdvanceIndex();

        Vector3 firstDir = waypoints[currentWaypoint] - transform.position;
        firstDir.y = 0f;
        if (firstDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(firstDir.normalized);

        SetWalking(true);
    }

    private void Update()
    {
        if (waypoints == null || waypoints.Length < 2)
            return;

        if (isPaused)
        {
            pauseTimer -= Time.deltaTime;
            if (pauseTimer <= 0f)
            {
                isPaused = false;
                AdvanceIndex();
                SetWalking(true);
            }
            return;
        }

        Vector3 target = waypoints[currentWaypoint];
        Vector3 delta = target - transform.position;
        delta.y = 0f;
        float dist = delta.magnitude;

        if (dist <= arrivalThreshold)
        {
            SetWalking(false);
            if (animator != null && Random.value < 0.28f)
            {
                animator.ResetTrigger(HashGreeting);
                animator.SetTrigger(HashGreeting);
            }
            isPaused = true;
            pauseTimer = Random.Range(pauseDurationRange.x, pauseDurationRange.y);
            return;
        }

        Vector3 moveDir = delta / dist;
        transform.position += moveDir * (walkSpeed * Time.deltaTime);

        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        SetWalking(true);
    }

    private void AdvanceIndex()
    {
        if (waypoints == null || waypoints.Length < 2)
            return;

        if (loop)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Length;
        }
        else
        {
            currentWaypoint += direction;
            if (currentWaypoint >= waypoints.Length)
            {
                currentWaypoint = waypoints.Length - 2;
                direction = -1;
            }
            else if (currentWaypoint < 0)
            {
                currentWaypoint = 1;
                direction = 1;
            }
        }
    }

    private void SetWalking(bool walking)
    {
        if (animator == null) return;
        animator.SetBool(HashIsGrounded, true);
        animator.SetBool(HashRunning, false);
        animator.SetBool(HashWalking, walking);
    }
}
