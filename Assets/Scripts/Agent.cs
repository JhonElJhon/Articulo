using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The complete agent brain.
///
/// Pipeline each tick:
///   1. Decay mood toward neutral (PADState).
///   2. Periodically: UpdatePerception → BeliefSet via OverlapSphere.
///   3. Periodically: SelectIntention via BDI scoring.
///   4. ExecuteCurrentAction via NavMeshAgent + state timer.
///
/// On event received:
///   AppraisalEngine.Appraise → EmotionInstance → UpdateMood → re-evaluate BDI.
/// </summary>
public class Agent : MonoBehaviour
{
    // ── Identity ──────────────────────────────────────────────────────────────
    private static int _totalSpawned = 0;
    public string npcName;
    public int    npcID;
    public Teams  team;

    // ── Personality (OCEAN) ───────────────────────────────────────────────────
    [Header("Personality")]
    public OCEAN_Model.Frames personalityType;
    public OCEAN_Model personality;

    // Cached as doubles to match OCEAN_Model; exposed to Inspector
    public double openness, conscientiousness, extraversion, agreeableness, neuroticism, stability;

    // ── Emotional State ───────────────────────────────────────────────────────
    [Header("Emotional State (OCC)")]
    public string currentEmotionName      = "None";
    public float  currentEmotionIntensity = 0f;
    private EmotionInstance _currentEmotion = EmotionInstance.None;
    private List<string> logs = new List<string>();

    [Header("Telemetry / Logging")]
    private SimEvent _lastEvent;
    private PADValues _previousMood;

    // ── Mood (PAD) ────────────────────────────────────────────────────────────
    [Header("Mood (PAD)")]
    public PADState mood = new PADState();
    public string   currentMoodOctant;

    // ── BDI ───────────────────────────────────────────────────────────────────
    private BDIEngine _bdi;

    [Header("BDI / Action")]
    public AgentActionType currentActionType = AgentActionType.WatchCalmly;
    public AgentActionType CurrentAction => currentActionType;

    // ── Perception ────────────────────────────────────────────────────────────
    [Header("Perception")]
    public float perceptionRadius = 8f;

    // ── Movement ──────────────────────────────────────────────────────────────
    [Header("Movement")]
    public NavMeshAgent navAgent;
    public float reachDistance  = 0.5f;

    private float _baseNavSpeed;

    // ── Action State Machine ──────────────────────────────────────────────────
    private float _actionTimer    = 0f;
    private float _actionDuration = 3f;

    private float _reEvalTimer              = 0f;
    private const float RE_EVAL_INTERVAL    = 1.2f;  // seconds between BDI re-evaluations

    private float _fightBroadcastCooldown   = 0f;
    private const float FIGHT_BCAST_INTERVAL = 2.0f;

    // ── Field reference ───────────────────────────────────────────────────────
    // Tag a GameObject "FieldCenter" in your scene for BOO/CHANT face-direction
    // and PitchInvasion destination.
    private Transform _fieldCenter;

