using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class ProjectileScript : NetworkBehaviour
{
    Rigidbody rb;
    LayerMask enemyLayer = 1 << 7;
    bool applyForce = true;
    // Start is called before the first frame update
    void Start()
    {
        if (!isServer) return;
        rb = GetComponent<Rigidbody>();
        rb.AddForce(transform.forward * 13, ForceMode.Impulse);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isServer) return;
        foreach (var item in collision.contacts)
        {
            if (collision.transform.tag != "Player" && collision.transform.tag != "Enemy" && collision.transform.tag != "Projectile")
            {
                NetworkServer.Destroy(gameObject);
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
