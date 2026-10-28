using UnityEngine;

// A thin stretched cube that flashes along a shot's path. One per shooter is enough: it lasts shorter than the gap between shots.
public class Tracer : MonoBehaviour
{
    float hideAt;

    public static Tracer Create(Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Tracer";
        DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().material.color = color;
        go.SetActive(false);
        return go.AddComponent<Tracer>();
    }

    public void Show(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        if (d.sqrMagnitude < 0.01f) return;
        transform.SetPositionAndRotation(from + d * 0.5f, Quaternion.LookRotation(d));
        transform.localScale = new Vector3(0.02f, 0.02f, d.magnitude);
        hideAt = Time.time + 0.04f;
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (Time.time >= hideAt) gameObject.SetActive(false);
    }
}
