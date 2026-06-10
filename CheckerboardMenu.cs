using UnityEngine;
using UnityEngine.UI;

public class CheckerboardMenu : MonoBehaviour
{
    public int textureSize = 256;
    public int squareSize = 100;

    public Color color1 = Color.black;
    public Color color2 = new Color(0.5f, 0f, 0f);

    void Start()
    {
        RawImage img = GetComponent<RawImage>();

        Texture2D tex = new Texture2D(textureSize, textureSize);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                bool checker =
                    ((x / squareSize) + (y / squareSize)) % 2 == 0;

                tex.SetPixel(x, y, checker ? color1 : color2);
            }
        }

        tex.Apply();

        img.texture = tex;

        // Répète la texture
        img.uvRect = new Rect(0, 0, 4, 2.5f);
    }
}