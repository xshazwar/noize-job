 using System.Collections;
 using System.Collections.Generic;
 using UnityEngine;
 
 public class StickyProjectile : MonoBehaviour
 {
    public Rigidbody rb;
    void OnCollisionEnter(Collision collision){
        foreach (ContactPoint contact in collision.contacts)
        {
            Debug.DrawRay(contact.point, contact.normal, Color.white, 3);
        }
        rb.isKinematic = true;
    }
 }
