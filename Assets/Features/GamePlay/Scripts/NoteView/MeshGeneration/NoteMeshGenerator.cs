using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Note 程序化 Mesh 生成工具，所有形状统一双 Submesh（0=描边/底，1=填充/顶）
/// </summary>
public static class NoteMeshGenerator
{
        /// <summary>
        /// 构建双 Submesh 实心圆（三角形扇）
        /// Submesh 0（描边）：半径 radius+strokeWidth
        /// Submesh 1（填充）：半径 radius
        /// </summary>
        public static void BuildCircle(Mesh mesh, float radius, int segments, float strokeWidth)
        {
            int vertCount = 1 + 2 * segments;
            var verts = new Vector3[vertCount];
            verts[0] = Vector3.zero;

            float outerR = radius + strokeWidth;
            float angleStep = Mathf.PI * 2f / segments;

            for (int i = 0; i < segments; i++)
            {
                float a = i * angleStep;
                float x = Mathf.Cos(a);
                float y = Mathf.Sin(a);
                verts[1 + i] = new Vector3(x * outerR, y * outerR, 0);
                verts[1 + segments + i] = new Vector3(x * radius, y * radius, 0);
            }

            var strokeTris = new int[segments * 3];
            var fillTris = new int[segments * 3];

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                strokeTris[i * 3] = 0;
                strokeTris[i * 3 + 1] = 1 + next;
                strokeTris[i * 3 + 2] = 1 + i;

                fillTris[i * 3] = 0;
                fillTris[i * 3 + 1] = 1 + segments + next;
                fillTris[i * 3 + 2] = 1 + segments + i;
            }

            mesh.vertices = verts;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(strokeTris, 0);
            mesh.SetTriangles(fillTris, 1);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 构建双 Submesh 折线（polyline），每段一个 quad，转角处 miter join
        /// Submesh 0（描边）：线宽 width+2*strokeWidth
        /// Submesh 1（填充）：线宽 width
        /// </summary>
        public static void BuildPolyline(Mesh mesh, IReadOnlyList<Vector3> points, float width, float strokeWidth, float miterLimit)
        {
            float hwFill = width * 0.5f;
            float hwStroke = hwFill + strokeWidth;

            var vertsFill = new List<Vector3>();
            var trisFill = new List<int>();
            var vertsStroke = new List<Vector3>();
            var trisStroke = new List<int>();

            BuildPolylineSubmesh(vertsFill, trisFill, points, hwFill, miterLimit, 0f);
            BuildPolylineSubmesh(vertsStroke, trisStroke, points, hwStroke, miterLimit, strokeWidth);

            var allVerts = new List<Vector3>(vertsFill.Count + vertsStroke.Count);
            allVerts.AddRange(vertsFill);
            allVerts.AddRange(vertsStroke);

            var offsetTrisStroke = new int[trisStroke.Count];
            for (int i = 0; i < trisStroke.Count; i++)
                offsetTrisStroke[i] = trisStroke[i] + vertsFill.Count;

            mesh.vertices = allVerts.ToArray();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(offsetTrisStroke, 0);
            mesh.SetTriangles(trisFill.ToArray(), 1);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 构建双 Submesh ">" 形箭头
        /// Submesh 0（描边）：填充顶点沿 miter 方向外推 strokeWidth
        /// Submesh 1（填充）：原始尺寸，两个平行四边形 4 个三角形
        /// 顶点外推量由 GetArrowOutward 计算（相邻边法线交点），统一的线求交公式自动处理凸角/凹角
        /// </summary>
        public static void BuildArrow(Mesh mesh, float length, float width, float slant, float strokeWidth)
        {
            float halfW = width * 0.5f;

            var fv = new Vector3[6];
            fv[0] = new Vector3(-slant, -halfW);
            fv[1] = new Vector3(length - slant, -halfW);
            fv[2] = new Vector3(length, 0);
            fv[3] = new Vector3(length - slant, halfW);
            fv[4] = new Vector3(-slant, halfW);
            fv[5] = new Vector3(0, 0);

            var sv = new Vector3[6];
            for (int i = 0; i < 6; i++)
                sv[i] = fv[i] + (Vector3)(GetArrowOutward(fv, i) * strokeWidth);

            var verts = new Vector3[12];
            for (int i = 0; i < 6; i++) verts[i] = sv[i];
            for (int i = 0; i < 6; i++) verts[6 + i] = fv[i];

            int[] pattern = { 5, 3, 2, 5, 4, 3, 0, 2, 1, 0, 5, 2 };
            var sTris = new int[12];
            var fTris = new int[12];
            for (int t = 0; t < 12; t++)
            {
                sTris[t] = pattern[t];
                fTris[t] = pattern[t] + 6;
            }

            mesh.vertices = verts;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sTris, 0);
            mesh.SetTriangles(fTris, 1);
            mesh.RecalculateBounds();
        }

