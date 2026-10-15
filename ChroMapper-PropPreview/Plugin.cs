using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_PropPreview
{
    [Plugin("PropPreview")]
    public class Plugin
    {
        // ChroMapper's map editor scene.
        private const int MapEditScene = 3;

        private BundleLoader? loader;

        [Init]
        private void Init() => SceneManager.sceneLoaded += SceneLoaded;

        [Exit]
        private void Exit()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            loader?.Unload();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            loader?.Unload();
            loader = null;
            if (scene.buildIndex != MapEditScene) return;
            try
            {
                loader = BundleLoader.ForMap(BeatSaberSongContainer.Instance.Info.Directory);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
