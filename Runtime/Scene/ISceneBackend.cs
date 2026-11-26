using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace EasyFramework
{
    /// <summary>提供 Unity 主线程使用的场景解析、叠加加载、激活和卸载能力。</summary>
    public interface ISceneBackend
    {
        /// <summary>底层场景实际卸载后发出的通知，包括外部发起的卸载。</summary>
        event Action<Scene> SceneUnloaded;

        /// <summary>当前底层活动场景。</summary>
        Scene ActiveScene { get; }

        /// <summary>已完成加载的常规场景数量，包括运行时创建的空场景。</summary>
        int LoadedSceneCount { get; }

        /// <summary>将场景名或路径解析为后端可加载的稳定路径。</summary>
        /// <param name="sceneNameOrPath">场景名称或完整路径。</param>
        /// <returns>场景的规范路径。</returns>
        string ResolvePath(string sceneNameOrPath);

        /// <summary>按规范路径查找已加载且可唯一定位的场景。</summary>
        /// <param name="path">后端解析后的规范路径。</param>
        /// <param name="scene">查到的场景，找不到时为默认值。</param>
        /// <returns>能否定位到已加载的场景。</returns>
        bool TryGetLoadedScene(string path, out Scene scene);

        /// <summary>提交叠加加载，等底层操作完成后返回本次创建的场景。</summary>
        /// <param name="scenePath">后端解析后的规范路径。</param>
        /// <param name="progress">可选的进度接收方，值在零到一之间。</param>
        /// <returns>新加载的场景和可用于清理的场景句柄。</returns>
        Task<Scene> LoadAdditiveAsync(string scenePath, IProgress<float> progress);

        /// <summary>将一个已加载场景设为底层活动场景。</summary>
        /// <param name="scene">要设为活动的场景。</param>
        /// <returns>底层是否接受本次激活。</returns>
        bool SetActiveScene(Scene scene);

        /// <summary>创建可承接卸载最后一个场景的空场景。</summary>
        /// <returns>可设为活动状态的临时场景句柄。</returns>
        Scene CreateStandbyScene();

        /// <summary>按句柄卸载场景，等底层实际卸载后结束。</summary>
        /// <param name="scene">需要卸载的场景句柄。</param>
        /// <param name="progress">可选的进度接收方，值在零到一之间。</param>
        /// <returns>此场景卸载完成的任务。</returns>
        Task UnloadAsync(Scene scene, IProgress<float> progress);
    }
}
