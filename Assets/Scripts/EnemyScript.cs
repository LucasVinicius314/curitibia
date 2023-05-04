using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Mirror;

public class EnemyScript : NetworkBehaviour
{
    // Start is called before the first frame update
    public Transform target;
    [SerializeField] float targetDistance;
    public NavMeshAgent navMeshAgent;
    float pathUpdateDelay = 0.2f;
    float maxRange = 13f;
    LayerMask ignoreSelf = 1 << 7;
    string enemyState;

    void Start()
    {
        ignoreSelf = ~ignoreSelf;
        navMeshAgent = GetComponent<NavMeshAgent>();
        if (isServer)
        {
            StartCoroutine(UpdateState());
        }
    }

    IEnumerator UpdateState()
    {
        enemyState = "Patrolling";
        while (true)
        {
            if (target != null)
            {
                if (CheckVisibility(target.position))
                {
                    navMeshAgent.stoppingDistance = 2f;
                    enemyState = "Chasing";
                    navMeshAgent.SetDestination(target.position);
                }
                else
                {
                    navMeshAgent.stoppingDistance = 0;
                    enemyState = "Searching";
                    target = null;
                }
            }
            else
            {
                if (Vector3.Distance(transform.position, navMeshAgent.destination) < .5f)
                {
                    enemyState = "Patrolling";
                }
                FindTarget();
            }
            yield return new WaitForSeconds(pathUpdateDelay);
        }
    }

    void Update()
    {
        Color stateColor = Color.green;
        if (target == null)
        {
            stateColor = Color.cyan;
        }
        else
        {
            NavMeshHit hit;
            if (!navMeshAgent.Raycast(target.position, out hit))
            {
                Debug.DrawLine(transform.position, hit.position, Color.gray);
            }
        }
        Debug.DrawLine(transform.position, navMeshAgent.destination, stateColor);
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

    bool CheckVisibility(Vector3 target)
    {
        if (target != null)
        {
            RaycastHit hit;
            Vector3 transformOffset = transform.position + new Vector3(0, 1f, 0);
            targetDistance = Vector3.Distance(transform.position, target);
            if (targetDistance < maxRange)
            {
                if (Physics.Raycast(transformOffset, target - transformOffset, out hit, maxRange, ignoreSelf))
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