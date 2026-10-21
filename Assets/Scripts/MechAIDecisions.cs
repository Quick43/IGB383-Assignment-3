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
        if(attackTarget != null)
            FiringSystem();
        else
            mechAIWeapons.laserBeamAI = false;
        UpdateDecisions();
    }

    // Detection Times
    [SerializeField] private float minDetectionTime = 0.15f;
    [SerializeField] private float maxDetectionTime = 0.45f;

    // Detection logic
    private GameObject pendingTarget;
    private float detectionTimer;
    private void UpdateTargetDetection()
    {
        if(attackTarget != null)
            return;
        
        GameObject visibleTarget = mechAIAiming.ClosestTarget(mechAIAiming.currentTargets);
        if(visibleTarget == null)
        {
            pendingTarget = null;
            detectionTimer = 0f;
            return;
        }

        if(visibleTarget != pendingTarget)
        {
            pendingTarget = visibleTarget;
            detectionTimer = Time.time + Random.Range(minDetectionTime, maxDetectionTime);
        }

        if(Time.time >= detectionTimer)
        {
            attackTarget = pendingTarget;
            pendingTarget = null;
        }
    }

    [SerializeField] private float minDecisionInterval = 0.25f;
    [SerializeField] private float maxDecisionInterval = 0.65f;
    private float nextDecisionTime;
    private bool shouldFlee;
    
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
    [SerializeField] private float targetMemoryDuration = 5f;

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

    //FSM Behaviour: Attack 
    [Task]
    private void Attack() {
         
        //If there is a target, set it as the aimTarget 
        if (attackTarget && mechAIAiming.LineOfSight(attackTarget)) {

            //Child object correction - wonky pivot point
            mechAIAiming.aimTarget = attackTarget.transform.GetChild(0).gameObject;

            //Move Towards attack Target
            if (Vector3.Distance(transform.position, attackTarget.transform.position) >= 45.0f) {
                mechAIMovement.Movement(attackTarget.transform.position, 45);
            }
            //Otherwise "strafe" - move towards random patrol points at intervals
            else if (Vector3.Distance(transform.position, attackTarget.transform.position) < 45.0f && Time.time > attackTimer) {
                patrolIndex = Random.Range(0, patrolPoints.Length - 1);
                mechAIMovement.Movement(patrolPoints[patrolIndex].transform.position, 2);
                attackTimer = Time.time + attackTime + Random.Range(-0.5f, 0.5f);
            }

            //Track position of current target to pursue if lost
            pursuePoint = attackTarget.transform.position;
        }
    }

    //FSM Behaviour: Pursue
    [Task]
    void Pursue() {

        //Move towards last known position of attackTarget
        if (Vector3.Distance(transform.position, pursuePoint) > 3.0f) {
            mechAIMovement.Movement(pursuePoint, 1);
            mechAIAiming.RandomAimTarget(patrolPoints);
        }
        //Otherwise if reached and have not re-engaged, reset attackTarget and Roam
        else {
            attackTarget = null;
            patrolIndex = Random.Range(0, patrolPoints.Length - 1);
            mechAIMovement.Movement(patrolPoints[patrolIndex].transform.position, 1);
            mechState = MechStates.Roam;
        }
    }

    //FSM Behaviour: Flee
    [Task]
    void Flee() {

        //If there is an attack target, set it as the aimTarget 
        if (attackTarget && mechAIAiming.LineOfSight(attackTarget)) {
            //Child object correction - wonky pivot point
            mechAIAiming.aimTarget = attackTarget.transform.GetChild(0).gameObject;
        } else {
            //Look at random patrol points
            mechAIAiming.RandomAimTarget(patrolPoints);
        }

        //Move towards random patrol points <<< This could be drastically improved!
        if (Vector3.Distance(transform.position, patrolPoints[patrolIndex].transform.position) <= 2.0f) {
            patrolIndex = Random.Range(0, patrolPoints.Length - 1);
        } else {
            mechAIMovement.Movement(patrolPoints[patrolIndex].transform.position, 1);
        }
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
    [Task]
    private bool StatusCheck() {

        return shouldFlee;
    }

    [SerializeField] private float aimErrorRadius = 2f;
    [SerializeField] private float aimCorrectionInterval = 0.35f;
    [SerializeField] private float aimTrackingSpeed = 5f;

    private Vector3 currentAimPoint;
    private Vector3 aimOffset;
    private float nextAimCorrection;

    private void UpdateImperfectAim(Transform target)
    {
        if(target == null)
            return;
        
        if(Time.time >= nextAimCorrection)
        {
            aimOffset = Random.insideUnitSphere * aimErrorRadius;
            nextAimCorrection = Time.time + aimCorrectionInterval;
        }
        Vector3 desiredAimPoint = target.position + aimOffset;
        currentAimPoint = Vector3.Lerp(currentAimPoint, desiredAimPoint, 1f - Mathf.Exp(-aimTrackingSpeed * Time.deltaTime));
    }

    //Method controlling logic of firing of weapons: Consider minimum ammunition, appropriate range, firing angle etc
    private void FiringSystem() {

        //Lasers - Enough energy and within a generous firing angle
        if (mechSystem.energy > 10 && mechAIAiming.FireAngle(20))
            mechAIWeapons.Lasers();

        //Cannons - Moderate distance, enough shells and tight firing angle
        if (Vector3.Distance(transform.position, attackTarget.transform.position) > 25
            && mechSystem.shells > 4 && mechAIAiming.FireAngle(15))
            mechAIWeapons.Cannons();

        //Laser Beam - Strict range, plenty of energy and very tight firing angle
        if (Vector3.Distance(transform.position, attackTarget.transform.position) > 20
            && Vector3.Distance(transform.position, attackTarget.transform.position) < 50
            && mechSystem.energy >= 300 && mechAIAiming.FireAngle(10))
            mechAIWeapons.laserBeamAI = true;
        else
            mechAIWeapons.laserBeamAI = false;

        
        //Missile Array - Long Range, enough ammo, very tight firing angle
        if (Vector3.Distance(transform.position, attackTarget.transform.position) > 50
            && mechSystem.missiles >= 18 && mechAIAiming.FireAngle(5))
            mechAIWeapons.MissileArray();

    }

}
