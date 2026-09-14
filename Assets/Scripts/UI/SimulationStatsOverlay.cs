using UnityEngine;
using TMPro;

public class SimulationStatsOverlay : MonoBehaviour
{
    private SheepSimulationSystem sheepSystem;
    private WolfSystem wolfSystem;
    private DogSystem dogSystem;

    public TMP_Text fpsText;
    public TMP_Text sheepText;
    public TMP_Text wolvesText;
    public TMP_Text dogsText;
    public TMP_Text totalEntitiesText;

    private int sheepAlive;
    private int wolvesAlive;
    private float refreshTimer;
    private float smoothedFPS;

    // Get components
    private void Start()
    {
        sheepSystem = GetComponent<SheepSimulationSystem>();
        wolfSystem = GetComponent<WolfSystem>();
        dogSystem = GetComponent<DogSystem>();
    }

    // Count FPS and count of each entity type. Entities are only counted once per second
    private void Update()
    {
        if ( sheepSystem == null || wolfSystem == null || dogSystem == null || !sheepSystem.Current.IsCreated || !wolfSystem.States.IsCreated || !dogSystem.States.IsCreated ) return;

        float currentFPS = 1f / Time.unscaledDeltaTime;

        smoothedFPS = Mathf.Lerp( smoothedFPS, currentFPS, 5f * Time.unscaledDeltaTime );

        refreshTimer += Time.deltaTime;

        // Recount active entities once per second
        if (refreshTimer >= 1f)
        {
            refreshTimer = 0f;

            sheepAlive = 0;

            for (int i = 0; i < sheepSystem.Current.Length; i++)
                if (sheepSystem.Current[i].lost == false)
                    sheepAlive++;

            wolvesAlive = 0;

            for (int i = 0; i < wolfSystem.States.Length; i++)
                if (wolfSystem.States[i].lost == 0)
                    wolvesAlive++;
        }

        // Update stats
        fpsText.text = "FPS: " + Mathf.RoundToInt(smoothedFPS);
        sheepText.text = "Sheep: " + sheepAlive + " / " + sheepSystem.Count;
        wolvesText.text = "Wolves: " + wolvesAlive + " / " + wolfSystem.Count;
        dogsText.text = "Dogs: " + dogSystem.Count + " / " + dogSystem.Count;

        int currentEntities = sheepAlive + wolvesAlive + dogSystem.Count;

        totalEntitiesText.text = "Total Entities: " + currentEntities;
    }
}