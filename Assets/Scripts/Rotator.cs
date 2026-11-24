using UnityEngine;

/// <summary>Rotates the GameObject constantly around its own axes.</summary>
public class Rotator : MonoBehaviour
{
    [Tooltip("Rotation speed in degrees per second for each axis.")]
    [SerializeField] private Vector3 degreesPerSecond = new Vector3(0f, 90f, 0f);

    private void Update()
    {
        transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
