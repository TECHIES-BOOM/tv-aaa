using UnityEngine;

/// <summary>Put on anything the player can click. Replace the placeholder cube with your model, keep this + a collider.</summary>
public class PvoClickable : MonoBehaviour
{
    public enum Type { SectorKnob, HeightKnob, Phone, FireCover, FireButton }
    public Type type;
    public string tip;
}
