using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.AI;

namespace SplatoonC.Gameplay.AI
{
    // bot 共用的地盤取樣點:從 NavMesh 三角化取「走得到的地面」均勻撒點,每點記住底下的可塗表面。
    // bot 以它挑塗地目標(沒塗/敵墨的點)、找回墨點(自家墨),不用每次射線掃場地。
    public sealed class TurfMap
    {
        public struct Sample
        {
            public Vector3 Position;
            public PaintableSurface Surface;
            // 0 = 己方(Alpha)基地端、1 = 對方(Bravo)基地端(沿長軸正規化)
            public float Progress;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private float _minZ;
        private float _maxZ;

        public IReadOnlyList<Sample> Samples => _samples;

        public static TurfMap Build(float spacing = 3f)
        {
            var map = new TurfMap();
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                Debug.LogWarning("TurfMap:場景沒有 NavMesh,bot 無法選塗地目標");
                return map;
            }
            var grid = new HashSet<Vector3Int>();
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = tri.vertices[tri.indices[i]];
                Vector3 b = tri.vertices[tri.indices[i + 1]];
                Vector3 c = tri.vertices[tri.indices[i + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                int count = Mathf.Clamp(Mathf.CeilToInt(area / (spacing * spacing)), 1, 400);
                for (int k = 0; k < count; k++)
                {
                    // 確定性的三角形內均勻點(黃金比序列),同一張 NavMesh 每次結果相同
                    float r1 = Mathf.Repeat(0.5f + k * 0.618034f, 1f);
                    float r2 = Mathf.Repeat(0.5f + k * 0.754878f, 1f);
                    if (r1 + r2 > 1f)
                    {
                        r1 = 1f - r1;
                        r2 = 1f - r2;
                    }
                    Vector3 p = a + (b - a) * r1 + (c - a) * r2;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x / spacing), Mathf.RoundToInt(p.y / 1.2f),
                        Mathf.RoundToInt(p.z / spacing));
                    if (!grid.Add(key))
                    {
                        continue;
                    }
                    if (!Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 1.5f,
                            ~GameLayers.InklingMask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    var surface = hit.collider.GetComponent<PaintableSurface>();
                    if (surface == null)
                    {
                        continue;
                    }
                    map._samples.Add(new Sample { Position = hit.point, Surface = surface });
                    minZ = Mathf.Min(minZ, hit.point.z);
                    maxZ = Mathf.Max(maxZ, hit.point.z);
                }
            }
            map._minZ = minZ;
            map._maxZ = maxZ;
            for (int i = 0; i < map._samples.Count; i++)
            {
                Sample s = map._samples[i];
                s.Progress = Mathf.InverseLerp(minZ, maxZ, s.Position.z);
                map._samples[i] = s;
            }
            return map;
        }

        public byte OwnerAt(int index)
        {
            Sample s = _samples[index];
            return s.Surface != null ? s.Surface.SampleOwnership(s.Position, Vector3.up) : (byte)0;
        }

        // 某隊視角的推進度:0 = 自家基地、1 = 敵方基地
        public float ProgressFor(int index, Team team)
        {
            float p = _samples[index].Progress;
            return team == Team.Bravo ? 1f - p : p;
        }
    }
}
