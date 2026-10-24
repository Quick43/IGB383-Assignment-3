using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Panda;
using UnityEngine.SocialPlatforms.Impl;

public class MechAIDecisions : MechAI {

    public string botName = "Test Bot";

    //Links to Mech AI Systems
    MechSystems mechSystem;
    MechAIMovement mechAIMovement;
    MechAIAiming mechAIAiming;
    MechAIWeapons mechAIWeapons;

    //FSM State Implementation
    public enum MechStates {
        Roam,
        Attack,
        Pursue,
        Flee
    }
    public MechStates mechState;

    //Roam Variables
    public GameObject[] patrolPoints;
    private int patrolIndex = 0;
    public GameObject[] aimTargets;

    //Attack Variables
    private GameObject attackTarget;
    private float attackTime = 3.5f;
    private float attackTimer;

    //Pursue Variables
    public GameObject pursueTarget;
    private Vector3 pursuePoint;

    //Flee Variables
    public GameObject fleeTarget;

    
    // Use this for initialization
    void Start () {
        //Collect Mech and AI Systems
        mechSystem = GetComponent<MechSystems>();
        mechAIMovement = GetComponent<MechAIMovement>();
        mechAIAiming = GetComponent<MechAIAiming>();
        mechAIWeapons = GetComponent<MechAIWeapons>();

        //Roam State Startup Declarations
        patrolPoints = GameObject.FindGameObjectsWithTag("Patrol Point");
        patrolIndex = Random.Range(0, patrolPoints.Length - 1);
        mechAIMovement.Movement(patrolPoints[patrolIndex].transform.position, 1);
    }

    // Update is called once per frame
    void Update() 
    {
        mechAIAiming.FrustumCheck();
        UpdateTargetDetection();
        UpdatePreyMemory();
        UpdateCombatTactic();
        UpdateDecisions();
        if(attackTarget == null)
            mechAIWeapons.laserBeamAI = false;
    }

    // Detection Times
    [SerializeField] private float minDetectionTime = 0.15f;
    [SerializeField] private float maxDetectionTime = 0.45f;

    // Detection logic
    private GameObject pendingTarget;
    private float detectionTimer;

    // Method to update the current target
    private void UpdateTargetDetection()
    {
        if(attackTarget != null)
            return;
        
        // Get the closest target that the mech can see
        GameObject visibleTarget = mechAIAiming.ClosestTarget(mechAIAiming.currentTargets);
        // If the mech can not see any targets, reset detection timer
        if(visibleTarget == null)
        {
            pendingTarget = null;
            detectionTimer = 0f;
            return;
        }
        // If the new clostest target does not match the currently tracked target, change the tracked target
        if(visibleTarget != pendingTarget)
        {
            pendingTarget = visibleTarget;
            detectionTimer = Time.time + Random.Range(minDetectionTime, maxDetectionTime);
        }
        // After the detection time is up, attack the tracked target
        if(Time.time >= detectionTimer)
        {
            attackTarget = pendingTarget;
            pendingTarget = null;
        }
    }

    // Decision switching log
    [SerializeField] private float minDecisionInterval = 0.25f;
    [SerializeField] private float maxDecisionInterval = 0.65f;
    private float nextDecisionTime;
    private bool shouldFlee;
    
    // Evaluate the status of the mech only at slight intervals, adds an aspect of reaction time
    private void UpdateDecisions()
    {
        if(Time.time < nextDecisionTime)
            return;
        nextDecisionTime = Time.time + Random.Range(minDecisionInterval, maxDecisionInterval);
        shouldFlee = EvaluateStatus();
    }

    private bool EvaluateStatus()
    {
        float status = mechSystem.health + mechSystem.energy + (mechSystem.shells * 7) + (mechSystem.missiles * 10);

        return status <= 1500f;
    }

    [Task]
    bool HasAttackTarget()
    {
        return attackTarget != null && (mechAIAiming.LineOfSight(attackTarget) || Time.time - lastSeenTime < targetMemoryDuration);
    }

    [Task]
    bool TargetLOS()
    {
        if(mechAIAiming.LineOfSight(attackTarget))
            return true;
        else
            return false;
    }

