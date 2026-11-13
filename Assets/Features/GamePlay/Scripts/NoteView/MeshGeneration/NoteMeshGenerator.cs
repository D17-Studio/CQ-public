using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Note 程序化 Mesh 生成工具，所有形状统一双 Submesh（0=描边/底，1=填充/顶）
/// </summary>
public static class NoteMeshGenerator
{
        /// <summary>折点圆补角的分段数</summary>
        private const int CircleSegments = 32;

        /// <summary>
        /// 构建双 Submesh 实心圆（描边为环形，与填充不重叠）
        /// Submesh 0（描边）：内径 radius、外径 radius+strokeWidth 的圆环
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

            var strokeTris = new int[segments * 6];
            var fillTris = new int[segments * 3];

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int outerCur = 1 + i;
                int outerNext = 1 + next;
                int innerCur = 1 + segments + i;
                int innerNext = 1 + segments + next;

                // 描边：内外环之间的四边形，与填充共享内环顶点
                strokeTris[i * 6] = outerCur;
                strokeTris[i * 6 + 1] = innerNext;
                strokeTris[i * 6 + 2] = outerNext;
                strokeTris[i * 6 + 3] = outerCur;
                strokeTris[i * 6 + 4] = innerCur;
                strokeTris[i * 6 + 5] = innerNext;

                fillTris[i * 3] = 0;
                fillTris[i * 3 + 1] = innerNext;
                fillTris[i * 3 + 2] = innerCur;
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
        /// 构建圆角双 Submesh 折线：线段平切，折点用圆补角（不使用 miter/bevel）
        /// Submesh 0（描边）：线宽 width+2*strokeWidth
        /// Submesh 1（填充）：线宽 width
        /// </summary>
        public static void BuildPolyline(Mesh mesh, IReadOnlyList<Vector3> points, float width, float strokeWidth)
        {
            float hwFill = width * 0.5f;
            float hwStroke = hwFill + strokeWidth;

            var vertsFill = new List<Vector3>();
            var trisFill = new List<int>();
            var vertsStroke = new List<Vector3>();
            var trisStroke = new List<int>();

            BuildSegmentSubmesh(vertsFill, trisFill, points, hwFill, 0f);
            BuildSegmentSubmesh(vertsStroke, trisStroke, points, hwStroke, strokeWidth);

            AppendJointCircles(vertsFill, trisFill, points, hwFill);
            AppendJointCircles(vertsStroke, trisStroke, points, hwStroke);

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

        /// <summary>
        /// 仅生成线段 quad：内部折点平切，整条折线首尾按 endCap 延长
        /// </summary>
        private static void BuildSegmentSubmesh(List<Vector3> verts, List<int> tris, IReadOnlyList<Vector3> points, float halfWidth, float endCap)
        {
            var pts = FilterCoincident(points);
            int N = pts.Count;
            if (N < 2) return;

            for (int k = 0; k < N - 1; k++)
            {
                Vector3 p0 = pts[k];
                Vector3 p1 = pts[k + 1];
                Vector3 d = (p1 - p0).normalized;
                Vector3 n = new Vector3(-d.y, d.x, 0);

                Vector3 start = k == 0 ? p0 - d * endCap : p0;
                Vector3 end = k == N - 2 ? p1 + d * endCap : p1;

                int idx = verts.Count;
                verts.Add(start + n * halfWidth);
                verts.Add(start - n * halfWidth);
                verts.Add(end + n * halfWidth);
                verts.Add(end - n * halfWidth);

                tris.Add(idx);
                tris.Add(idx + 2);
                tris.Add(idx + 1);
                tris.Add(idx + 2);
                tris.Add(idx + 3);
                tris.Add(idx + 1);
            }
        }

        /// <summary>
        /// 在每个内部折点追加实心圆，填补平切线段留下的缺角
        /// </summary>
        private static void AppendJointCircles(List<Vector3> verts, List<int> tris, IReadOnlyList<Vector3> points, float radius)
        {
            var pts = FilterCoincident(points);
            for (int i = 1; i < pts.Count - 1; i++)
                AppendCircle(verts, tris, pts[i], radius);
        }

        private static void AppendCircle(List<Vector3> verts, List<int> tris, Vector3 center, float radius)
        {
            int centerIndex = verts.Count;
            verts.Add(center);

            float step = Mathf.PI * 2f / CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a = i * step;
                verts.Add(center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }

            for (int i = 0; i < CircleSegments; i++)
            {
                int cur = centerIndex + 1 + i;
                int next = centerIndex + 1 + (i + 1) % CircleSegments;
                tris.Add(centerIndex);
                tris.Add(next);
                tris.Add(cur);
            }
        }

        private static List<Vector3> FilterCoincident(IReadOnlyList<Vector3> points)
        {
            var pts = new List<Vector3> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if ((points[i] - pts[pts.Count - 1]).sqrMagnitude > 1e-4f)
                    pts.Add(points[i]);
            }
            return pts;
        }
}
