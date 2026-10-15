using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_PropPreview
{
    [Plugin("PropPreview")]
    public class Plugin
    {
        // ChroMapper's map editor scene.
        private const int MapEditScene = 3;

        [Init]
        private void Init()
        {
            if (SelfTest.Run()) return;
            BaseProviders.Patch(new HarmonyLib.Harmony("ChroMapper-PropPreview"));
            ScriptedOpen.Start(out _);
            SceneManager.sceneLoaded += SceneLoaded;
        }

        // Every container ChroMapper spawns (notes, chains, walls, events...): note prefabs.
        [ObjectLoaded]
        private void ObjectLoaded(Beatmap.Containers.ObjectContainer container) =>
            PreviewController.Current?.ContainerLoaded(container);

        [Exit]
        private void Exit() => SceneManager.sceneLoaded -= SceneLoaded;

        // The controller lives in the editor scene and goes away with it. Environment scenes load
        // additively on top, so only the editor scene itself counts.
        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex != MapEditScene || mode != LoadSceneMode.Single) return;
            var host = new GameObject("PropPreview");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<PreviewController>();
        }
    }
}
