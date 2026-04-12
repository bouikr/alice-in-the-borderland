// TokyoCitySetup.cs — attache sur un GameObject vide
using UnityEngine;

public class TokyoCitySetup : MonoBehaviour
{
    void Start()
    {
        // Sol de la ville
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "CityGround";
        ground.transform.localScale = new Vector3(50, 1, 50);  // 500x500m

        // Caméra positionnée pour vue urbaine
        Camera.main.transform.position = new Vector3(0, 15, -30);
        Camera.main.transform.rotation = Quaternion.Euler(20, 0, 0);
        Camera.main.fieldOfView = 70;
    }
}