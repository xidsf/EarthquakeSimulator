using UnityEngine;

public class VelocityChecker : MonoBehaviour
{
    Rigidbody rb;
    EarthquakeManager earthquakeManager;
    public Rigidbody roomRb;
    public float highestVelocity = 0f;
    public Vector3 relativeDisplacement;
    public Vector3 maxRelativeDisplacement;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        earthquakeManager = FindAnyObjectByType<EarthquakeManager>();
    }


    private void FixedUpdate()
    {
        if(earthquakeManager.IsSimulating)
        {
            float currentVelocity = rb.linearVelocity.magnitude;
            if (currentVelocity > highestVelocity)
            {
                highestVelocity = currentVelocity;
            }
            relativeDisplacement = roomRb.position - rb.position;
            maxRelativeDisplacement = Vector3.Max(maxRelativeDisplacement, new Vector3(Mathf.Abs(relativeDisplacement.x), Mathf.Abs(relativeDisplacement.y), Mathf.Abs(relativeDisplacement.z)));
            Debug.Log($"상대변위: {relativeDisplacement}");
        }
        
    }
}
