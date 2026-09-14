using UnityEngine;
using System.Collections;

public class HerdSimulationManager : MonoBehaviour
{
    private LoadingOverlay loadingOverlay;
    private bool simulationReady = false;

    // Run simulation at 30 updates per second
    // Rendering can still happen at higher FPS
    private const float SimulationStep = 1f / 30f; // advance sheep at 30 Hz

    // Used to track when the next simulation update should happen. 30 Hz
    private float SimulationCounter = 0f;

    // Controls how quickly simulation time advances
    private float simulationSpeed = 1f;

    // Stops simulation updates while still allowing rendering/UI
    private bool simulationPaused = false;

    // Pasture bounds (X and Z)
    private static float PastureMin => -SimulationConfig.PastureSize / 2f;

    private static float PastureMax => SimulationConfig.PastureSize / 2f;

    // Each spatial-grid cell is 4x4 world units
    private const float GridCellSize = 4f;

    // 1 grid cell per 4 units
    private static int GridWidth => Mathf.CeilToInt( (PastureMax - PastureMin) / GridCellSize );

    // Components responsible for entities
    private SheepSimulationSystem sheepSystem;
    private WolfSystem wolfSystem;
    private DogSystem dogSystem;

    [SerializeField] // private, but show in inspector
    private InstancedRenderer sheepRenderer;

    [SerializeField]
    private InstancedRenderer wolfRenderer;

    [SerializeField]
    private InstancedRenderer dogRenderer;

    [SerializeField]
    private Transform pasturePlane;

    // Initialize simulation
    IEnumerator Start()
    {
        loadingOverlay = GetComponent<LoadingOverlay>();
        loadingOverlay.Progress = 0f;

        // Unity's default Plane is 10x10 world units,
        // so scale it to match the selected pasture size
        float planeScale = SimulationConfig.PastureSize / 10f;

        pasturePlane.localScale = new Vector3(planeScale, 1f, planeScale);

        yield return null;

        sheepSystem = GetComponent<SheepSimulationSystem>();
        sheepSystem.Initialize( PastureMin, PastureMax, GridCellSize, GridWidth );
        loadingOverlay.Progress = 0.33f; yield return null;
        dogSystem = GetComponent<DogSystem>();
        dogSystem.Initialize( PastureMin, PastureMax );

        loadingOverlay.Progress = 0.66f;
        yield return null;

        wolfSystem = GetComponent<WolfSystem>();
        wolfSystem.Initialize( PastureMin, PastureMax, GridCellSize, GridWidth );

        loadingOverlay.Progress = 1f;
        yield return null;

        simulationReady = true;
        loadingOverlay.IsLoading = false;
    }

    // Run simulation at 30hz. Render every frame
    void Update()
    {
        if (!simulationReady)
            return;

        // Add the amount of real time that passed since the last frame
        if (!simulationPaused)
        {
            SimulationCounter += Time.deltaTime * simulationSpeed;
        }

        // Simulation code is run at 30 Hz
        while (SimulationCounter >= SimulationStep)
        {
            SimulateTick(SimulationStep);

            SimulationCounter -= SimulationStep;
        }

        // Rendering happens every frame, even though sheep movement updates at 30 Hz
        sheepRenderer.Render( sheepSystem.Matrices, sheepSystem.RenderCount );
        wolfRenderer.Render( wolfSystem.Matrices, wolfSystem.RenderCount );
        dogRenderer.Render( dogSystem.Matrices, dogSystem.Count );
    }

    // Run one simulation tick for sheep, dogs, and wolves
    private void SimulateTick(float deltaTime)
    {
        // Build wolf spatial grid before sheep check for nearby wolves.
        wolfSystem.BuildGrid();

        sheepSystem.SimulateMovement( deltaTime, wolfSystem.States, wolfSystem.Grid, wolfSystem.FleeRadius );

        // Move Dogs first so wolves can react to them this tick
        dogSystem.Simulate( deltaTime, wolfSystem );

        // Move wolves
        wolfSystem.Simulate( deltaTime, sheepSystem.Current, sheepSystem.Next, sheepSystem.Grid, dogSystem );

        // sheepNext now contains the newly calculated simulation state,
        // so it becomes sheepCurrent. Swapping references
        sheepSystem.FinishSimulationTick();
    }

    public bool SimulationPaused => simulationPaused;
    public float SimulationSpeed => simulationSpeed;

    // Pause and unpause the simulation
    public void TogglePause()
    {
        simulationPaused = !simulationPaused;
    }

    // Set speed of simulation (0.5, 1, 2)
    public void SetSimulationSpeed(float speed)
    {
        simulationSpeed = speed;

        // Clicking a speed button also resumes the simulation.
        simulationPaused = false;
    }
}