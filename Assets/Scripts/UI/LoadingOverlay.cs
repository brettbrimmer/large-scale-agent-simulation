using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Displays the loading overlay while the simulation is loading
public class LoadingOverlay : MonoBehaviour
{
    public bool IsLoading { get; set; } = true;
    public float Progress { get; set; } = 0f;

    public GameObject loadingPanel;
    public Image progressBarFill;
    public TMP_Text progressText;

    private void Update()
    {
        loadingPanel.SetActive(IsLoading);

        if (IsLoading == false)
            return;

        progressBarFill.fillAmount = Progress;

        progressText.text = Mathf.RoundToInt(Progress * 100f) + "%";
    }
}