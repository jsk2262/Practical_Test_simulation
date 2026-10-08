using UnityEngine;

/// <summary>
/// The gentle flicker of a gas ring's blue flames (GasFlame_* rings made by
/// Tools > Kitchen > Add Gas Flame Rings): the flame height breathes a little
/// at random, along the ring's own up (the counter's up).
/// </summary>
public class GasFlameFlicker : MonoBehaviour
{
    [SerializeField] private float amount = 0.15f;
    [SerializeField] private float speed = 9f;

    private Vector3 baseScale;
    private float seed;

    void Awake()
    {
        baseScale = transform.localScale;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed) - 0.5f;
        transform.localScale = new Vector3(baseScale.x, baseScale.y * (1f + n * 2f * amount), baseScale.z);
    }
}
