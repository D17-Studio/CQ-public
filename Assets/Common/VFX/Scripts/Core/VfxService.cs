using UnityEngine;

namespace VFXTest.Vfx
{
    /// <summary>
    /// 特效系统对外服务。由 Zenject 绑定为单例后注入使用。
    /// </summary>
    public sealed class VfxService
    {
        readonly VfxRuntime runtime;

        /// <summary>
        /// 用指定的运行时创建服务。
        /// </summary>
        public VfxService(VfxRuntime runtime)
        {
            this.runtime = runtime;
        }

        /// <summary>catalog 是否加载成功。</summary>
        public bool IsReady => runtime != null && runtime.Catalog != null;

        /// <summary>当前统计快照。</summary>
        public VfxStats Stats => runtime != null ? runtime.GetStats() : default;

        /// <summary>无障碍"减弱动效"开关。</summary>
        public bool ReducedMotion
        {
            get => runtime != null && runtime.ReducedMotion;
            set
            {
                if (runtime != null)
                {
                    runtime.ReducedMotion = value;
                }
            }
        }

        /// <summary>按事件规则播放，返回成功播放的条数。</summary>
        public int Signal(VfxSignal signal)
        {
            return runtime != null ? runtime.Signal(signal) : 0;
        }

        /// <summary>按请求播放，返回句柄（可能为 null）。</summary>
        public VfxHandle Play(VfxRequest request)
        {
            return runtime != null ? runtime.Play(request) : null;
        }

        /// <summary>最简播放。</summary>
        public VfxHandle Play(string cueId, Vector3 position)
        {
            return Play(new VfxRequest(cueId).At(position));
        }

        /// <summary>跟随目标播放。</summary>
        public VfxHandle PlayAt(Transform follow, string cueId)
        {
            var request = new VfxRequest(cueId);
            if (follow != null)
            {
                request.Follow(follow);
            }

            return Play(request);
        }

        /// <summary>停止所有活跃特效。</summary>
        public void StopAll()
        {
            runtime?.StopAll();
        }
    }
}
