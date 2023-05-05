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
    Transform root;
    Transform legR;
    Transform legL;
    Vector3 oldPosition;
    LayerMask enemyLayer = 1 << 7;
    float turningSpeed = 6f;
    float pathUpdateDelay = 0.2f;
    float maxRange = 13f;
    float fieldOfView = 70f;
    float stoppingDistance = .5f;
    float targetDistance;
    float speed;
    float speedPerSec;
    float animationRandomiser;

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
        legR.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(speedPerSec * -40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(speedPerSec * 40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
        legL.localRotation = Quaternion.Slerp(Quaternion.Euler(Mathf.Clamp(speedPerSec * 40f, -60, 60), 0, 0), Quaternion.Euler(Mathf.Clamp(speedPerSec * -40f, -60, 60), 0, 0), (animationSin + 1f) / 2f);
        root.localRotation = Quaternion.Euler(speedPerSec * 4, animationSin * speedPerSec * 6, 0);
    }

    IEnumerator UpdateState()
    {
        while (true)
        {
            if (target != null)
            {
                if (CheckVisibility(target.position) || targetDistance < 2f)
                {
                    
                    navMeshAgent.stoppingDistance = stoppingDistance;
                    navMeshAgent.SetDestination(target.position);
                }
                else
                {
                    LoseTarget();
                }
            }
            else
            {
                FindNewTarget();
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
            targetDistance = Vector3.Distance(transform.position, target.position);
            Debug.DrawLine(transform.position, target.position, Color.white);
        }
        Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward), Color.red);
        AnimateRunning();
        if (navMeshAgent.remainingDistance < 4f && target != null)
        {
            LookAt(target.position);
        }
    }

    // Search for the closest player
    void FindNewTarget()
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

    void LoseTarget()
    {
        if (target == null) return;
        Vector3 targetTracker = target.position + target.forward;
        target = null;
        navMeshAgent.stoppingDistance = .5f;
        navMeshAgent.SetDestination(targetTracker);

    }

    void LookAt(Vector3 targetPosition)
    {
        if (targetPosition == null) return;
        Vector3 direction = (targetPosition - transform.position).normalized;
        // direction.y = 0;
        Quaternion _lookRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, _lookRotation, Time.deltaTime * turningSpeed);
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
        bool isVisible = false;
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
                        isVisible = true;
                    }
                }
            }
        }
        return isVisible;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isServer) return;
        foreach (var item in collision.contacts)
        {
            if (collision.transform.tag == "Player")
            {
                collision.gameObject.GetComponent<Rigidbody>().AddExplosionForce(20, -item.point, 1f);
                Debug.DrawLine(item.point, item.normal, Color.magenta, 1.5f);
            }
        }
    }
}
