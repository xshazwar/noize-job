 using System.Collections;
 using System.Collections.Generic;
 using UnityEngine;
 
 public class Shooter : MonoBehaviour
 {
     public GameObject obj;
     public float speed = 500f;
     public float TTL_seconds = 10f;

     void Update()
     {
         if (Input.GetKeyDown(KeyCode.Space))
         {
             GameObject go = Instantiate(obj, transform.position, Quaternion.identity);
             Rigidbody rb = go.GetComponent<Rigidbody>();
 
             rb.AddForce(transform.forward * speed);
             Destroy(go, TTL_seconds);
         }
     }
 }
 