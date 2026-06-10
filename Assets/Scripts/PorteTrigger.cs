using UnityEngine;
using UnityEngine.SceneManagement;

public class PorteTrigger : MonoBehaviour
{
    public string sceneDestination;
    private bool active = false;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !active)
        {
            active = true;
            StartCoroutine(OuvrirPorte());
        }
    }

    System.Collections.IEnumerator OuvrirPorte()
    {
        float t = 0f;
        Quaternion depart = transform.rotation;
        Quaternion arrivee = depart * Quaternion.Euler(0, 90, 0);

        while (t < 1f)
        {
            t += Time.deltaTime * 1.5f;
            transform.rotation = Quaternion.Lerp(depart, arrivee, t);
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
        SceneManager.LoadScene(sceneDestination);
    }
}