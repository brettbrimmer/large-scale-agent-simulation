using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OpenDefaultScene
{
    static OpenDefaultScene()
    {
        EditorApplication.delayCall += OpenSceneIfUntitled;
    }

    private static void OpenSceneIfUntitled()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        // Make sure fresh clones open HerdArea scene
        if (activeScene.path == "")
        {
            EditorSceneManager.OpenScene("Assets/Scenes/HerdArea.unity");
        }
    }
}