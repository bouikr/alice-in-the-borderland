using UnityEngine;

public class TitleAnimation : MonoBehaviour
{
    private Vector3 startPos;

    public float floatAmount = 10f;
    public float speed = 2f;

    void Start()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        Vector3 pos = startPos;

        pos.y += Mathf.Sin(Time.time * speed) * floatAmount;

        transform.localPosition = pos;
    }
}