    //FSM Behaviour: Roam - Roam between random patrol points
    [Task]
    private void Roam() {
        //Move towards random patrol point
        if (Vector3.Distance(transform.position, patrolPoints[patrolIndex].transform.position) <= 2.0f) {
            patrolIndex = Random.Range(0, patrolPoints.Length);
        } else {
            mechAIMovement.Movement(patrolPoints[patrolIndex].transform.position, 1);
        }
        //Look at random patrol points
        mechAIAiming.RandomAimTarget(patrolPoints);
    }

    private Vector3 lastKnownPosition;
    private float lastSeenTime;
    [SerializeField] private float targetMemoryDuration = 8f;

    private void UpdateTargetMemory()
    {
        if(attackTarget == null)
            return;
        
        if(mechAIAiming.LineOfSight(attackTarget))
        {
            lastKnownPosition = attackTarget.transform.position;
            lastSeenTime = Time.time;
        }
    }

    // Combat Tactic Logic
    public enum CombatTactic
    {
        Approach,
        Strafe,
        Retreat,
        Reposition,
        Hold
    }

    private CombatTactic currentTactic;
    [SerializeField] private float maxHealth = 2000f;
    [SerializeField] private float preferredCombatDistance = 35f;
    [SerializeField] private float minTacticInterval = 1.5f;
    [SerializeField] private float maxTacticInterval = 3.5f;
    private float nextTacticDecision;

    private void UpdateCombatTactic()
    {
        if(Time.time < nextTacticDecision || attackTarget == null)
            return;
        
        nextTacticDecision = Time.time + Random.Range(minTacticInterval, maxTacticInterval);

        float distance = Vector3.Distance(transform.position, attackTarget.transform.position);

        float health = Mathf.Clamp01(mechSystem.health / maxHealth);
        float damage = 1f - health;

        float tooClose = 1f - Mathf.InverseLerp(15f, 35f, distance);
        float tooFar = Mathf.InverseLerp(35f, 75f, distance);

        float goodRange = 1f - Mathf.Clamp01(Mathf.Abs(distance - preferredCombatDistance) / 35f);

        float approachScore = distance > 40f ? Mathf.InverseLerp(40f, 75f, distance) * (0.5f + health * 0.5f) : 0f;
        float retreatScore = tooClose * 0.4f + damage * 0.6f;
        float strafeScore = goodRange * 0.4f + health * 0.6f;
        float repositionScore = damage * 0.55f + goodRange * 0.25f + tooClose * 0.20f;
        float holdScore = goodRange * 0.65f + health * 0.35f;

        float bestScore = float.NegativeInfinity;
        CombatTactic bestTactic = currentTactic;

        EvaluateTactic(CombatTactic.Approach, approachScore, ref bestScore, ref bestTactic);
        EvaluateTactic(CombatTactic.Retreat, retreatScore, ref bestScore, ref bestTactic);
        EvaluateTactic(CombatTactic.Strafe, strafeScore, ref bestScore, ref bestTactic);
        EvaluateTactic(CombatTactic.Reposition, repositionScore, ref bestScore, ref bestTactic);
        EvaluateTactic(CombatTactic.Hold, holdScore, ref bestScore, ref bestTactic);

        currentTactic = bestTactic;
    }

    private void EvaluateTactic(CombatTactic tactic, float score, ref float bestScore, ref CombatTactic bestTactic)
    {
        if(score > bestScore)
        {
            bestScore = score;
            bestTactic = tactic;
        }
    }

    [Task]
    void CombatAim()
    {
        if (attackTarget != null && mechAIAiming.LineOfSight(attackTarget))
        {
            // Child object correction - original aiming logic
            if (attackTarget.transform.childCount > 0)
                mechAIAiming.aimTarget = attackTarget.transform.GetChild(0).gameObject;
            else
                mechAIAiming.aimTarget = attackTarget;
            // Remember last known target position
            pursuePoint = attackTarget.transform.position;
        }
        else
            mechAIAiming.RandomAimTarget(patrolPoints);
        Task.current.Succeed();
    }

