using System.Collections.Generic;
using UnityEngine;

public class FurnitureRegistry : MonoBehaviour
{
    public static FurnitureRegistry Instance { get; private set; }

    private readonly List<Rigidbody> furnitureBodies = new();

    private void Awake()
    {
        Instance = this;
    }

    public void Register(Rigidbody rb)
    {
        if (rb != null && !furnitureBodies.Contains(rb))
            furnitureBodies.Add(rb);
    }

    public IReadOnlyList<Rigidbody> GetBodies()
    {
        return furnitureBodies;
    }
}