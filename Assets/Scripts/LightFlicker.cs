using UnityEngine;

/// <summary>Flickering bunker lamp with a Blackout() hit used by near explosions.</summary>
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    public float baseIntensity = 3f, amount = .18f, speed = 7f;
    Light l; float black;

    void Awake() { l = GetComponent<Light>(); }
    public void Blackout(float seconds) { black = seconds; }

    void Update()
    {
        float f = 1f - amount * Mathf.PerlinNoise(Time.time * speed, 3.7f);
        if (Random.value < .003f) f *= .25f;
        if (black > 0f) { black -= Time.deltaTime; f *= Random.value < .5f ? 0f : .15f; }
        l.intensity = baseIntensity * f;
    }
}