    [Task]
    bool ShouldApproach()
    {
        return currentTactic == CombatTactic.Approach;
    }

    [Task]
    bool ShouldStrafe()
    {
        return currentTactic == CombatTactic.Strafe;
    }

    [Task]
    bool ShouldRetreat()
    {
        return currentTactic == CombatTactic.Retreat;
    }

    [Task]
    bool ShouldReposition()
    {
        return currentTactic == CombatTactic.Reposition && (isRepositioning || Time.time >= nextRepositionTime);
    }

    [Task]
    void CombatApproach()
    {
        if (attackTarget != null)
            mechAIMovement.Movement(attackTarget.transform.position, 15f);
        Task.current.Succeed();
    }

    [Task]
    void CombatRetreat()
    {
        if (attackTarget != null)
        {
            Vector3 awayDirection = (transform.position - attackTarget.transform.position).normalized;
            Vector3 retreatPosition = transform.position + awayDirection * 20f;
            mechAIMovement.Movement(retreatPosition, 2f);
        }
        Task.current.Succeed();
    }

    private int strafeDirection = 1;
    private float nextStrafeSwitch;
    [SerializeField] private float strafeSwitchInterval = 2f;
    [SerializeField] private float strafeDistance = 15f;

    [Task]
    void CombatStrafe()
    {
        if (attackTarget == null)
        {
            Task.current.Succeed();
            return;
        }

        if (Time.time >= nextStrafeSwitch)
        {
            strafeDirection = Random.value > 0.5f ? 1 : -1;
            nextStrafeSwitch = Time.time + strafeSwitchInterval;
        }

        Vector3 toTarget = attackTarget.transform.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude < 0.01f)
        {
            Task.current.Succeed();
            return;
        }

