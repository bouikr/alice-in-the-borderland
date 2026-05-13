using UnityEngine;

public class BulletDetection : MonoBehaviour
{
    public float damage = 25f;

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Bullet touche : " + other.name + " tag : " + other.tag);

        // Cherche PlayerHealth sur l'objet ou ses parents
        PlayerHealth ph = other.GetComponentInParent<PlayerHealth>();

        if (ph != null)
        {
            Debug.Log("DEGAT SUR PLAYER !");
            ph.TakeDamage(damage);
            Destroy(gameObject);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        PlayerHealth ph = collision.gameObject.GetComponentInParent<PlayerHealth>();

        if (ph != null)
        {
            ph.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}