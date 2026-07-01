using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

/// <summary>
/// Controls a single robot (runner or hunter) using potential-field navigation.
/// Receives positions and game commands over ROS, decides on a target point each
/// physics tick, and drives the Rigidbody towards it.
/// </summary>
public class RobotBrain : MonoBehaviour
{
    #region Configuration

    [Header("Identity")]
    public int id = 0; // Unique ID for this bot, matches the ID used on the ROS topics

    [Header("Drive")]
    private float MaxSpeed = 1.0f;     // Current active speed (set from Runner/HunterSpeed when the game starts)
    private float RunnerSpeed = 1.0f;  // Speed used while this bot is the runner
    private float HunterSpeed = 1.0f;  // Speed used while this bot is a hunter

    [Header("Potential Field — Attraction")]
    private float GoalAttractionStrength = 2.0f; // How strongly the bot is pulled towards its target
    private float MinDistanceToGoal = 0.3f;      // Distance at which the bot considers itself "arrived"

    [Header("Potential Field — Repulsion")]
    private float RobotRepulsionStrength = 4.0f; // How strongly other robots push this one away
    private float RobotInfluenceRadius = 2.5f;   // Range within which other robots start to repel
    private float MinRobotSeparation = 1.6f;     // Clamp to avoid divide-by-near-zero blowups
    private float WallRepulsionStrength = 3.0f;  // How strongly arena walls push the bot back in
    private float WallSafetyMargin = 1.0f;       // Distance from a wall at which repulsion kicks in

    [Header("Jitter")]
    private float JitterStrength = 0.3f;  // Magnitude of random nudges added to the movement force
    private float JitterInterval = 0.2f;  // How often (seconds) a new jitter direction is rolled

    [Header("Runner — Corner Patrol")]
    private float CornerChangeInterval = 5f; // How long the runner stays heading to one corner before picking a new one
    private float HunterFleeRadius = 4.1f;   // Distance at which the runner notices a hunter and starts fleeing

    [Header("Debug")]
    public Vector3 testTarget;  // Manual target used when testDrive is enabled, for testing movement in isolation
    public bool testDrive = false;

    public Material Material1; // Unused — kept for inspector/editor reference
    public GameObject Object;  // Unused — kept for inspector/editor reference

    #endregion

    #region State

    private float _startTime = 0f;                 // Time.time when the current round started
    private int _runnerId = -1;                    // ID of whichever bot is currently the runner (-1 = none/idle)
    private bool _runnerVisible = false;            // Whether a hunter currently has line of sight on the runner
    private Vector3 _runnerPos;                     // Last known position of the runner
    private Dictionary<int, Vector3> _botPositions = new Dictionary<int, Vector3>(); // Latest known position of every bot, keyed by ID

    private Rigidbody _rb;
    private Vector3 _startPos; // Spawn position, used when resetting

    // Visual indicator: a green line shown 3 units in front of hunters
    private LineRenderer _frontLine;
    private const float FrontLineLength = 3f;

    // Runner state
    private int _currentCornerIndex = -1;   // Index into _corners the runner is currently heading towards
    private float _lastCornerChangeTime = 0f;

    // Patrol corners for the runner to cycle between when not being chased.
    // (Original 3x3 grid of corners left commented out below for reference.)
    //private readonly Vector3[] _corners =
    //{
    //    new Vector3(1, 0, 1), new Vector3(5, 0, 1), new Vector3(9, 0, 1),
    //    new Vector3(1, 0, 5), new Vector3(5, 0, 5), new Vector3(9, 0, 5),
    //    new Vector3(1, 0, 9), new Vector3(5, 0, 9), new Vector3(9, 0, 9),
    //};

    private readonly Vector3[] _corners =
    {
        new Vector3(1, 0, 5), new Vector3(9, 0, 5), new Vector3(5, 0, 1), new Vector3(5, 0, 9)
    };

    // Hunter state
    private Vector3 _roamTarget;       // Current random point a hunter is wandering towards when it can't see the runner
    private bool _hasRoamTarget = false;

    // Smoothed flee direction for the runner, to avoid the target snapping around
    // wildly frame-to-frame as nearby hunters move (which was causing driveTo's
    // angle gate to repeatedly stop the bot to re-rotate)
    private Vector3 _smoothedFleeDir = Vector3.zero;

