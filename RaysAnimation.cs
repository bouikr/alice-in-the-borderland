using UnityEngine;

public class RaysAnimation : MonoBehaviour
{
    public float speed = 400f;

    RectTransform rect;

    void Start()
    {
        rect = GetComponent<RectTransform>();
    }

    void Update()
    {
        rect.anchoredPosition += Vector2.right * speed * Time.deltaTime;

        if (rect.anchoredPosition.x > 2000)
        {
            rect.anchoredPosition =
                new Vector2(-2000, rect.anchoredPosition.y);
        }
    }
}