using System.Collections;
using System.Text;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 量墨路的「寬度剖面」而不是只量有沒有墨。
    // RoadAutoTest 用 ±0.6m 的窗、任一點有墨就算過,量的是存在性;
    // 玩家看到的斷點其實是「路變太細」,必須逐距離量寬度才找得出來。
    public sealed class RoadProfileProbe : MonoBehaviour
    {
        private sealed class FireIntent : IPlayerIntentSource
        {
            public Vector2 MoveInput => Vector2.zero;
            public Vector2 LookDeltaDeg => Vector2.zero;
            public bool JumpPressedThisFrame => false;
            public bool AttackHeld { get; set; }
            public bool SquidHeld => false;
        }

        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[PROBE] 需在 Play mode 中執行");
                return;
            }
            var host = new GameObject("RoadProfileProbe");
            host.AddComponent<RoadProfileProbe>();
        }

        private IEnumerator Start()
        {
            var player = GameObject.Find("Player");
            var router = player.GetComponent<PlayerInputRouter>();
            var controller = player.GetComponent<CharacterController>();
            var rig = Camera.main.GetComponent<ThirdPersonCameraRig>();
            PaintableSurface ground = null;
            foreach (var surface in PaintableSurface.Active)
            {
                if (surface.name == "Ground")
                {
                    ground = surface;
                }
            }
            player.GetComponent<PlayerInkTank>().InfiniteInk = true;

            var intent = new FireIntent();
            router.SetOverrideSource(intent);

            float[] pitches = { 8f, 20f };
            for (int index = 0; index < pitches.Length; index++)
            {
                float pitch = pitches[index];
                controller.enabled = false;
                // 每個俯角換一塊乾淨地面,但必須留在 50x50 的 Ground 範圍內
                player.transform.position = new Vector3(-16f + index * 8f, 0.1f, 18f);
                controller.enabled = true;
                rig.SetAngles(180f, pitch);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                }

                Vector3 origin = player.transform.position;
                intent.AttackHeld = true;
                yield return new WaitForSeconds(0.5f);
                intent.AttackHeld = false;
                yield return new WaitForSeconds(2f);

                // 逐距離量寬度:橫向 ±1.5m 每 0.25m 取樣一次(13 點)
                var profile = new StringBuilder();
                float thinnestWidth = 99f;
                float thinnestAt = -1f;
                for (float d = 1f; d <= 18f; d += 0.5f)
                {
                    int inked = 0;
                    for (float lateral = -1.5f; lateral <= 1.51f; lateral += 0.25f)
                    {
                        var probe = new Vector3(origin.x + lateral, 0f, origin.z - d);
                        if (ground.SampleOwnership(probe) == 1)
                        {
                            inked++;
                        }
                    }
                    float width = inked * 0.25f;
                    profile.Append($"{d:F1}:{width:F2} ");
                    if (width < thinnestWidth)
                    {
                        thinnestWidth = width;
                        thinnestAt = d;
                    }
                }

                Debug.Log($"[PROBE] pitch={pitch:F0}° 路寬剖面(距離:寬度m) {profile}");
                Debug.Log($"[PROBE] pitch={pitch:F0}° 最細處在 {thinnestAt:F1}m,寬度僅 {thinnestWidth:F2}m");
            }

            router.ClearOverrideSource();
            Debug.Log("[PROBE] DONE 路寬剖面量測");
            Destroy(gameObject);
        }
    }
}
