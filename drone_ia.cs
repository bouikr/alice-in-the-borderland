using UnityEngine;

public class DroneAI : MonoBehaviour
{
    public Transform player;
    public float speed = 6f;
    public float height = 6f;
    public float followDistance = 15f;

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance < followDistance)
        {
            Vector3 target = player.position + Vector3.up * height;

            transform.position = Vector3.Lerp(transform.position, target, speed * Time.deltaTime);
            transform.LookAt(player);
        }
    }
}