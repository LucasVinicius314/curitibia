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
    private float pathUpdateDeadLine;
    float pathUpdateDelay = 0.2f;
    float aggroDistance = 5f;

    void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        if (isServer)
        {
            StartCoroutine(UpdateTarget());
        }
    }

    IEnumerator UpdateTarget()
    {
        while (true)
        {
            if (target != null)
            {
                targetDistance = Vector3.Distance(transform.position, target.position);
                if (targetDistance < aggroDistance)
                {
                    navMeshAgent.SetDestination(target.position);
                }
                else
                {
                    target = null;
                }
            }
            else
            {
                FindTarget();
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    void Update()
    {
        if (target != null)
        {
            Debug.DrawLine(transform.position, target.position, Color.blue);
        }
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
        if (closestPlayer != null && Vector3.Distance(transform.position, closestPlayer.transform.position) < aggroDistance)
        {
            target = closestPlayer.transform;
        }
    }
}
