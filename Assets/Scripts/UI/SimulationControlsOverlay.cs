using UnityEngine;
using UnityEngine.UI;

// Buttons to adjust simulation speed
public class SimulationControlsOverlay : MonoBehaviour
{
    private HerdSimulationManager simulationManager;
    private SheepSimulationSystem sheepSystem;

    public Button flockingButton;
    public Button pauseButton;
    public Button halfSpeedButton;
    public Button normalSpeedButton;
    public Button doubleSpeedButton;

    // Set up button components
    private void Start()
    {
        simulationManager = GetComponent<HerdSimulationManager>();
        sheepSystem = GetComponent<SheepSimulationSystem>();

        flockingButton.onClick.AddListener( sheepSystem.ToggleFlocking );
        pauseButton.onClick.AddListener( simulationManager.TogglePause );
        halfSpeedButton.onClick.AddListener(() => simulationManager.SetSimulationSpeed(0.5f) );
        normalSpeedButton.onClick.AddListener(() => simulationManager.SetSimulationSpeed(1f) );
        doubleSpeedButton.onClick.AddListener(() => simulationManager.SetSimulationSpeed(2f) );
    }

    private void Update()
    {
        // Selected buttons are highlighted green
        SetButtonHighlight(flockingButton, sheepSystem.FlockingEnabled);
        SetButtonHighlight( pauseButton, simulationManager.SimulationPaused );
        SetButtonHighlight( halfSpeedButton, !simulationManager.SimulationPaused && simulationManager.SimulationSpeed == 0.5f );
        SetButtonHighlight( normalSpeedButton, !simulationManager.SimulationPaused && simulationManager.SimulationSpeed == 1f );
        SetButtonHighlight( doubleSpeedButton, !simulationManager.SimulationPaused && simulationManager.SimulationSpeed == 2f );
    }

    private void SetButtonHighlight(Button button, bool active)
    {
        if (active)
            button.image.color = Color.green;
        else
            button.image.color = Color.white;
    }
}