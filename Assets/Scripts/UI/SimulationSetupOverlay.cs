using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/*
    Draws a GUI window with sliders to adjust number of entities, with
    a button to Confirm and Restart the simulation with the selected settings.
    Also draws a GUI window with simulation preset buttons that select specific settings,
    then restart the simulation
*/
public class SimulationSetupOverlay : MonoBehaviour
{
    // Simulation slider options
    private readonly int[] sheepOptions = { 1, 2, 8, 100, 500, 5000, 20000, 30000 };
    private readonly int[] wolfOptions = { 1, 2, 8, 100, 500, 5000, 15000, 20000 };
    private readonly int[] dogOptions = { 1, 2, 8, 100, 500, 1000, 5000, 10000 };
    private readonly int[] pastureOptions = { 200, 500, 1000, 2000 };

    public Slider sheepSlider;
    public Slider wolfSlider;
    public Slider dogSlider;
    public Slider pastureSlider;

    public TMP_Text sheepValueText;
    public TMP_Text wolfValueText;
    public TMP_Text dogValueText;
    public TMP_Text pastureValueText;

    public Button confirmButton;

    public Button pasturePresetButton;
    public Button wolfAttackButton;
    public Button wildlandsButton;

    private void Start()
    {
        // Initialize sliders
        sheepSlider.minValue = 0;
        sheepSlider.maxValue = sheepOptions.Length - 1;
        wolfSlider.minValue = 0;
        wolfSlider.maxValue = wolfOptions.Length - 1;
        dogSlider.minValue = 0;
        dogSlider.maxValue = dogOptions.Length - 1;
        pastureSlider.minValue = 0;
        pastureSlider.maxValue = pastureOptions.Length - 1;

        sheepSlider.value = FindOptionIndex(sheepOptions, SimulationConfig.SheepCount);
        wolfSlider.value = FindOptionIndex(wolfOptions, SimulationConfig.WolfCount);
        dogSlider.value = FindOptionIndex(dogOptions, SimulationConfig.DogCount);
        pastureSlider.value = FindOptionIndex(pastureOptions, SimulationConfig.PastureSize);

        // Update values
        sheepSlider.onValueChanged.AddListener(UpdateSheepValue);
        wolfSlider.onValueChanged.AddListener(UpdateWolfValue);
        dogSlider.onValueChanged.AddListener(UpdateDogValue);
        pastureSlider.onValueChanged.AddListener(UpdatePastureValue);

        // Preset buttons
        confirmButton.onClick.AddListener(ConfirmAndRestart);
        pasturePresetButton.onClick.AddListener(() => ApplyPreset(20000, 8, 2, 200) );
        wolfAttackButton.onClick.AddListener(() => ApplyPreset(20000, 500, 8, 200) );
        wildlandsButton.onClick.AddListener(() => ApplyPreset(20000, 15000, 500, 2000) );

        UpdateDisplayedValues();
    }

    private void UpdateSheepValue(float value)
    {
        int index = (int)value;
        sheepValueText.text = sheepOptions[index].ToString();
    }

    private void UpdateWolfValue(float value)
    {
        int index = (int)value;
        wolfValueText.text = wolfOptions[index].ToString();
    }

    private void UpdateDogValue(float value)
    {
        int index = (int)value;
        dogValueText.text = dogOptions[index].ToString();
    }

    private void UpdatePastureValue(float value)
    {
        int index = (int)value;
        pastureValueText.text = pastureOptions[index].ToString();
    }

    private void UpdateDisplayedValues()
    {
        UpdateSheepValue(sheepSlider.value);
        UpdateWolfValue(wolfSlider.value);
        UpdateDogValue(dogSlider.value);
        UpdatePastureValue(pastureSlider.value);
    }

    // Restart the simulation with the specified settings
    private void ConfirmAndRestart()
    {
        SimulationConfig.SheepCount = sheepOptions[Mathf.RoundToInt(sheepSlider.value)];
        SimulationConfig.WolfCount = wolfOptions[Mathf.RoundToInt(wolfSlider.value)];
        SimulationConfig.DogCount = dogOptions[Mathf.RoundToInt(dogSlider.value)];
        SimulationConfig.PastureSize = pastureOptions[Mathf.RoundToInt(pastureSlider.value)];

        SceneManager.LoadScene( SceneManager.GetActiveScene().buildIndex );
    }

    // Set the simulation settings to the given values
    private void ApplyPreset( int sheep, int wolves, int dogs, int pasture )
    {
        SimulationConfig.SheepCount = sheep;
        SimulationConfig.WolfCount = wolves;
        SimulationConfig.DogCount = dogs;
        SimulationConfig.PastureSize = pasture;

        SceneManager.LoadScene( SceneManager.GetActiveScene().buildIndex );
    }

    // Given the value, finds the integer index where that value exists in the options array
    private int FindOptionIndex(int[] options, int value)
    {
        for (int i = 0; i < options.Length; i++)
            if (options[i] == value)
                return i;

        return 0;
    }
}