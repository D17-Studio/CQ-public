using System;
namespace CQMusicGame.Shared
{
    /// <summary>
    /// 缓动曲线类型枚举
    /// </summary>
    public enum EaseCurve
    {
        Linear,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InSine, OutSine, InOutSine,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InBack, OutBack, InOutBack
    }

    /// <summary>
    /// 缓动曲线工具类
    /// 所有函数接受 0-1 的输入，返回 0-1 的输出
    /// 参考：https://easings.net
    /// </summary>
    public static class Easing
    {
        /// <summary>
        /// 根据枚举类型执行对应的缓动函数
        /// </summary>
        /// <param name="curve">缓动类型</param>
        /// <param name="t">时间进度 (0-1)</param>
        /// <returns>缓动后的值 (0-1)</returns>
        public static float Apply(EaseCurve curve, float t)
        {
            return curve switch
            {
                EaseCurve.Linear => Linear(t),
                EaseCurve.InQuad => InQuad(t),
                EaseCurve.OutQuad => OutQuad(t),
                EaseCurve.InOutQuad => InOutQuad(t),
                EaseCurve.InCubic => InCubic(t),
                EaseCurve.OutCubic => OutCubic(t),
                EaseCurve.InOutCubic => InOutCubic(t),
                EaseCurve.InQuart => InQuart(t),
                EaseCurve.OutQuart => OutQuart(t),
                EaseCurve.InOutQuart => InOutQuart(t),
                EaseCurve.InQuint => InQuint(t),
                EaseCurve.OutQuint => OutQuint(t),
                EaseCurve.InOutQuint => InOutQuint(t),
                EaseCurve.InSine => InSine(t),
                EaseCurve.OutSine => OutSine(t),
                EaseCurve.InOutSine => InOutSine(t),
                EaseCurve.InExpo => InExpo(t),
                EaseCurve.OutExpo => OutExpo(t),
                EaseCurve.InOutExpo => InOutExpo(t),
                EaseCurve.InCirc => InCirc(t),
                EaseCurve.OutCirc => OutCirc(t),
                EaseCurve.InOutCirc => InOutCirc(t),
                EaseCurve.InBack => InBack(t),
                EaseCurve.OutBack => OutBack(t),
                EaseCurve.InOutBack => InOutBack(t),
                _ => t
            };
        }

        #region 瞬间变化

        public static float InStep(float t) => 1;
        
        public static float OutStep(float t) => 0;

        public static float InOutStep(float t)
        {
            if (t < 0.5)
            {
                return 0;
            }
            else
            {
                return 1;
            }
        }

        #endregion

        #region 基础函数

        /// <summary>
        /// 线性运动
        /// </summary>
        public static float Linear(float t) => t;

        #endregion

        #region 二次函数

        /// <summary>
        /// 二次缓动 - 加速
        /// </summary>
        public static float InQuad(float t) => t * t;

        /// <summary>
        /// 二次缓动 - 减速
        /// </summary>
        public static float OutQuad(float t) => t * (2 - t);

        /// <summary>
        /// 二次缓动 - 先加速后减速
        /// </summary>
        public static float InOutQuad(float t) => t < 0.5f ? 2 * t * t : 1 - (float)Math.Pow(-2 * t + 2, 2) / 2;

        #endregion

        #region 三次函数

        /// <summary>
        /// 三次缓动 - 加速
        /// </summary>
        public static float InCubic(float t) => t * t * t;

        /// <summary>
        /// 三次缓动 - 减速
        /// </summary>
        public static float OutCubic(float t) => 1 - (float)Math.Pow(1 - t, 3);

        /// <summary>
        /// 三次缓动 - 先加速后减速
        /// </summary>
        public static float InOutCubic(float t) => t < 0.5f ? 4 * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 3) / 2;

        #endregion

        #region 四次函数

        /// <summary>
        /// 四次缓动 - 加速
        /// </summary>
        public static float InQuart(float t) => t * t * t * t;

        /// <summary>
        /// 四次缓动 - 减速
        /// </summary>
        public static float OutQuart(float t) => 1 - (float)Math.Pow(1 - t, 4);

