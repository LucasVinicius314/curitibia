using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Mirror;

public class EnemyScript : NetworkBehaviour
{
    // Start is called before the first frame update
    Transform target;
    GameObject projectilePrefab;
    NavMeshAgent navMeshAgent;
    LayerMask enemyLayer = 1 << 7;
    Transform root;
    Transform legR;
    Transform legL;
    float targetDistance;
    float speed;
    float speedPerSec;
    float animationRandomiser;
    Vector3 oldPosition;
    float pathUpdateDelay = 0.2f;
    float maxRange = 13f;
    float fieldOfView = 70f;

    void Start()
    {
        projectilePrefab = Utils.LoadPrefabFromFile("Prefabs/Projectile");
        navMeshAgent = GetComponent<NavMeshAgent>();
        root = transform.Find("Amogos/Root");
        legR = transform.Find("Amogos/Root/Legs/Leg_R");
        legL = transform.Find("Amogos/Root/Legs/Leg_L");
        oldPosition = transform.position;
        animationRandomiser = Random.Range(0, 50);
        if (isServer)
        {
            StartCoroutine(UpdateState());
        }
    }

    void AnimateRunning()
    {
        float animationSin = Mathf.Sin((Time.time + animationRandomiser) * 10f);
        legR.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(speedPerSec * -40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(speedPerSec * 40f, -60, 60), 0, 0), (animationSin + 1f ) / 2f );
        legL.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(speedPerSec * 40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(speedPerSec * -40f, -60, 60), 0, 0), (animationSin + 1f ) / 2f );
        root.localRotation = Quaternion.Euler(speedPerSec * 4, animationSin * speedPerSec * 6, 0);
    }

    IEnumerator UpdateState()
    {
        while (true)
        {
            if (target != null)
            {
                if (CheckVisibility(target.position))
                {
                    navMeshAgent.stoppingDistance = 2f;
                    navMeshAgent.SetDestination(target.position);
                    // Shoot();
                }
                else
                {
                    navMeshAgent.stoppingDistance = 0;
                    target = null;
                }
            }
            else
            {
                FindTarget();
            }
            yield return new WaitForSeconds(pathUpdateDelay);
        }
    }

    void Update()
    {
        speedPerSec = Vector3.Distance(oldPosition, transform.position) / Time.deltaTime;
        speed = Vector3.Distance(oldPosition, transform.position);
        oldPosition = transform.position;
        if (target == null)
        {
            Debug.DrawLine(transform.position, navMeshAgent.destination, Color.grey);
        }
        else
        {
            Debug.DrawLine(transform.position, target.position, Color.white);
        }
        Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward), Color.red);
        AnimateRunning();
    }

    // Search for the closest player
    void FindTarget()
    {
        GameObject closestPlayer = null;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (closestPlayer != null)
            {
                if (Vector3.Distance(transform.position, player.transform.position) < Vector3.Distance(transform.position, closestPlayer.transform.position))
                {
                    closestPlayer = player;
                }
            }
            else
            {
                closestPlayer = player;
            }
        }
        if (closestPlayer != null)
        {
            if (CheckVisibility(closestPlayer.transform.position))
            {
                target = closestPlayer.transform;
            }
        }
    }

    void Shoot()
    {
        GameObject projectile = Instantiate(projectilePrefab, transform.position + Vector3.up, transform.rotation);
        // Debug.Log(projectile.GetHashCode());
        NetworkServer.Spawn(projectile);
    }

    // Check if target is visible
    bool CheckVisibility(Vector3 targetPosition)
    {
        if (targetPosition != null)
        {
            RaycastHit hit;
            Vector3 targetDirection = targetPosition - transform.position;
            float angle = Vector3.Angle(targetDirection, transform.forward);
            if (angle < fieldOfView)
            {
                if (Physics.Raycast(transform.position, targetDirection, out hit, maxRange, ~enemyLayer))
                {
                    if (hit.transform.tag == "Player")
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
