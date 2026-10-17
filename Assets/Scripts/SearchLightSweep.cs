using UnityEngine;

/// <summary>Sweeping searchlight beam. Builds its own cone mesh (apex at origin, pointing +Y).</summary>
public class SearchLightSweep : MonoBehaviour
{
    public float length = 450f, radius = 16f, amp = 28f, speed = 1f, phase;

    void Awake()
    {
        var mf = GetComponent<MeshFilter>(); if (!mf) mf = gameObject.AddComponent<MeshFilter>();
        int n = 20; var v = new Vector3[n + 1]; var c = new Color[n + 1]; var t = new int[n * 3];
        v[0] = Vector3.zero; c[0] = new Color(1, 1, 1, .55f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            v[i + 1] = new Vector3(Mathf.Cos(a) * radius, length, Mathf.Sin(a) * radius); c[i + 1] = new Color(1, 1, 1, 0);
            t[i * 3] = 0; t[i * 3 + 1] = 1 + i; t[i * 3 + 2] = 1 + (i + 1) % n;
        }
        var m = new Mesh { vertices = v, colors = c, triangles = t };
        m.bounds = new Bounds(new Vector3(0, length / 2f, 0), new Vector3(radius * 2f, length, radius * 2f));
        mf.sharedMesh = m;
    }

    void Update()
    {
        float t = Time.time * speed + phase;
        transform.localRotation = Quaternion.Euler(Mathf.Sin(t * .7f) * amp, 0f, Mathf.Cos(t * .5f) * amp * .7f);
    }
}
