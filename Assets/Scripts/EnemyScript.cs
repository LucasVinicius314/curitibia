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
    float aggroDistance = 14f;

    void Start(){
        navMeshAgent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        if (isServer){
            if (Time.time >= pathUpdateDeadLine){
                pathUpdateDeadLine = Time.time + pathUpdateDelay;
                if (target != null){
                    targetDistance = Vector3.Distance(transform.position, target.position);
                    if (targetDistance < aggroDistance){
                        navMeshAgent.SetDestination(target.position);
                    }
                }
                else {
                    FindTarget();
                }
            }
        }
    }

    void FindTarget() {
        GameObject closestPlayer = null;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (closestPlayer != null){
                if (Vector3.Distance(transform.position, player.transform.position) > Vector3.Distance(transform.position, closestPlayer.transform.position)){
                    closestPlayer = player;
                }
            }
            else {
                closestPlayer = player;
            }
        }
        if (closestPlayer != null) {
            target = closestPlayer.transform;
        }
    }
}