        // 返回 miter join 偏移方向（乘以 strokeWidth 后 = 两条偏移边的交点 − 原始顶点）
        private static Vector2 GetArrowOutward(Vector3[] verts, int i)
        {
            int prev = (i + 5) % 6;
            int next = (i + 1) % 6;

            Vector2 dIn = ((Vector2)(verts[i] - verts[prev])).normalized;
            Vector2 dOut = ((Vector2)(verts[next] - verts[i])).normalized;

            Vector2 nIn = new Vector2(dIn.y, -dIn.x);
            Vector2 nOut = new Vector2(dOut.y, -dOut.x);

            float det = nIn.x * nOut.y - nIn.y * nOut.x;
            if (Mathf.Abs(det) < 1e-6f)
                return new Vector2(-dIn.y, dIn.x);

            return new Vector2(
                (nOut.y - nIn.y) / det,
                (nIn.x - nOut.x) / det
            );
        }

        private struct JoinInfo
        {
            public bool isBevel;
            public Vector3 left0, right0;  // prev 侧（入段）or miter
            public Vector3 left1, right1;  // next 侧（出段）or unused
        }

        private static void BuildPolylineSubmesh(List<Vector3> verts, List<int> tris, IReadOnlyList<Vector3> points, float halfWidth, float miterLimit, float endCap)
        {
            // 滤掉连续重合点（阈值 > 精度误差，允许谱面写出重合点）
            var pts = new List<Vector3> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if ((points[i] - pts[pts.Count - 1]).sqrMagnitude > 1e-4f)
                    pts.Add(points[i]);
            }
            int N = pts.Count;
            if (N < 2) return;

            float miterMax = miterLimit * halfWidth;

            // 计算每个中间点的 join 类型
            var joins = new JoinInfo[N];
            for (int i = 1; i < N - 1; i++)
            {
                Vector3 p = pts[i];
                Vector3 dPrev = (p - pts[i - 1]).normalized;
                Vector3 dNext = (pts[i + 1] - p).normalized;
                Vector3 nPrev = new Vector3(-dPrev.y, dPrev.x, 0);
                Vector3 nNext = new Vector3(-dNext.y, dNext.x, 0);

                Vector3 miterDir = nPrev + nNext;
                bool bevel = false;

                if (miterDir.sqrMagnitude < 1e-6f)
                {
                    bevel = true;
                }
                else
                {
                    miterDir.Normalize();
                    float dot = Vector3.Dot(miterDir, nPrev);
                    if (Mathf.Abs(dot) < 1e-6f)
                        bevel = true;
                    else if (Mathf.Abs(halfWidth / dot) > miterMax)
                        bevel = true;
                    else
                    {
                        joins[i] = new JoinInfo
                        {
                            isBevel = false,
                            left0 = p + miterDir * (halfWidth / dot),
                            right0 = p - miterDir * (halfWidth / dot)
                        };
                    }
                }

                if (bevel)
                {
                    joins[i] = new JoinInfo
                    {
                        isBevel = true,
                        left0 = p + nPrev * halfWidth,
                        right0 = p - nPrev * halfWidth,
                        left1 = p + nNext * halfWidth,
                        right1 = p - nNext * halfWidth
                    };
                    // bevel 填充三角形
                    int bv = verts.Count;
                    verts.Add(joins[i].left0);
                    verts.Add(joins[i].right0);
                    verts.Add(joins[i].left1);
                    verts.Add(joins[i].right1);
                    verts.Add(p);
                    // left gap: left0 → left1 → p
                    tris.Add(bv + 0); tris.Add(bv + 2); tris.Add(bv + 4);
                    // right gap: right1 → right0 → p
                    tris.Add(bv + 3); tris.Add(bv + 1); tris.Add(bv + 4);
                }
            }

            // 逐段生成 quad
            for (int k = 0; k < N - 1; k++)
            {
                Vector3 p0 = pts[k];
                Vector3 p1 = pts[k + 1];
                Vector3 d = (p1 - p0).normalized;
                Vector3 n = new Vector3(-d.y, d.x, 0);

                Vector3 leftStart, rightStart;
                Vector3 leftEnd, rightEnd;

                // 段起点
                if (k == 0)
                {
                    leftStart = p0 + n * halfWidth - d * endCap;
                    rightStart = p0 - n * halfWidth - d * endCap;
                }
                else if (joins[k].isBevel)
                {
                    leftStart = joins[k].left1;
                    rightStart = joins[k].right1;
                }
                else
                {
                    leftStart = joins[k].left0;
                    rightStart = joins[k].right0;
                }

                // 段终点
                if (k == N - 2)
                {
                    leftEnd = p1 + n * halfWidth + d * endCap;
                    rightEnd = p1 - n * halfWidth + d * endCap;
                }
                else if (joins[k + 1].isBevel)
                {
                    leftEnd = joins[k + 1].left0;
                    rightEnd = joins[k + 1].right0;
                }
                else
                {
                    leftEnd = joins[k + 1].left0;
                    rightEnd = joins[k + 1].right0;
                }

                int idx = verts.Count;
                verts.Add(leftStart);
                verts.Add(rightStart);
                verts.Add(leftEnd);
                verts.Add(rightEnd);

                tris.Add(idx);
                tris.Add(idx + 2);
                tris.Add(idx + 1);
                tris.Add(idx + 2);
                tris.Add(idx + 3);
                tris.Add(idx + 1);
            }
        }
}