    // ── Animator ───────────────────────────────────────────────────────
    private Animator animator;
    private bool isWalking = false;
    private bool isRunning = false;
    private bool isFighting = false;
    private bool _actionAnimTriggered = false;
    private bool _inSingleUseAnimation = false;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGlobalEvent += ReceiveGlobalEvent;
            GameManager.Instance.ExecuteAction += ExecuteLocalAction;
        }
            
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGlobalEvent -= ReceiveGlobalEvent;
            GameManager.Instance.ExecuteAction -= ExecuteLocalAction;
        }

    }

    void Start()
    {
        _totalSpawned++;
        npcID   = _totalSpawned;
        npcName = "NPC_" + npcID;
        gameObject.name = npcName;

        // Cache field center
        GameObject fc = GameObject.FindWithTag("FieldCenter");
        if (fc != null) _fieldCenter = fc.transform;
        FaceTarget(_fieldCenter);

        if (navAgent != null)
            _baseNavSpeed = navAgent.speed;

        // Add random stagger to BDI timer to prevent spikes
        _reEvalTimer = Random.Range(0f, RE_EVAL_INTERVAL);

        animator = GetComponent<Animator>();   
    }

    void Update()
    {
        if (mood == null) return;

        // Drive the current action first
        ExecuteCurrentAction();
        /*
        // Terminal states logic: stop updating BDI and Mood once locked in
        if (currentActionType == AgentActionType.Run || 
            currentActionType == AgentActionType.Fight || 
            currentActionType == AgentActionType.PitchInvasion)
        {
            return;
        }*/

        // Decay mood every frame
        mood.Decay((float)stability, Time.deltaTime);
        currentMoodOctant = mood.GetOctantName();

        // Cooldown timers
        _fightBroadcastCooldown -= Time.deltaTime;

        // Periodic perception + BDI re-evaluation (internal mood drift)
        _reEvalTimer += Time.deltaTime;
        if (_reEvalTimer >= RE_EVAL_INTERVAL)
        {
            _reEvalTimer = 0f;
            UpdatePerception();
            ReEvaluateIntention(false); // Evaluate and change state, but DO NOT submit event to GM yet
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event handling
    // ─────────────────────────────────────────────────────────────────────────

    public void ReceiveGlobalEvent(SimEvent evt)
    {
        ProcessEvent(evt);
        // Do not call ReEvaluateIntention here. This is the listening phase.
    }

    public void ReceiveLocalEvent(SimEvent evt)  => ProcessEvent(evt);
    
    public void ExecuteLocalAction() 
    {
        UpdatePerception();
        ReEvaluateIntention(true); // Forces submission for the new iteration
    }

    private void ProcessEvent(SimEvent evt)
    {
        // Don't react to events you yourself caused
        if (evt.instigator == this) return;

        /*
        // Terminal states logic: stop reacting to events once locked in
        if (currentActionType == AgentActionType.Run || 
            currentActionType == AgentActionType.Fight || 
            currentActionType == AgentActionType.PitchInvasion)
        {
            return;
        }
        */
        EmotionInstance felt = AppraisalEngine.Appraise(evt, this);
        if (felt.emotion == OCCEmotion.None) return;

        // Cache state BEFORE update for telemetry

        if (evt.eventId == "teamScores" && this.team != evt.instigatingTeam) evt.eventId = "enemyScores";
        if (evt.eventId == "nearMiss")
        {
            if (evt.instigatingTeam == this.team) evt.eventId = "teamMisses";
            else evt.eventId = "enemyMisses";
        }
        if (evt.eventId == "controversialCall")
        {
            if (evt.instigatingTeam == this.team) evt.eventId = "controversialCall-Team";
            else evt.eventId = "controversialCall-Enemy";
        }

        _lastEvent = evt;
        _previousMood = mood.Values;

        _currentEmotion           = felt;
        currentEmotionName        = felt.emotion.ToString();
        currentEmotionIntensity   = felt.intensity;

        // Update mood
        mood.UpdateFromEmotion(felt, (float)neuroticism);

        // For debugging
        string messageLog = "Agent percieved event: " + evt.eventId + "\n";
        messageLog += "Agent felt: " + currentEmotionName + " with intensity: " + currentEmotionIntensity + "\n";
        messageLog += "Agent shifted mood to: " + mood.ToString() + "\n";
        //logs.Add(messageLog);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Perception + BDI
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdatePerception()
    {
        if (_bdi == null) return;

        // Provide game-level beliefs from GameManager
        if (GameManager.Instance != null)
        {
            int raw = GameManager.Instance.gameScore;
            _bdi.Beliefs.gameScoreFromMyTeam = team == Teams.Red ? raw : -raw;
        }

        // Field proximity (radius 15 — adjust to your stadium scale)
        _bdi.Beliefs.isNearField = _fieldCenter != null &&
            Vector3.Distance(transform.position, _fieldCenter.position) < 15f;

        _bdi.UpdateBeliefs(perceptionRadius);
    }

    private void ReEvaluateIntention(bool submitToGM)
    {
        if (_bdi == null) return;

        // If no emotion yet, infer from mood to avoid agents being frozen
        EmotionInstance effective = _currentEmotion;
        if (effective.emotion == OCCEmotion.None)
        {
            OCCEmotion inferred  = OCCEmotionData.FindClosest(mood.Values);
            float      magnitude = mood.Values.Magnitude;
            effective = new EmotionInstance
            {
                emotion   = inferred,
                intensity = magnitude * 0.5f,
                pad       = mood.Values
            };
        }

        AgentActionType chosen = _bdi.SelectIntention(effective, mood);
        
        if (chosen != currentActionType || submitToGM)
        {
            logs.Add("Agent will perform action: " + chosen.ToString() + "\n");
            StartAction(chosen);
        }

        if (submitToGM)
        {
            // Submit complete data row to CSV Logger!
            DataLogger.Instance.LogReaction(
                npcID, npcName, team.ToString(), personalityType,
                openness, conscientiousness, extraversion, agreeableness, neuroticism, stability,
                _previousMood,
                string.IsNullOrEmpty(_lastEvent.eventId) ? "None" : _lastEvent.eventId,
                _currentEmotion.emotion.ToString(),
                _currentEmotion.intensity,
                mood.Values,
                chosen.ToString(),
                GameManager.Instance.crono
            );

            SubmitReactionToGameManager();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Action state machine
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Transitions to a new action and configures NavMesh accordingly.</summary>
    private void StartAction(AgentActionType action)
    {
        bool sameAction = (action == currentActionType);

        if (navAgent == null || !navAgent.isOnNavMesh) return;

        if (sameAction)
        {
            // Re-trigger instantaneous animations if BDI chose them again during an event evaluation
            if (action == AgentActionType.Celebrate) { animator.CrossFadeInFixedTime("Cheering", 0.1f); _inSingleUseAnimation = true; }
            else if (action == AgentActionType.Chant) { animator.CrossFadeInFixedTime("Excited", 0.1f); _inSingleUseAnimation = true; }
            else if (action == AgentActionType.Boo) { animator.CrossFadeInFixedTime("Disappointed", 0.1f); _inSingleUseAnimation = true; }
            else if (action == AgentActionType.ThrowObject) { animator.CrossFadeInFixedTime("Throw Object", 0.1f); _inSingleUseAnimation = true; }
            else if (action == AgentActionType.ComfortAlly || action == AgentActionType.CalmSituation) 
            { 
                _actionTimer = 0f;
                _actionAnimTriggered = false;
                isWalking = true;
            }
            return;
        }

        currentActionType = action;
        _actionTimer      = 0f;
        _actionAnimTriggered = false;

        // Prev action. If agent is fighting or running, must continue fighting until BDI stops him.
        if (currentActionType != AgentActionType.Fight) isFighting = false;
        if (currentActionType != AgentActionType.Run) isRunning = false;
        isWalking = false;

        if (_inSingleUseAnimation)
        {
            animator.CrossFadeInFixedTime("Idle", 0.1f);
            _inSingleUseAnimation = false;
        }

        switch (action)
        {
            case AgentActionType.WatchCalmly:
                navAgent.speed = _baseNavSpeed;
                navAgent.ResetPath();
                break;

            case AgentActionType.Celebrate:
                animator.CrossFadeInFixedTime("Cheering", 0.1f);
                _inSingleUseAnimation = true;
                navAgent.speed = _baseNavSpeed;
                navAgent.ResetPath();
                break;

            case AgentActionType.Chant:
                navAgent.speed = _baseNavSpeed;
                navAgent.ResetPath();
                FaceTarget(_fieldCenter);
                animator.CrossFadeInFixedTime("Excited", 0.1f);
                _inSingleUseAnimation = true;
                break;

            case AgentActionType.Boo:
                navAgent.speed = _baseNavSpeed;
                navAgent.ResetPath();
                FaceTarget(_fieldCenter);
                animator.CrossFadeInFixedTime("Disappointed", 0.1f);
                _inSingleUseAnimation = true;
                break;

            case AgentActionType.Insult:
                navAgent.speed = _baseNavSpeed;
                Agent enemy = _bdi?.Beliefs.nearestEnemy;
                if (enemy != null)
                {
                    navAgent.SetDestination(enemy.transform.position);
                }
                isWalking = true;
                _actionDuration = 1f;
                break;

            case AgentActionType.Fight:
                navAgent.speed = _baseNavSpeed * 1.2f;
                Agent fightTarget = _bdi?.Beliefs.nearestEnemy;
                if (fightTarget != null)
                {
                    navAgent.SetDestination(fightTarget.transform.position);
                }
                isWalking = true;
                _actionDuration = 1f;
                break;

            case AgentActionType.Run:
                navAgent.speed = _baseNavSpeed * 2.5f;
                isRunning = true;
                FleeFromThreat();
                break;

            case AgentActionType.ComfortAlly:
                navAgent.speed = _baseNavSpeed;
                Agent sad = _bdi?.Beliefs.nearestDistressedAlly;
                if (sad != null)
                {
                    navAgent.SetDestination(sad.transform.position);
                }
                _actionDuration = 1f;
                isWalking = true;
                break;

            case AgentActionType.CalmSituation:
                navAgent.speed = _baseNavSpeed * 1.1f;
                Agent aggressive = _bdi?.Beliefs.nearestEnemy;
                if (aggressive != null)
                {
                    navAgent.SetDestination(aggressive.transform.position);
                }
                _actionDuration = 1f;
                isWalking = true;
                break;

            case AgentActionType.FormGroup:
                navAgent.speed = _baseNavSpeed;
                if (_bdi?.Beliefs.nearestAlly != null)
                    navAgent.SetDestination(_bdi.Beliefs.nearestAlly.transform.position);
                _actionDuration = 1f;
                isWalking = true;
                break;

            case AgentActionType.PitchInvasion:
                navAgent.speed = _baseNavSpeed * 2.5f;
                if (_fieldCenter != null) navAgent.SetDestination(_fieldCenter.position);
                isRunning = true;
                break;

            case AgentActionType.ThrowObject:
                navAgent.ResetPath();
                FaceTarget(_fieldCenter);
                animator.CrossFadeInFixedTime("Throw Object", 0.1f);
                _inSingleUseAnimation = true;
                break;
        }
    }

    /// <summary>Called every Update — drives movement logic and action timers.</summary>
    private void ExecuteCurrentAction()
    {
        _actionTimer += Time.deltaTime;
        animator.SetBool("isWalking", isWalking);
        animator.SetBool("isFighting", isFighting);
        animator.SetBool("isRunning", isRunning);

        if (_inSingleUseAnimation)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            
            // List of states that should return to Idle after one play (Yelling and Fighting excluded so they loop)
            if (!animator.IsInTransition(0) && 
                (stateInfo.IsName("Cheering") || stateInfo.IsName("Excited") || 
                 stateInfo.IsName("Disappointed") || stateInfo.IsName("Throw Object") ||
                 stateInfo.IsName("Talking_ally") || stateInfo.IsName("Talking_enemy")))
            {
                if (stateInfo.normalizedTime >= 0.95f)
                {
                    animator.CrossFadeInFixedTime("Idle", 0.1f);
                    _inSingleUseAnimation = false;
                }
            }
        }

        switch (currentActionType)
        {
            case AgentActionType.WatchCalmly:
                // Stand still
                break;

            case AgentActionType.Celebrate:
                break;

            case AgentActionType.Chant:
                break;

            case AgentActionType.Boo:
                break;

            case AgentActionType.ThrowObject:
                break;

            case AgentActionType.Insult:
                // Keep tracking the enemy
                if (_bdi?.Beliefs.nearestEnemy != null && !_actionAnimTriggered)
                    navAgent.SetDestination(_bdi.Beliefs.nearestEnemy.transform.position);
                // Should walk to enemy
                if (_actionTimer > _actionDuration && !_actionAnimTriggered)
                {
                    navAgent.speed = _baseNavSpeed;
                    navAgent.ResetPath();
                    isWalking = false;
                    animator.CrossFadeInFixedTime("Yelling", 0.1f);
                    _inSingleUseAnimation = true;
                    _actionAnimTriggered = true;
                }
                break;

            case AgentActionType.Fight:
                HandleFighting();
                break;

            case AgentActionType.Run:
                break;

            case AgentActionType.ComfortAlly:
                if (_bdi?.Beliefs.nearestDistressedAlly != null && !_actionAnimTriggered)
                    navAgent.SetDestination(_bdi.Beliefs.nearestDistressedAlly.transform.position);
                    
                if (_actionTimer > _actionDuration && !_actionAnimTriggered)
                {
                    isWalking = false;
                    animator.CrossFadeInFixedTime("Talking_ally", 0.1f);
                    _inSingleUseAnimation = true;
                    _actionAnimTriggered = true;
                    navAgent.speed = _baseNavSpeed;
                    navAgent.ResetPath();
                }
                break;
            case AgentActionType.CalmSituation:
                if (_bdi?.Beliefs.nearestEnemy != null && !_actionAnimTriggered)
                    navAgent.SetDestination(_bdi.Beliefs.nearestEnemy.transform.position);
                    
                if (_actionTimer > _actionDuration && !_actionAnimTriggered)
                {
                    isWalking = false;
                    animator.CrossFadeInFixedTime("Talking_enemy", 0.1f);
                    _inSingleUseAnimation = true;
                    _actionAnimTriggered = true;
                    navAgent.speed = _baseNavSpeed;
                    navAgent.ResetPath();
                }
                break;
            case AgentActionType.FormGroup:
                if (_bdi?.Beliefs.nearestAlly != null && !_actionAnimTriggered)
                    navAgent.SetDestination(_bdi.Beliefs.nearestAlly.transform.position);
                    
                if (_actionTimer > _actionDuration && !_actionAnimTriggered)
                {
                    isWalking = false;
                    navAgent.speed = _baseNavSpeed;
                    navAgent.ResetPath();
                    _actionAnimTriggered = true;
                }
                break;

            case AgentActionType.PitchInvasion:
                // Keep heading to field
                if (_fieldCenter != null && navAgent.isOnNavMesh &&
                    !navAgent.pathPending && navAgent.remainingDistance <= reachDistance)
                    navAgent.SetDestination(_fieldCenter.position);
                break;
        }

    }

    private void HandleFighting()
    {
        Agent foe = _bdi?.Beliefs.nearestEnemy;
        if (foe == null)
        {
            navAgent.speed = _baseNavSpeed;
            navAgent.ResetPath();
            return; 
        }

        if (!_actionAnimTriggered)
            navAgent.SetDestination(foe.transform.position);

        /*
        // When close enough, trigger the fight event for nearby agents
        float dist = Vector3.Distance(transform.position, foe.transform.position);
        if (dist < 1.5f && _fightBroadcastCooldown <= 0f)
        {
            // Removed: agents should not auto-broadcast outside managed iterations
            // BroadcastLocal(SimEvent.Fight(this, foe));
            _fightBroadcastCooldown = FIGHT_BCAST_INTERVAL;
        }
        */

        // Stop fighting eventually when timer is up (or when mood naturally forces BDI state switch)
        if (_actionTimer > _actionDuration)
        {
            isWalking = false;
            isFighting = true;
            if (!_actionAnimTriggered)
            {
                navAgent.ResetPath();
                _actionAnimTriggered = true;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper methods
    // ─────────────────────────────────────────────────────────────────────────

    private void FleeFromThreat()
    {
        if (GameManager.Instance != null && GameManager.Instance.exitPoints != null && GameManager.Instance.exitPoints.Length > 0)
        {
            Transform bestExit = null;
            float bestDist = float.MaxValue;
            foreach (Transform ext in GameManager.Instance.exitPoints)
            {
                float d = Vector3.Distance(transform.position, ext.position);
                if (d < bestDist) { bestDist = d; bestExit = ext; }
            }
            if (bestExit != null)
            {
                navAgent.SetDestination(bestExit.position);
                return;
            }
        }

        // Fallback
        Vector3 threatPos = _bdi?.Beliefs.nearestEnemy?.transform.position ?? transform.position;
        Vector3 fleeDir   = (transform.position - threatPos).normalized;
        if (fleeDir.sqrMagnitude < 0.01f) fleeDir = Random.onUnitSphere;
        fleeDir.y = 0f;
        navAgent.SetDestination(transform.position + fleeDir * 20f);
    }

    private void FaceTarget(Transform target)
    {
        if (target == null) return;
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    private void SubmitReactionToGameManager()
    {
        SimEvent evt = SimEvent.WatchCalmly(this);
        switch (currentActionType)
        {
            case AgentActionType.WatchCalmly:  evt = SimEvent.WatchCalmly(this); break;
            case AgentActionType.Celebrate:    evt = SimEvent.Celebration(this); break;
            case AgentActionType.Chant:        evt = SimEvent.Chant(this); break;
            case AgentActionType.Boo:          evt = SimEvent.Boo(this); break;
            case AgentActionType.Insult:       
                Agent enemy = _bdi?.Beliefs.nearestEnemy;
                evt = SimEvent.Insult(this, enemy != null ? enemy : this); 
                break;
            case AgentActionType.Fight:        
                Agent foe = _bdi?.Beliefs.nearestEnemy;
                evt = SimEvent.Fight(this, foe != null ? foe : this); 
                break;
            case AgentActionType.Run:          evt = SimEvent.Run(this); break;
            case AgentActionType.ComfortAlly:  
                Agent sad = _bdi?.Beliefs.nearestDistressedAlly;
                evt = SimEvent.ComfortAlly(this, sad != null ? sad : this); 
                break;
            case AgentActionType.CalmSituation:
                Agent aggressive = _bdi?.Beliefs.nearestEnemy;
                evt = SimEvent.CalmSituation(this, aggressive != null ? aggressive : this); 
                break;
            case AgentActionType.FormGroup:    evt = SimEvent.FormGroup(this); break;
            case AgentActionType.PitchInvasion:evt = SimEvent.PitchInvasion(this); break;
            case AgentActionType.ThrowObject:  evt = SimEvent.ThrowObject(this); break;
        }

        float radius = (currentActionType == AgentActionType.PitchInvasion) ? perceptionRadius * 5f : perceptionRadius;
        GameManager.Instance?.BroadcastLocalEvent(evt, transform.position, radius);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Setup — called by GameManager after Instantiate
    // ─────────────────────────────────────────────────────────────────────────

    public void SetPersonality(OCEAN_Model.Frames frame)
    {
        OCEAN_Model.Frames pers;
        if(frame == OCEAN_Model.Frames.Random)
        {
            pers = (OCEAN_Model.Frames)Random.Range(1, 21);
        }
        else
        {
            pers = frame;
        }
        personality     = OCEAN_Model.GenerateByFrame(pers);
        personalityType = pers;

        openness          = personality.Openness;
        conscientiousness = personality.Conscientiousness;
        extraversion      = personality.Extraversion;
        agreeableness     = personality.Agreeableness;
        neuroticism       = personality.Neuroticism;
        stability         = personality.Stability;

        _bdi = new BDIEngine(this);
        _bdi.Initialize(personality);
    }

    public void SetTeam(Teams newTeam)
    {
        team = newTeam == Teams.Random
            ? (Random.Range(0, 2) == 0 ? Teams.Red : Teams.Blue)
            : newTeam;

        Color teamColour = team == Teams.Red
            ? new Color(1.00f, 0.20f, 0.20f)   // red
            : new Color(0.20f, 0.45f, 1.00f);  // blue
        Color blackColour = new Color(0.05f, 0.05f, 0.05f);
        Color defaultColour = new Color(0.6f, 0.5f, 0.4f); // neutral skin tone

        var colourMap = new Dictionary<string, Color>
        {
            { "Chest", teamColour  },
            { "Legs",  blackColour },
            { "Feet",  blackColour },
        };

        // Walk every SkinnedMeshRenderer in the hierarchy.
        // GetComponentsInChildren is recursive, so it finds all depths.
        foreach (SkinnedMeshRenderer smr in
                 GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            var props = new MaterialPropertyBlock();
            // Read whatever the renderer already has so we only override _BaseColor.
            smr.GetPropertyBlock(props);

            if (smr.gameObject.name == "Underwear") smr.gameObject.SetActive(false);

            if (colourMap.TryGetValue(smr.gameObject.name, out Color c))
            {
                props.SetColor("_BaseColor", c);
            }
            // If the part is NOT in the map we leave props as-is (no tint applied).

            smr.SetPropertyBlock(props);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Debug / Inspector helpers
    // ─────────────────────────────────────────────────────────────────────────

    public void MoveToDestination(Transform dest) { if (navAgent.isOnNavMesh) navAgent.SetDestination(dest.position); }
    public void MoveToDestination(Vector3 point)  { if (navAgent.isOnNavMesh) navAgent.SetDestination(point); }

    public string PrintValues()
    {
        string beliefs = _bdi != null
            ? $"Safety:{_bdi.Beliefs.safetyLevel:F2} " +
              $"Allies:{_bdi.Beliefs.nearbyAllyCount} " +
              $"Enemies:{_bdi.Beliefs.nearbyEnemyCount} " +
              $"Score:{_bdi.Beliefs.gameScoreFromMyTeam}"
            : "(BDI not initialised)";
        string _logs = "";
        foreach(string log in logs)
        {
            _logs += log;
        }

        return $"=== {npcName} [{team}] ===\n" +
               $"Frame : {personalityType}\n" +
               $"O:{openness:F2} C:{conscientiousness:F2} E:{extraversion:F2} " +
               $"A:{agreeableness:F2} N:{neuroticism:F2} S:{stability:F2}\n" +
               $"Emotion: {currentEmotionName} (x{currentEmotionIntensity:F2})\n" +
               $"Mood  : {mood}\n" +
               $"Action: {currentActionType}\n" +
               $"Beliefs: {beliefs}\n" +
               $"Emotion/Action logs: {_logs}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    public enum Teams { Blue, Red, Random }
}