        Vector3 sideways = Vector3.Cross(Vector3.up, toTarget.normalized);
        Vector3 strafePosition = transform.position + sideways * strafeDirection * strafeDistance;
        mechAIMovement.Movement(strafePosition, 2f);
        Task.current.Succeed();
    }

    [Task]
    void CombatHold()
    {
        Task.current.Succeed();
    }

    // Reposition variables
    [SerializeField] private float repositionDistance = 20f;
    [SerializeField] private float repositionCooldown = 4f;
    [SerializeField] private float repositionTimeout = 3f;

    private Vector3 repositionPoint;
    private float nextRepositionTime;
    private float repositionStartTime;
    private bool isRepositioning;

    [Task]
    void CombatReposition()
    {
        if (attackTarget == null)
        {
            isRepositioning = false;
            Task.current.Succeed();
            return;
        }

        // Choose a new destination only when beginning a reposition
        if (!isRepositioning)
        {
            Vector3 bestPosition = transform.position;
            float bestScore = float.NegativeInfinity;
            float preferredDistance = 35f;

            for (int i = 0; i < 8; i++)
            {
                Vector2 randomDirection = Random.insideUnitCircle.normalized;
                Vector3 candidate = transform.position + new Vector3(randomDirection.x, 0f, randomDirection.y) * repositionDistance;
                float targetDistance = Vector3.Distance(candidate, attackTarget.transform.position);

                // Prefer positions close to ideal combat distance
                float distanceScore = -Mathf.Abs(targetDistance - preferredDistance);

                // Check visibility from the candidate position
                Vector3 targetPoint = attackTarget.transform.position + Vector3.up * 1.5f;
                Vector3 origin = candidate + Vector3.up * 1.5f;
                Vector3 direction = targetPoint - origin;
                bool clearShot = false;
                if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude))
                    clearShot = hit.transform == attackTarget.transform || hit.transform.IsChildOf(attackTarget.transform);

                float visibilityScore = clearShot ? 20f : -20f;
                float totalScore = distanceScore + visibilityScore;
                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestPosition = candidate;
                }
            }

            repositionPoint = bestPosition;
            repositionStartTime = Time.time;
            isRepositioning = true;
            mechAIMovement.Movement(repositionPoint, 2f);
        }

        // Check whether the destination has been reached
        float distanceToPoint = Vector3.Distance(transform.position, repositionPoint);
        bool reachedDestination = distanceToPoint <= 3f;
        bool timedOut = Time.time - repositionStartTime >= repositionTimeout;
        if (reachedDestination || timedOut)
        {
            isRepositioning = false;
            nextRepositionTime = Time.time + repositionCooldown;
            Task.current.Succeed();
        }
    }

    [SerializeField] private float searchDuration = 5f;
    [SerializeField] private float searchRadius = 12f;

    private float preyLastSeenTime = float.NegativeInfinity;
    private float searchStartTime;
    private Vector3 searchPoint;
    private bool isSearching;

    private void UpdatePreyMemory()
    {
        if(attackTarget == null)
            return;

        if(!mechAIAiming.LineOfSight(attackTarget))
            return;
        
        pursuePoint = attackTarget.transform.position;
        lastSeenTime = Time.time;
        isSearching = false;
    }


    //FSM Behaviour: Pursue
    [Task]
    void Pursue() 
    {
        if(attackTarget != null && mechAIAiming.LineOfSight(attackTarget))
        {
            isSearching = false;
            Task.current.Succeed();
            return;
        }

        if(Time.time - preyLastSeenTime > targetMemoryDuration)
        {
            isSearching = false;
            Task.current.Fail();
            return;
        }

        float distanceToLastSeen = Vector3.Distance(transform.position, pursuePoint);

        if(!isSearching && distanceToLastSeen > 5f)
        {
            mechAIMovement.Movement(pursuePoint, 3f);
            Task.current.Succeed();
            return;
        }

        if(!isSearching)
        {
            isSearching = true;
            searchStartTime = Time.time;
            ChooseSearchPoint();
        }

        if(Time.time - searchStartTime >= searchDuration)
        {
            isSearching = false;
            Task.current.Fail();
            return;
        }

        if(Vector3.Distance(transform.position, searchPoint) < 3f)
        {
            ChooseSearchPoint();
        }

        mechAIMovement.Movement(searchPoint, 2f);
        mechAIAiming.RandomAimTarget(patrolPoints);
        Task.current.Succeed();
    }

    private void ChooseSearchPoint()
    {
        Vector2 offset = Random.insideUnitCircle * searchRadius;
        searchPoint = pursuePoint + new Vector3(offset.x, 0f, offset.y);
    }

    [Task]
    bool HasRecentTargetMemory()
    {
        if(isSearching)
            return Time.time - searchStartTime < searchDuration;
        return Time.time - lastSeenTime < targetMemoryDuration;
    }

    [SerializeField] private float fleeDistance = 40.0f;
    [SerializeField] private float fleeDestinationInterval = 2.5f;
    private Vector3 fleePoint;
    private float nextFleeDestinationTime;
    private bool hasFleePoint;

    private void UpdateFleeDestination()
    {
        if(attackTarget == null)
            return;
        
        if(hasFleePoint && Time.time < nextFleeDestinationTime)
            return;

        Vector3 away = transform.position - attackTarget.transform.position;
        away.y = 0f;
        if(away.sqrMagnitude < 0.01f)
            away = -transform.forward;
        away.Normalize();

        float angle = Random.Range(-35f, 35f);
        Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
        fleePoint = transform.position + direction * fleeDistance;
        hasFleePoint = true;
        nextFleeDestinationTime = Time.time + fleeDestinationInterval;
    }

    //FSM Behaviour: Flee
    [Task]
    void Flee() 
    {
        if(attackTarget != null)
        {
            UpdateFleeDestination();
            mechAIMovement.Movement(fleePoint, 3f);

            if(mechAIAiming.LineOfSight(attackTarget))
            {
                mechAIAiming.aimTarget = attackTarget.transform.childCount > 0 ? attackTarget.transform.GetChild(0).gameObject : attackTarget;
            }
            else
            {
                mechAIAiming.RandomAimTarget(patrolPoints);
            }
        }
        else
        {
            mechAIAiming.RandomAimTarget(patrolPoints);
        }
        Task.current.Succeed();
    }

    //Method allowing AI Mech to acquire target after taking damage from enemy
    public override void TakingFire(int origin) {

        //If not own damage and no current attack target, find attack target
        if (origin != mechSystem.ID && !attackTarget) {
            foreach (GameObject target in mechAIAiming.targets) {
                if (target) {
                    if (origin == target.GetComponent<MechSystems>().ID) {
                        attackTarget = target;
                        mechAIAiming.aimTarget = target;
                    }
                }
            }
        }
    }

    //Method for checking heuristic status of Mech to determine if Fleeing is necessary
    [SerializeField] private float fleeHealthThreshold = 0.25f;
    [SerializeField] private float recoverHealthThreshold = 0.40f;
    private bool isFleeing;

    [Task]
    private bool StatusCheck() {

        float health = Mathf.Clamp01(mechSystem.health / maxHealth);
        if(!isFleeing)
        {
            if(health <= fleeHealthThreshold)
                isFleeing = true;
        }
        else
        {
            if(health >= recoverHealthThreshold)
                isFleeing = false;
        }
        return isFleeing;
    }

    // Variables for inaccurate aiming
    [SerializeField] private float aimErrorRadius = 2f;
    [SerializeField] private float aimCorrectionInterval = 0.35f;
    [SerializeField] private float aimTrackingSpeed = 5f;

    private Vector3 currentAimPoint;
    private Vector3 aimOffset;
    private float nextAimCorrection;

    // Method to make the aiming of the mech more inaccurate
    private void UpdateImperfectAim(Transform target)
    {
        if(target == null)
            return;
        
        // Correct the aim slightly every aim correction time
        if(Time.time >= nextAimCorrection)
        {
            aimOffset = Random.insideUnitSphere * aimErrorRadius;
            nextAimCorrection = Time.time + aimCorrectionInterval;
        }
        // Apply the aim offset to the aim vector
        Vector3 desiredAimPoint = target.position + aimOffset;
        currentAimPoint = Vector3.Lerp(currentAimPoint, desiredAimPoint, 1f - Mathf.Exp(-aimTrackingSpeed * Time.deltaTime));
    }

    // Weapon Types
    public enum WeaponType
    {
        None,
        Lasers,
        Cannons,
        Beam,
        Missiles
    }

    // Weapon Firing Modes
    public enum FireMode
    {
        Single,
        Burst,
        Auto
    }

    // Weapon Logic
    private WeaponType selectedEnergyWeapon;
    private bool useCannons;
    private bool useMissiles;
    private float nextWeaponsDecision;
    [SerializeField] private float minWeaponDecisionTime = 0.2f;
    [SerializeField] private float maxWeaponDecisionTime = 0.6f;

    [Task]
    void UpdateWeaponDecision()
    {
        if(Time.time < nextWeaponsDecision)
            return;
        
        nextWeaponsDecision = Time.time + Random.Range(minWeaponDecisionTime, maxWeaponDecisionTime);

        if (attackTarget == null)
        {
            selectedEnergyWeapon = WeaponType.None;
            useCannons = false;
            useMissiles = false;
            return;
        }
        
        float distance = Vector3.Distance(transform.position, attackTarget.transform.position);

        // Energy Weapon Selection
        if(distance >= 20f && distance <= 50f && mechSystem.energy >= 300)
            selectedEnergyWeapon = WeaponType.Beam;
        else if(mechSystem.energy >= 10)
            selectedEnergyWeapon = WeaponType.Lasers;
        else
            selectedEnergyWeapon = WeaponType.None;

        // Cannon Weapon Selection
        useCannons = distance >= 25f && mechSystem.shells >= 4;

        // Missile Weapon Selection
        useMissiles = distance >= 50f && mechSystem.missiles >= 18;

        Task.current.Succeed(); 
    }

    // Condition Tasks
    [Task]
    bool ShouldUseBeam()
    {
        return selectedEnergyWeapon == WeaponType.Beam && mechAIAiming.FireAngle(10);
    }

    [Task]
    bool ShouldUseLasers()
    {
        return selectedEnergyWeapon == WeaponType.Lasers && mechAIAiming.FireAngle(20);
    }

    [Task]
    bool ShouldUseCannons()
    {
        return useCannons && mechAIAiming.FireAngle(15);
    }

    [Task]
    bool ShouldUseMissiles()
    {
        return useMissiles && mechAIAiming.FireAngle(5);
    }

    // Firing Tasks
    [Task]
    void FireLasers()
    {
        mechAIWeapons.laserBeamAI = false;
        mechAIWeapons.Lasers();
        Task.current.Succeed(); 
    }

    [Task]
    void FireBeam()
    {
        ResetLaserBurst();
        GetComponent<Animator>().SetBool("Lasers", false);
        mechAIWeapons.laserBeamAI = true;
        Task.current.Succeed(); 
    }

    [Task]
    void FireCannons()
    {
        mechAIWeapons.Cannons();
        Task.current.Succeed(); 
    }

    [Task]
    void FireMissiles()
    {
        mechAIWeapons.MissileArray();
        Task.current.Succeed(); 
    }

    // Weapons Stop Tasks
    [Task]
    void StopWeapons()
    {
        mechAIWeapons.laserBeamAI = false;

        Animator anim = GetComponent<Animator>();
        anim.SetBool("Lasers", false);
        anim.SetBool("CannonsA", false);
        anim.SetBool("CannonsB", false);

        ResetLaserBurst();
        Task.current.Succeed(); 
    }

    [Task]
    void DisableEnergyWeapons()
    {
        mechAIWeapons.laserBeamAI = false;

        Animator anim = GetComponent<Animator>();
        anim.SetBool("Lasers", false);

        ResetLaserBurst();
        Task.current.Succeed(); 
    }

    [Task]
    void StopCannons()
    {
        Animator anim = GetComponent<Animator>();
        anim.SetBool("CannonsA", false);
        anim.SetBool("CannonsB", false);
        Task.current.Succeed(); 
    }
    
    // Weapon Firing modes
    private bool burstActive;
    private float burstEndTime;
    private float nextBurstTime;
    [SerializeField] private float burstDuration = 0.5f;
    [SerializeField] private float burstPause = 0.8f;

    [Task]
    void FireLaserBurst()
    {
        if(!burstActive)
        {
            if(Time.time < nextBurstTime)
                return;
            burstActive = true;
            burstEndTime = Time.time + burstDuration;
        }

        if(Time.time < burstEndTime)
            mechAIWeapons.Lasers();
        else
        {
            burstActive = false;
            nextBurstTime = Time.time + burstPause;
        }
        Task.current.Succeed(); 
    }

    private void ResetLaserBurst()
    {
        burstActive = false;
        burstEndTime = 0f;
        nextBurstTime = 0f;
    }


    // //Method controlling logic of firing of weapons: Consider minimum ammunition, appropriate range, firing angle etc
    // private void FiringSystem() {

    //     //Lasers - Enough energy and within a generous firing angle
    //     if (mechSystem.energy > 10 && mechAIAiming.FireAngle(20))
    //         mechAIWeapons.Lasers();

    //     //Cannons - Moderate distance, enough shells and tight firing angle
    //     if (Vector3.Distance(transform.position, attackTarget.transform.position) > 25
    //         && mechSystem.shells > 4 && mechAIAiming.FireAngle(15))
    //         mechAIWeapons.Cannons();

    //     //Laser Beam - Strict range, plenty of energy and very tight firing angle
    //     if (Vector3.Distance(transform.position, attackTarget.transform.position) > 20
    //         && Vector3.Distance(transform.position, attackTarget.transform.position) < 50
    //         && mechSystem.energy >= 300 && mechAIAiming.FireAngle(10))
    //         mechAIWeapons.laserBeamAI = true;
    //     else
    //         mechAIWeapons.laserBeamAI = false;

        
    //     //Missile Array - Long Range, enough ammo, very tight firing angle
    //     if (Vector3.Distance(transform.position, attackTarget.transform.position) > 50
    //         && mechSystem.missiles >= 18 && mechAIAiming.FireAngle(5))
    //         mechAIWeapons.MissileArray();

    // }

}
