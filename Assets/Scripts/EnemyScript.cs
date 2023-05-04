using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Mirror;

public class EnemyScript : NetworkBehaviour
{
    // Start is called before the first frame update
    public Transform target;
    public GameObject projectile;
    [SerializeField] float targetDistance;
    public NavMeshAgent navMeshAgent;
    float pathUpdateDelay = 0.2f;
    float maxRange = 13f;
    float fieldOfView = 70f;
    LayerMask enemyLayer = 1 << 7;
    string enemyState;

    void Start()
    {
        projectile = Utils.LoadPrefabFromFile("Prefabs/Projectile");
        navMeshAgent = GetComponent<NavMeshAgent>();
        if (isServer)
        {
            StartCoroutine(UpdateState());
        }
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
        if (target == null)
        {
            Debug.DrawLine(transform.position, navMeshAgent.destination, Color.grey);
        }
        else
        {
            Debug.DrawLine(transform.position, target.position, Color.white);
        }
        Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward), Color.red);
    }

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
        GameObject.Instantiate(projectile, transform.position, Quaternion.identity);
    }

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
        Debug.DrawLine(transform.position, targetPosition, Color.red);
        return false;
    }
}