    // Jitter
    private Vector3 _currentJitter = Vector3.zero; // Current random force vector, refreshed every JitterInterval
    private float _lastJitterTime = 0f;

    private enum GameState { Idle, Start, Pause, Ready }
    private GameState _gameState = GameState.Idle;

    private ROSConnection _ros;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        _startPos = transform.position;

        _rb = GetComponent<Rigidbody>();
        _rb.position = new Vector3(_startPos.x, 0f, _startPos.z);

        // Set up ROS publisher/subscribers
        _ros = ROSConnection.GetOrCreateInstance();
        _ros.RegisterPublisher<Int32Msg>("/game/robots/ready");
        _ros.Subscribe<PoseArrayMsg>("/robots/pos", OnRobotPositions);
        _ros.Subscribe<StringMsg>("/game/command", OnGameCommand);
        _ros.Subscribe<BoolMsg>("/robots/seen", OnSeen);

        // Tell the game master this bot is alive and ready to play
        PublishReady();
    }

    void FixedUpdate()
    {
        // Debug/test mode: ignore game state entirely and just drive towards testTarget
        if (testDrive)
        {
            driveTo(nextPoint(testTarget));
            UpdateFrontLine(true);
            return;
        }

        // Don't move while waiting for the game to start or while paused
        if (_gameState == GameState.Idle || _gameState == GameState.Pause)
        {
            UpdateFrontLine(false);
            return;
        }

        // Run the appropriate behaviour depending on this bot's role this round
        bool isHunter = id != _runnerId;
        if (isHunter)
            HunterBrain();
        else
            RunnerBrain();

        UpdateFrontLine(isHunter);
    }

    #endregion

    #region ROS Callbacks

    /// <summary>
    /// Updates the cached position of every bot from a ROS PoseArray.
    /// Bot ID is encoded in the z-component of the position, and ROS Y maps to Unity Z.
    /// </summary>
    void OnRobotPositions(PoseArrayMsg msg)
    {
        _botPositions.Clear();

        foreach (var pose in msg.poses)
        {
            int botId = (int)pose.position.z;
            _botPositions[botId] = new Vector3(
                (float)pose.position.x,
                0f,
                (float)pose.position.y  // ROS Y → Unity Z
            );
        }

        // Keep the cached runner position up to date for quick access elsewhere
        if (_runnerId >= 0 && _botPositions.TryGetValue(_runnerId, out Vector3 runnerPos))
            _runnerPos = runnerPos;
    }

    /// <summary>
    /// Handles game state commands published on /game/command:
    /// "start &lt;runnerId&gt;", "pause", "resume", "reset".
    /// </summary>
    void OnGameCommand(StringMsg msg)
    {
        string raw = msg.data.Trim();
        Debug.Log($"[RobotBrain:{id}] Command received: '{raw}'");

        // "start <id>" begins a new round and assigns the runner
        if (raw.StartsWith("start "))
        {
            if (int.TryParse(raw.Substring(6).Trim(), out int newRunnerId))
            {
                _runnerId = newRunnerId;
                _gameState = GameState.Start;
                _startTime = Time.time;
                MaxSpeed = (id == _runnerId) ? RunnerSpeed : HunterSpeed;
            }
            return;
        }

        switch (raw)
        {
            case "pause":
                _gameState = GameState.Pause;
                break;

            case "resume":
                _gameState = GameState.Start;
                break;

            case "reset":
                ResetState();
                break;
        }

        Debug.Log($"[RobotBrain:{id}] State → {_gameState}");
    }

    /// <summary>
    /// Updates whether a hunter currently has line of sight on the runner.
    /// </summary>
    void OnSeen(BoolMsg msg)
    {
        _runnerVisible = msg.data;
        Debug.Log($"[RobotBrain:{id}] Runner visible: {_runnerVisible}");
    }

    #endregion

    #region Game Logic

    /// <summary>
    /// Behaviour used when this bot is the runner: flee from nearby hunters,
    /// otherwise patrol between corners of the arena.
    /// </summary>
    void RunnerBrain()
    {
        Vector3 fleeDirection = Vector3.zero;
        bool hunterNearby = false;

        // Check distance to every other bot (i.e. every hunter) to see if we need to flee
        foreach (var kvp in _botPositions)
        {
            if (kvp.Key == _runnerId) continue;

            if (Vector3.Distance(transform.position, kvp.Value) < HunterFleeRadius)
            {
                hunterNearby = true;
                fleeDirection += transform.position - kvp.Value;
            }
        }

        if (hunterNearby)
        {
            // Cancel any pending corner-patrol timer so we re-roll a fresh corner once we escape
            _lastCornerChangeTime = 0f;

            // Blend the flee direction with a pull toward the arena center,
            // so the runner doesn't just run straight into a wall/corner
            Vector3 toCenter = new Vector3(5f, 0f, 5f) - transform.position;
            Vector3 blended = (fleeDirection.normalized + toCenter.normalized * 0.8f).normalized;

            // Smooth the flee direction over time instead of snapping straight to the
            // new direction every tick. A raw frame-to-frame jump here was causing
            // driveTo's angle gate to trigger constantly, freezing the runner in place
            // to re-rotate instead of actually fleeing.
            _smoothedFleeDir = Vector3.Lerp(_smoothedFleeDir, blended, Time.fixedDeltaTime * 5f).normalized;

            driveTo(nextPoint(transform.position + _smoothedFleeDir * HunterFleeRadius));
            return;
        }

        // No hunters nearby: patrol corners, picking a new one when the timer expires
        bool timerExpired = (Time.time - _lastCornerChangeTime) > CornerChangeInterval;

        if (_currentCornerIndex < 0 || timerExpired)
        {
            // Pick a new corner that isn't the one we just came from
            int newIndex;
            do { newIndex = Random.Range(0, _corners.Length); }
            while (newIndex == _currentCornerIndex);

            _currentCornerIndex = newIndex;
            _lastCornerChangeTime = Time.time;
            Debug.Log($"[RobotBrain:{id}] Runner → corner {_currentCornerIndex}: {_corners[_currentCornerIndex]}");
        }

        driveTo(nextPoint(_corners[_currentCornerIndex]));
    }

    /// <summary>
    /// Behaviour used when this bot is a hunter: chase the runner if visible,
    /// otherwise roam randomly looking for it. Idle for the first couple of
    /// seconds of a round to give the runner a head start.
    /// </summary>
    void HunterBrain()
    {
        if (Time.time - _startTime < 2f) return;

        if (_runnerVisible)
            driveTo(nextPoint(_runnerPos));
        else
            Roam();
    }

    /// <summary>
    /// Picks a new random point to wander towards once the current one is reached,
    /// used by hunters that have lost sight of the runner.
    /// </summary>
    void Roam()
    {
        if (!_hasRoamTarget || Vector3.Distance(transform.position, _roamTarget) < 0.5f)
        {
            _roamTarget = new Vector3(Random.Range(1f, 9f), 0f, Random.Range(1f, 9f));
            _hasRoamTarget = true;
            Debug.Log($"[RobotBrain:{id}] New roam target: {_roamTarget}");
        }

        driveTo(nextPoint(_roamTarget));
    }

    #endregion

    #region Movement

    /// <summary>
    /// Computes the next point to drive towards using a potential-field approach:
    /// attraction to the target, repulsion from other robots and walls, plus jitter
    /// to avoid the bot getting stuck in local force equilibria.
    /// </summary>
    /// <param name="target">Desired goal position, or null for no specific goal.</param>
    Vector3 nextPoint(Vector3? target)
    {
        Vector3 pos = transform.position;
        Vector3 force = Vector3.zero;

        // 1. Goal attraction — pull towards the target, or stop if already close enough
        if (target.HasValue)
        {
            Vector3 toTarget = target.Value - pos;
            toTarget.y = 0;

            if (toTarget.magnitude > MinDistanceToGoal)
            {
                force += toTarget.normalized * GoalAttractionStrength;
            }
            else
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return pos;
            }
        }

        // 2. Robot repulsion — push away from nearby bots (ignoring self and the runner,
        //    since the runner is handled separately by flee/chase logic)
        foreach (var kvp in _botPositions)
        {
            if (kvp.Key == id || kvp.Key == _runnerId) continue;

            Vector3 away = pos - kvp.Value;
            away.y = 0;
            float dist = away.magnitude;

            if (dist > RobotInfluenceRadius) continue;

            float safeDist = Mathf.Max(dist, MinRobotSeparation); // avoid divide-by-near-zero
            force += away.normalized * (RobotRepulsionStrength / safeDist);
        }

        // 3. Wall repulsion — push back in whenever close to one of the arena's four boundaries
        const float FIELD_MIN = 0f, FIELD_MAX = 10f;

        float dxMin = pos.x - FIELD_MIN, dxMax = FIELD_MAX - pos.x;
        float dzMin = pos.z - FIELD_MIN, dzMax = FIELD_MAX - pos.z;

        if (dxMin < WallSafetyMargin) force += Vector3.right * (WallRepulsionStrength / Mathf.Max(0.001f, dxMin * dxMin));
        if (dxMax < WallSafetyMargin) force += Vector3.left * (WallRepulsionStrength / Mathf.Max(0.001f, dxMax * dxMax));
        if (dzMin < WallSafetyMargin) force += Vector3.forward * (WallRepulsionStrength / Mathf.Max(0.001f, dzMin * dzMin));
        if (dzMax < WallSafetyMargin) force += Vector3.back * (WallRepulsionStrength / Mathf.Max(0.001f, dzMax * dzMax));

        // 4. Jitter — small periodic random nudge so bots don't get stuck if forces cancel out exactly
        if (Time.time - _lastJitterTime > JitterInterval)
        {
            Vector2 rand = Random.insideUnitCircle;
            _currentJitter = new Vector3(rand.x, 0, rand.y) * JitterStrength;
            _lastJitterTime = Time.time;
        }
        force += _currentJitter;

        // 5. Fallback — if everything still cancels out to (near) zero, just keep moving forward
        //    rather than freezing in place
        if (force.sqrMagnitude < 0.001f)
            force = transform.forward;

        return pos + force.normalized;
    }

    /// <summary>
    /// Drives the Rigidbody towards a target point: rotates to face it, and only
    /// moves forward once roughly facing the right way (avoids strafing/sliding).
    /// </summary>
    void driveTo(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;

        // Already at the target, nothing to do
        if (dir.sqrMagnitude < MinDistanceToGoal * MinDistanceToGoal) return;

        float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(transform.eulerAngles.y, targetAngle);

        // Turn towards the target, clamped to a max turn rate
        _rb.angularVelocity = new Vector3(0, Mathf.Clamp(angleDiff * 0.1f, -3f, 3f), 0);

        // Scale forward speed by how well-aligned we are with the target instead of a
        // hard on/off cutoff. This keeps the bot moving (just slower) while it's turning
        // to face a new target, rather than stopping dead every time the target direction
        // changes — which was especially noticeable when the runner's flee target shifted.
        float absAngle = Mathf.Abs(angleDiff);
        float alignment = Mathf.Clamp01(1f - absAngle / 90f); // 1 = facing target, 0 = facing 90°+ away

        Vector3 forwardFlat = new Vector3(transform.forward.x, 0, transform.forward.z);
        _rb.linearVelocity = forwardFlat * MaxSpeed * alignment;
    }

    /// <summary>
    /// Shows or hides the green front-facing line and, if shown, positions it
    /// 3 units ahead of the bot along its current forward direction.
    /// </summary>
    void UpdateFrontLine(bool visible)
    {
        if (_frontLine == null) return;

        _frontLine.enabled = visible;
        if (!visible) return;

        Vector3 start = transform.position;
        Vector3 end = start + transform.forward * FrontLineLength;

        _frontLine.SetPosition(0, start);
        _frontLine.SetPosition(1, end);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resets this bot back to its spawn position and idle state, then signals readiness.
    /// </summary>
    void ResetState()
    {
        _runnerId = -1;
        _runnerVisible = false;
        _hasRoamTarget = false;
        _currentCornerIndex = -1;
        _lastCornerChangeTime = 0f;
        _smoothedFleeDir = Vector3.zero;
        _gameState = GameState.Idle;
        _startTime = 0f;

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.position = new Vector3(_startPos.x, 0f, _startPos.z);

        PublishReady();
    }

    /// <summary>
    /// Publishes this bot's ID on /game/robots/ready to signal it's spawned and ready.
    /// </summary>
    void PublishReady() => _ros.Publish("/game/robots/ready", new Int32Msg { data = id });

    #endregion
}