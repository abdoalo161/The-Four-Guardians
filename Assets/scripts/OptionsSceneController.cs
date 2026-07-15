using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class OptionsSceneController : MonoBehaviour
{
    [SerializeField] private Button backButton;
    [SerializeField] private string backSceneName = "MainMenuScene";

    private void OnEnable()
    {
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnDisable()
    {
        if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
    }

    private void OnBackClicked()
    {
        // Persist any pending option changes
        if (OptionsManager.Instance != null)
        {
            OptionsManager.Instance.SaveSettings();
            OptionsManager.Instance.ApplySettings();
        }
        SceneManager.LoadScene(backSceneName);
    }
}
