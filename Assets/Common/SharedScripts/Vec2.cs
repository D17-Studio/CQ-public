using System;

namespace CQMusicGame.Shared
{
    /// <summary>
    /// 二维向量结构体
    /// </summary>
    public struct Vec2
    {
        public float x;
        public float y;

        public Vec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 v) => new(-v.x, -v.y);
        public static Vec2 operator *(Vec2 v, float scalar) => new(v.x * scalar, v.y * scalar);
        public static Vec2 operator *(float scalar, Vec2 v) => v * scalar;

        /// <summary>
        /// 绕原点逆时针旋转（度）
        /// </summary>
        public Vec2 Rotate(float degrees)
        {
            float rad = (float)(degrees * Math.PI / 180.0);
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return new Vec2(x * cos - y * sin, x * sin + y * cos);
        }

        public static readonly Vec2 Zero = new(0, 0);
    }
}