        /// <summary>
        /// 四次缓动 - 先加速后减速
        /// </summary>
        public static float InOutQuart(float t) => t < 0.5f ? 8 * t * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 4) / 2;

        #endregion

        #region 五次函数

        /// <summary>
        /// 五次缓动 - 加速
        /// </summary>
        public static float InQuint(float t) => t * t * t * t * t;

        /// <summary>
        /// 五次缓动 - 减速
        /// </summary>
        public static float OutQuint(float t) => 1 - (float)Math.Pow(1 - t, 5);

        /// <summary>
        /// 五次缓动 - 先加速后减速
        /// </summary>
        public static float InOutQuint(float t) => t < 0.5f ? 16 * t * t * t * t * t : 1 - (float)Math.Pow(-2 * t + 2, 5) / 2;

        #endregion

        #region 正弦函数

        /// <summary>
        /// 正弦缓动 - 加速
        /// </summary>
        public static float InSine(float t) => 1 - (float)Math.Cos(t * Math.PI / 2);

        /// <summary>
        /// 正弦缓动 - 减速
        /// </summary>
        public static float OutSine(float t) => (float)Math.Sin(t * Math.PI / 2);

        /// <summary>
        /// 正弦缓动 - 先加速后减速
        /// </summary>
        public static float InOutSine(float t) => 1 - (float)Math.Cos(Math.PI * t) / 2;

        #endregion

        #region 指数函数

        /// <summary>
        /// 指数缓动 - 加速
        /// </summary>
        public static float InExpo(float t) => t == 0 ? 0 : (float)Math.Pow(2, 10 * t - 10);

        /// <summary>
        /// 指数缓动 - 减速
        /// </summary>
        public static float OutExpo(float t) => t == 1 ? 1 : 1 - (float)Math.Pow(2, -10 * t);

        /// <summary>
        /// 指数缓动 - 先加速后减速
        /// </summary>
        public static float InOutExpo(float t)
        {
            if (t == 0) return 0;
            if (t == 1) return 1;
            return t < 0.5f ? (float)Math.Pow(2, 20 * t - 10) / 2 : (2 - (float)Math.Pow(2, -20 * t + 10)) / 2;
        }

        #endregion

        #region 圆形函数

        /// <summary>
        /// 圆形缓动 - 加速
        /// </summary>
        public static float InCirc(float t) => 1 - (float)Math.Sqrt(1 - Math.Pow(t, 2));

        /// <summary>
        /// 圆形缓动 - 减速
        /// </summary>
        public static float OutCirc(float t) => (float)Math.Sqrt(1 - Math.Pow(t - 1, 2));

        /// <summary>
        /// 圆形缓动 - 先加速后减速
        /// </summary>
        public static float InOutCirc(float t) => t < 0.5f
            ? (1 - (float)Math.Sqrt(1 - Math.Pow(2 * t, 2))) / 2
            : ((float)Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;

        #endregion

        #region 回弹函数 (Back)

        private const float C1 = 1.70158f;
        private const float C2 = C1 * 1.525f;
        private const float C3 = C1 + 1;

        /// <summary>
        /// 回弹缓动 - 加速（先反向回弹再前进）
        /// </summary>
        public static float InBack(float t) => C3 * t * t * t - C1 * t * t;

        /// <summary>
        /// 回弹缓动 - 减速（超过目标再回弹）
        /// </summary>
        public static float OutBack(float t) => 1 + C3 * (float)Math.Pow(t - 1, 3) + C1 * (float)Math.Pow(t - 1, 2);

        /// <summary>
        /// 回弹缓动 - 先加速后减速
        /// </summary>
        public static float InOutBack(float t) => t < 0.5f
            ? (float)(Math.Pow(2 * t, 2) * ((C2 + 1) * 2 * t - C2)) / 2
            : (float)(Math.Pow(2 * t - 2, 2) * ((C2 + 1) * (t * 2 - 2) + C2) + 2) / 2;

        #endregion
    }
}

