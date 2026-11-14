using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 第三阶段的开场过场（挂在 Chess 物体上，和 ChessGameController 放在一起；没有它就还是原来的沙尘风暴）。
    ///
    ///   镜头 1：贴着棋盘对准朋友的王，王身上放特效，镜头往上摇看天空，乌云聚起来、天变暗。
    ///   镜头 2：镜头跟着陨石往下砸，穿过云层，砸进棋盘变成岩浆坑；冲击把棋子吹到第三阶段的位置，火球溅到其他岩浆格。
    ///   镜头 3：山一层一层升起来，棋子跟着升。
    ///   镜头 4：王和王后跑上山、逃进城堡。
    ///   最后镜头平滑回到第三阶段的游戏视角。
    ///
    /// 逻辑层在过场开始前就把第三阶段定好了（地形、岩浆、棋子位置），过场只负责一步一步演出来，
    /// 所以每局陨石砸的格子、王后逃跑的路线都不一样。按空格或 Esc 跳过，直接跳到最后的样子。
    /// 坐标都是棋盘根物体（BoardView.Root）的本地坐标：格子 (x, y) 的中心在 (x - 3.5, 0, y - 3.5)，一格 = 1。
    /// </summary>
    public class Stage3Cutscene : MonoBehaviour
    {
        [Tooltip("关掉就不播过场，用原来的沙尘风暴")]
        public bool play = true;
        [Tooltip("可以按空格 / Esc 跳过")]
        public bool allowSkip = true;
        [Tooltip("过场里相机的视野角")]
        [Range(20f, 90f)] public float fieldOfView = 50f;
        [Tooltip("过场演完后，镜头回到游戏视角用多久")]
        public float blendBackDuration = 1.2f;

        [Header("Music")]
        [Tooltip("过场开始时播放（stage3.wav）。跳过过场不会停")]
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = 1f;
        public bool loopMusic;

        [Header("Shot 1: king casts, camera tilts up, clouds form")]
        [Tooltip("王身上放的特效（PlasmaExplosionEffect）")]
        public GameObject kingEffect;
        public float kingEffectScale = 0.35f;
        [Tooltip("特效在王脚下往上多高")]
        public float kingEffectLift = 0.4f;
        [Tooltip("相机离王多远（往玩家那边退）")]
        public float shot1Distance = 2.2f;
        [Tooltip("相机离棋盘多高")]
        public float shot1Height = 0.45f;
        [Tooltip("相机往右偏多少")]
        public float shot1Side = 0.5f;
        [Tooltip("相机看着王身上多高的位置")]
        public float shot1LookHeight = 0.4f;
        [Tooltip("特效出现前先停多久")]
        public float shot1Lead = 0.6f;
        [Tooltip("特效放完多久后镜头开始往上摇")]
        public float kingEffectHold = 1.4f;
        [Tooltip("往上摇用多久")]
        public float tiltDuration = 2.5f;
        [Tooltip("摇到最后仰头多少度（90 = 正上方）")]
        [Range(0f, 89f)] public float tiltUpAngle = 65f;
        [Tooltip("摇的同时相机往上抬多少")]
        public float tiltRise = 0.3f;
        [Tooltip("乌云从无到有用多久（和往上摇同时开始），同时灯光从白天换成第三阶段的聚光灯")]
        public float cloudFormDuration = 3f;
        [Tooltip("过场里云的不透明度（0–1）。游戏里为了看清棋子云很淡，从下往上看要浓一些")]
        [Range(0f, 1f)] public float cloudOpacity = 0.8f;
        [Tooltip("看着天空停多久")]
        public float skyHold = 1f;

        [Header("Shot 2: meteor")]
        [Tooltip("陨石模型，留空就用一个深色球")]
        public GameObject meteorModel;
        [Tooltip("陨石大小（世界单位）")]
        public float meteorSize = 0.6f;
        [Tooltip("陨石身上的火（FireBall），火苗朝着陨石飞的反方向")]
        public GameObject meteorFire;
        public float meteorFireScale = 0.35f;
        [Tooltip("陨石拖的烟（SmokeEffect），留空就没有")]
        public GameObject meteorTrail;
        public float meteorTrailScale = 0.3f;
        [Tooltip("陨石从砸落点的哪里出发：x 左右、y 高度、z 前后（负数 = 玩家这边）")]
        public Vector3 meteorStartOffset = new Vector3(-2f, 14f, -7f);
        [Tooltip("陨石落下用多久")]
        public float fallDuration = 2.2f;
        [Tooltip("越落越快：1 = 匀速，越大最后越快")]
        [Range(1f, 3f)] public float fallAcceleration = 1.6f;
        [Tooltip("相机跟在陨石后面多远")]
        public float followDistance = 2.5f;
        [Tooltip("相机比陨石高多少")]
        public float followHeight = 0.8f;
        [Tooltip("陨石落到这个高度以下时相机停住，看着它砸下去")]
        public float cameraStopHeight = 3f;
        [Tooltip("砸到棋盘时的爆炸（BigExplosion）")]
        public GameObject impactEffect;
        public float impactScale = 0.5f;
        [Tooltip("保留爆炸特效里的冲击波扭曲圈（很大，会盖住一片棋盘）")]
        public bool impactShockwave;
        [Tooltip("砸下去时镜头震多厉害、震多久")]
        public float impactShake = 0.25f;
        public float impactShakeDuration = 0.8f;
        [Tooltip("棋子被冲击波吹到新位置用多久")]
        public float scatterDuration = 0.9f;
        [Tooltip("溅到其他岩浆格的小火球（FireBall），留空就直接冒出岩浆")]
        public GameObject splashEffect;
        public float splashScale = 0.12f;
        [Tooltip("小火球飞多久、飞多高")]
        public float splashDuration = 0.8f;
        public float splashHeight = 2f;
        [Tooltip("砸完后停多久")]
        public float shot2Hold = 1.5f;
        [Tooltip("砸完后云慢慢淡到这个不透明度（之后镜头从上面看，云太浓会挡住棋盘）")]
        [Range(0f, 1f)] public float cloudOpacityAfterImpact = 0.3f;

        [Header("Shot 3: mountains rise")]
        [Tooltip("每升一级用多久")]
        public float risePerLevel = 0.6f;
        [Tooltip("每级升完停多久")]
        public float levelPause = 0.15f;
        [Tooltip("每升一级冒的尘土（DustExplosion），留空就没有")]
        public GameObject riseDust;
        public float riseDustScale = 0.25f;
        [Tooltip("每升一级最多在几格上冒尘土")]
        public int riseDustPerLevel = 5;
        public float riseShake = 0.06f;
        [Tooltip("相机从山的哪个方向看过去（会自动归一化）：x 右、y 上、z 前（负数 = 玩家这边）")]
        public Vector3 shot3Direction = new Vector3(1f, 0.6f, -1.2f);
        [Tooltip("相机离山多远：开始和结束（慢慢推近）")]
        public Vector2 shot3Distance = new Vector2(8f, 6.5f);
        [Tooltip("升完后停多久")]
        public float shot3Hold = 0.6f;

        [Header("Shot 4: king and queen flee to the castle")]
        [Tooltip("每跳一格用多久")]
        public float hopDuration = 0.28f;
        [Tooltip("每跳一格跳多高（往上爬时再加上高度差）")]
        public float hopHeight = 0.35f;
        [Tooltip("王后比王晚出发多久")]
        public float queenDelay = 0.15f;
        [Tooltip("相机跟在两枚棋子的哪里：x 右、y 上、z 前（负数 = 玩家这边）")]
        public Vector3 shot4Offset = new Vector3(2.2f, 1.4f, -3.2f);
        [Tooltip("相机跟上去的平滑时间")]
        public float shot4Smooth = 0.35f;
        [Tooltip("逃进城堡后停多久")]
        public float shot4Hold = 0.8f;

        /// <summary>过场需要的外部东西，由 ChessGameController 传进来。</summary>
        public struct Context
        {
            public BoardView Board;
            public Camera Camera;
            public DeskCameraFit CameraFit;
            /// <summary>过场演完后相机要回到的第三阶段视角。</summary>
            public float GamePitch, GameFieldOfView, GameZoom;
            /// <summary>灯光过渡：0 = 第一、二阶段的平行光，1 = 第三阶段的聚光灯。</summary>
            public Action<float> BlendLighting;
            /// <summary>镜头演完（或跳过）后把棋盘摆成第三阶段的最终样子。</summary>
            public Action FinishBoard;
            /// <summary>这下面的屏幕 UI（状态栏、按钮）在过场里藏起来。</summary>
            public Transform HudRoot;
        }

        Context ctx;
        bool running;
        bool skipped;
        float shakeAmount, shakeUntil, shakeDuration;
        readonly List<GameObject> temporary = new List<GameObject>();
        readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        AudioSource musicSource;

        public bool IsPlaying => running;

        /// <summary>过场：演四个镜头（可以跳过），摆好棋盘，再把镜头平滑转回游戏视角。</summary>
        public IEnumerator Play(Context context)
        {
            ctx = context;
            running = true;
            skipped = false;
            shakeUntil = 0f;
            PlayMusic();
            HideHud(true);
            if (ctx.CameraFit != null) ctx.CameraFit.enabled = false;
            float originalFov = ctx.Camera.fieldOfView;
            ctx.Camera.fieldOfView = fieldOfView;
            FaceCamera.FollowCameraPitch = true;
            ctx.Board.BeginStage3Reveal();

            StartCoroutine(Shots());
            while (running && !skipped)
            {
                if (allowSkip && SkipPressed()) skipped = true;
                yield return null;
            }
            StopAllCoroutines();
            Cleanup();

            ctx.BlendLighting?.Invoke(1f);
            ctx.FinishBoard?.Invoke();
            ResetPieceTransforms();
            yield return BlendBack(originalFov);
            HideHud(false);
            running = false;
        }

        /// <summary>开新局时过场还没演完：全部停掉，相机和 UI 还原（棋盘由新局重建）。</summary>
        public void Abort()
        {
            StopAllCoroutines();
            Cleanup();
            HideHud(false);
            if (ctx.CameraFit != null) ctx.CameraFit.enabled = true;
            running = false;
            if (musicSource != null) musicSource.Stop();
        }

        IEnumerator Shots()
        {
            var board = ctx.Board.Board;
            var friend = board.Rescuer.Opponent();
            var king = board.King(friend);
            var queen = FindRoyalQueen(board, friend, king);
            var royals = new List<Piece>();
            if (king != null) royals.Add(king);
            if (queen != null) royals.Add(queen);
            var royalIds = new HashSet<int>();
            foreach (var r in royals) royalIds.Add(r.Id);

            // 王、王后过场里待的格子（风暴前站的地方，被冲击波挤开的话换到最近的空格）
            var temp = new Dictionary<int, Coord>();

            yield return ShotKing(king);
            yield return ShotMeteor(board, royals, royalIds, temp);
            yield return ShotMountains(board, royals, royalIds, temp);
            yield return ShotEscape(board, royals, temp);
            running = false;
        }

        // ---------- 镜头 1 ----------

        IEnumerator ShotKing(Piece king)
        {
            Vector3 kingPos = Vector3.zero;
            if (king != null && ctx.Board.TryGetPieceView(king.Id, out var kingView)) kingPos = kingView.localPosition;
            var look = kingPos + Vector3.up * shot1LookHeight;
            var camPos = kingPos + new Vector3(shot1Side, shot1Height, -shot1Distance);
            var startRotation = LookRotation(look - camPos);

            for (float t = 0; t < shot1Lead; t += Time.deltaTime)
            {
                SetCamera(camPos, startRotation);
                yield return null;
            }

            if (kingEffect != null)
            {
                var fx = ParticleFx.Spawn(kingEffect, ctx.Board.Root, kingPos + Vector3.up * kingEffectLift, kingEffectScale);
                fx.name = "Cutscene King Effect";
                ParticleFx.PlayOnce(fx);
                temporary.Add(fx);
            }
            for (float t = 0; t < kingEffectHold; t += Time.deltaTime)
            {
                SetCamera(camPos, startRotation);
                yield return null;
            }

            // 往上摇：水平方向不变，俯仰角从看着王转到仰头看天；同时乌云聚起来、天变暗
            ctx.Board.FormStormCloud();
            ctx.Board.SetStormCloudOpacity(0f);
            var forward = startRotation * Vector3.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float startPitch = Mathf.Asin(Mathf.Clamp(-forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float total = Mathf.Max(tiltDuration, cloudFormDuration) + skyHold;
            for (float t = 0; t < total; t += Time.deltaTime)
            {
                float tilt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, tiltDuration)));
                float form = Mathf.Clamp01(t / Mathf.Max(0.01f, cloudFormDuration));
                var rotation = Quaternion.Euler(Mathf.Lerp(startPitch, -tiltUpAngle, tilt), yaw, 0f);
                SetCamera(camPos + Vector3.up * tiltRise * tilt, rotation);
                ctx.Board.SetStormCloudOpacity(cloudOpacity * form);
                ctx.BlendLighting?.Invoke(form);
                yield return null;
            }
        }

        // ---------- 镜头 2 ----------

        IEnumerator ShotMeteor(Board board, List<Piece> royals, HashSet<int> royalIds, Dictionary<int, Coord> temp)
        {
            var lava = new List<Coord>();
            for (int i = 0; i < 64; i++)
                if (board.IsLava(Coord.FromIndex(i))) lava.Add(Coord.FromIndex(i));
            // 砸在离棋盘中心最近的岩浆格（画面里最好看），没有岩浆就砸在中间
            var impact = new Coord(3, 4);
            float best = float.MaxValue;
            foreach (var c in lava)
            {
                float d = (BoardView.SquareCenter(c) - Vector3.zero).sqrMagnitude;
                if (d < best) { best = d; impact = c; }
            }
            var impactPos = ctx.Board.PiecePosition(impact);
            var start = impactPos + meteorStartOffset;

            var meteor = BuildMeteor(start, impactPos - start);
            Vector3 camPos = start, look = impactPos;
            bool cameraStopped = false;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, fallDuration))
            {
                float k = Mathf.Pow(t, fallAcceleration);
                var p = Vector3.Lerp(start, impactPos, k);
                meteor.transform.position = ctx.Board.Root.TransformPoint(p);
                if (!cameraStopped && p.y < cameraStopHeight) cameraStopped = true;
                if (!cameraStopped)
                {
                    var dir = (impactPos - start).normalized;
                    camPos = p - dir * followDistance + Vector3.up * followHeight;
                    look = Vector3.Lerp(p, impactPos, 0.15f);
                }
                else look = Vector3.Lerp(look, impactPos, 1f - Mathf.Exp(-6f * Time.deltaTime));
                SetCamera(camPos, LookRotation(look - camPos));
                yield return null;
            }
            Destroy(meteor);
            temporary.Remove(meteor);

            // 砸中：爆炸、震动、岩浆坑、棋子被吹到第三阶段的位置
            if (impactEffect != null)
            {
                var fx = ParticleFx.Spawn(impactEffect, ctx.Board.Root, impactPos + Vector3.up * 0.2f, impactScale);
                fx.name = "Cutscene Meteor Impact";
                if (!impactShockwave)
                {
                    var shockwave = fx.transform.Find("Shockwave");
                    if (shockwave != null) shockwave.gameObject.SetActive(false);
                }
                ParticleFx.PlayOnce(fx);
                temporary.Add(fx);
            }
            Shake(impactShake, impactShakeDuration);
            ctx.Board.RevealLava(impact);
            ctx.Board.Highlight(null, null, null, null);

            PlaceRoyals(board, royals, temp);
            foreach (var piece in board.AllPieces)
            {
                if (!piece.OnBoard || !ctx.Board.TryGetPieceView(piece.Id, out var view)) continue;
                if (!view.gameObject.activeSelf)
                {
                    // 风暴里新冒出来的棋子（补回来的王后）：直接出现在它的位置
                    view.gameObject.SetActive(true);
                    view.localPosition = ctx.Board.PiecePosition(royalIds.Contains(piece.Id) ? temp[piece.Id] : piece.Pos);
                    continue;
                }
                var to = royalIds.Contains(piece.Id) ? temp[piece.Id] : piece.Pos;
                StartCoroutine(ctx.Board.AnimateKnockback(piece.Id, to, scatterDuration));
            }

            // 小火球溅到其他岩浆格
            float delay = 0f;
            foreach (var c in lava)
            {
                if (c == impact) continue;
                StartCoroutine(Splash(impactPos, c, delay));
                delay += 0.08f;
            }

            float hold = shot2Hold + delay;
            for (float t = 0; t < hold; t += Time.deltaTime)
            {
                ctx.Board.SetStormCloudOpacity(Mathf.Lerp(cloudOpacity, cloudOpacityAfterImpact, t / Mathf.Max(0.01f, hold)));
                SetCamera(camPos, LookRotation(look - camPos));
                yield return null;
            }
        }

        GameObject BuildMeteor(Vector3 localStart, Vector3 localDirection)
        {
            var meteor = new GameObject("Cutscene Meteor");
            meteor.transform.SetParent(ctx.Board.Root, false);
            meteor.transform.localPosition = localStart;
            temporary.Add(meteor);

            GameObject body;
            if (meteorModel != null) body = Instantiate(meteorModel, meteor.transform);
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(meteor.transform, false);
                var renderer = body.GetComponent<Renderer>();
                var material = renderer.material;
                var rock = new Color(0.22f, 0.16f, 0.13f);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", rock);
                material.color = rock;
                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", new Color(1.2f, 0.35f, 0.05f));
                }
            }
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = Vector3.one * meteorSize;

            // 火和烟挂在一个朝着飞行反方向的支点下：粒子包里的特效默认往上喷，转过来就往后喷
            var tail = new GameObject("Tail");
            tail.transform.SetParent(meteor.transform, false);
            tail.transform.localRotation = Quaternion.FromToRotation(Vector3.up, -localDirection.normalized);
            if (meteorFire != null)
            {
                var fx = ParticleFx.Spawn(meteorFire, tail.transform, Vector3.zero, meteorFireScale);
                ParticleFx.Warm(fx, 0.5f);
            }
            if (meteorTrail != null)
            {
                var fx = ParticleFx.Spawn(meteorTrail, tail.transform, Vector3.zero, meteorTrailScale);
                fx.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                ParticleFx.Warm(fx, 0.1f);
            }
            return meteor;
        }

        IEnumerator Splash(Vector3 from, Coord to, float delay)
        {
            for (float t = 0; t < delay; t += Time.deltaTime) yield return null;
            var end = ctx.Board.PiecePosition(to);
            GameObject fx = null;
            if (splashEffect != null)
            {
                fx = ParticleFx.Spawn(splashEffect, ctx.Board.Root, from, splashScale);
                fx.name = "Cutscene Lava Splash";
                ParticleFx.Warm(fx, 0.3f);
                temporary.Add(fx);
                for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, splashDuration))
                {
                    if (fx == null) break;
                    fx.transform.localPosition = Vector3.Lerp(from, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * splashHeight + 0.2f);
                    yield return null;
                }
                if (fx != null)
                {
                    temporary.Remove(fx);
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    Destroy(fx, 1.5f);
                }
            }
            ctx.Board.RevealLava(to);
            ctx.Board.Highlight(null, null, null, null);
        }

        /// <summary>
        /// 王和王后在过场里先待在风暴前站的格子（逻辑上它们已经在城堡里了）。
        /// 那一格要是被吹过来的棋子、岩浆或城堡占了，就挪到最近的空格。
        /// </summary>
        void PlaceRoyals(Board board, List<Piece> royals, Dictionary<int, Coord> temp)
        {
            var taken = new bool[64];
            foreach (var p in board.AllPieces)
                if (p.OnBoard && !royals.Contains(p)) taken[p.Pos.Index] = true;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (board.IsLava(c) || board.IsCastle(c)) taken[i] = true;
            }
            Coord? previous = null;
            foreach (var royal in royals)
            {
                Coord origin;
                if (ctx.Board.TryGetPieceView(royal.Id, out var view) && view.gameObject.activeSelf) origin = ToCoord(view.localPosition);
                else origin = previous ?? new Coord(3, 6);
                var spot = NearestFree(origin, taken);
                taken[spot.Index] = true;
                temp[royal.Id] = spot;
                previous = spot;
            }
        }

        static Coord NearestFree(Coord origin, bool[] taken)
        {
            Coord best = origin;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                if (taken[i]) continue;
                var c = Coord.FromIndex(i);
                float d = (c.X - origin.X) * (c.X - origin.X) + (c.Y - origin.Y) * (c.Y - origin.Y);
                if (d < bestDistance) { bestDistance = d; best = c; }
            }
            return best;
        }

        static Coord ToCoord(Vector3 local) =>
            new Coord(Mathf.Clamp(Mathf.RoundToInt(local.x + 3.5f), 0, 7), Mathf.Clamp(Mathf.RoundToInt(local.z + 3.5f), 0, 7));

        // ---------- 镜头 3 ----------

        IEnumerator ShotMountains(Board board, List<Piece> royals, HashSet<int> royalIds, Dictionary<int, Coord> temp)
        {
            int maxLevel = ctx.Board.MaxTerrainLevel;
            var raised = new List<Coord>();
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (board.Height(c) <= 0) continue;
                raised.Add(c);
                sum += BoardView.SquareCenter(c);
            }
            if (raised.Count == 0 || maxLevel <= 0) yield break;
            var look = sum / raised.Count + Vector3.up * (maxLevel * BoardView.LevelHeight * 0.5f);
            var direction = shot3Direction.sqrMagnitude > 0.0001f ? shot3Direction.normalized : new Vector3(1f, 0.6f, -1.2f).normalized;
            float totalTime = maxLevel * (risePerLevel + levelPause) + shot3Hold;
            float elapsed = 0f;

            void Frame()
            {
                float u = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, totalTime));
                var camPos = look + direction * Mathf.Lerp(shot3Distance.x, shot3Distance.y, Mathf.SmoothStep(0f, 1f, u));
                SetCamera(camPos, LookRotation(look - camPos));
                foreach (var royal in royals)
                    if (ctx.Board.TryGetPieceView(royal.Id, out var view)) view.localPosition = ctx.Board.PiecePosition(temp[royal.Id]);
            }

            for (int level = 0; level < maxLevel; level++)
            {
                // 这一级在升的格子上冒尘土
                if (riseDust != null)
                {
                    var rising = raised.FindAll(c => board.Height(c) > level);
                    for (int n = 0; n < Mathf.Min(riseDustPerLevel, rising.Count); n++)
                    {
                        int k = UnityEngine.Random.Range(n, rising.Count);
                        (rising[n], rising[k]) = (rising[k], rising[n]);
                        var fx = ParticleFx.Spawn(riseDust, ctx.Board.Root, ctx.Board.PiecePosition(rising[n]), riseDustScale, UnityEngine.Random.Range(0f, 360f));
                        fx.name = "Cutscene Rise Dust";
                        var shockwave = fx.transform.Find("Shockwave");
                        if (shockwave != null) shockwave.gameObject.SetActive(false);
                        ParticleFx.PlayOnce(fx);
                        temporary.Add(fx);
                    }
                }
                Shake(riseShake, risePerLevel);
                for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, risePerLevel))
                {
                    // 先快后慢，像被顶上来再停住
                    float k = 1f - (1f - t) * (1f - t);
                    ctx.Board.SetTerrainRise(level + k, royalIds);
                    elapsed += Time.deltaTime;
                    Frame();
                    yield return null;
                }
                ctx.Board.SetTerrainRise(level + 1, royalIds);
                for (float t = 0; t < levelPause; t += Time.deltaTime)
                {
                    elapsed += Time.deltaTime;
                    Frame();
                    yield return null;
                }
            }
            for (float t = 0; t < shot3Hold; t += Time.deltaTime)
            {
                elapsed += Time.deltaTime;
                Frame();
                yield return null;
            }
        }

        // ---------- 镜头 4 ----------

        IEnumerator ShotEscape(Board board, List<Piece> royals, Dictionary<int, Coord> temp)
        {
            if (royals.Count == 0) yield break;
            var views = new List<Transform>();
            int stillRunning = 0;
            for (int i = 0; i < royals.Count; i++)
            {
                if (!ctx.Board.TryGetPieceView(royals[i].Id, out var view)) continue;
                views.Add(view);
                var path = FindPath(board, temp[royals[i].Id], royals[i].Pos);
                stillRunning++;
                StartCoroutine(Run(view, path, i * queenDelay, () => stillRunning--));
            }
            if (views.Count == 0) yield break;

            Vector3 Middle()
            {
                var m = Vector3.zero;
                foreach (var v in views) m += v.localPosition;
                return m / views.Count;
            }
            var look = Middle() + Vector3.up * 0.5f;
            var camPos = look + shot4Offset;
            var velocity = Vector3.zero;
            var lookVelocity = Vector3.zero;
            float hold = 0f;
            while (hold < shot4Hold)
            {
                var targetLook = Middle() + Vector3.up * 0.5f;
                look = Vector3.SmoothDamp(look, targetLook, ref lookVelocity, shot4Smooth);
                camPos = Vector3.SmoothDamp(camPos, targetLook + shot4Offset, ref velocity, shot4Smooth);
                SetCamera(camPos, LookRotation(look - camPos));
                if (stillRunning <= 0) hold += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator Run(Transform view, List<Coord> path, float delay, Action done)
        {
            for (float t = 0; t < delay; t += Time.deltaTime) yield return null;
            for (int i = 1; i < path.Count; i++)
            {
                var a = ctx.Board.PiecePosition(path[i - 1]);
                var b = ctx.Board.PiecePosition(path[i]);
                float peak = Mathf.Max(a.y, b.y) + hopHeight;
                for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, hopDuration))
                {
                    var p = Vector3.Lerp(a, b, t);
                    // 抛物线经过 a、peak、b：先爬到 peak 再落到 b
                    float up = t < 0.5f ? Mathf.Lerp(a.y, peak, 1f - (1f - 2f * t) * (1f - 2f * t)) : Mathf.Lerp(b.y, peak, 1f - (2f * t - 1f) * (2f * t - 1f));
                    p.y = up;
                    view.localPosition = p;
                    yield return null;
                }
                view.localPosition = b;
            }
            done();
        }

        /// <summary>从 from 跳到 to 的路线：八个方向一格一格跳，绕开岩浆和别的城堡格，尽量少爬坡（高度差越大越不想走）。</summary>
        static List<Coord> FindPath(Board board, Coord from, Coord to)
        {
            var cost = new float[64];
            var prev = new int[64];
            var done = new bool[64];
            for (int i = 0; i < 64; i++) { cost[i] = float.MaxValue; prev[i] = -1; }
            cost[from.Index] = 0f;
            for (int step = 0; step < 64; step++)
            {
                int current = -1;
                for (int i = 0; i < 64; i++)
                    if (!done[i] && cost[i] < float.MaxValue && (current < 0 || cost[i] < cost[current])) current = i;
                if (current < 0 || current == to.Index) break;
                done[current] = true;
                var c = Coord.FromIndex(current);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Coord(c.X + dx, c.Y + dy);
                    if (!n.IsValid || done[n.Index]) continue;
                    if (n != to && (board.IsLava(n) || board.IsCastle(n))) continue;
                    float next = cost[current] + 1f + 0.6f * Mathf.Abs(board.Height(n) - board.Height(c));
                    if (next < cost[n.Index]) { cost[n.Index] = next; prev[n.Index] = current; }
                }
            }
            var path = new List<Coord>();
            if (prev[to.Index] < 0 && from != to)
            {
                // 走不通（被岩浆围住）：直接跳过去
                path.Add(from);
                path.Add(to);
                return path;
            }
            for (int i = to.Index; i >= 0; i = prev[i])
            {
                path.Add(Coord.FromIndex(i));
                if (i == from.Index) break;
            }
            path.Reverse();
            return path;
        }

        static Piece FindRoyalQueen(Board board, Side friend, Piece king)
        {
            // 风暴把王后放进了城堡：城堡里除了王以外的朋友的后
            foreach (var p in board.AllPieces)
                if (p.OnBoard && p != king && p.Side == friend && p.Type == PieceType.Queen && board.IsCastle(p.Pos)) return p;
            return null;
        }

        // ---------- 镜头回到游戏视角 ----------

        IEnumerator BlendBack(float originalFov)
        {
            var cam = ctx.Camera.transform;
            var fit = ctx.CameraFit;
            if (fit == null)
            {
                ctx.Camera.fieldOfView = originalFov;
                yield break;
            }
            var fromPosition = cam.localPosition;
            var fromRotation = cam.localRotation;
            float fromFov = ctx.Camera.fieldOfView;

            fit.pitch = ctx.GamePitch;
            fit.fieldOfView = ctx.GameFieldOfView;
            fit.zoom = ctx.GameZoom;
            fit.extraPoints = ctx.Board.TallPoints();
            fit.SetOrbit(0f, 0f, 1f);
            fit.Fit();
            var toPosition = cam.localPosition;
            var toRotation = cam.localRotation;
            float toFov = ctx.Camera.fieldOfView;

            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, blendBackDuration))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                cam.localPosition = Vector3.Lerp(fromPosition, toPosition, k);
                cam.localRotation = Quaternion.Slerp(fromRotation, toRotation, k);
                ctx.Camera.fieldOfView = Mathf.Lerp(fromFov, toFov, k);
                yield return null;
            }
            fit.enabled = true;
            fit.Fit();
        }

        // ---------- 工具 ----------

        void SetCamera(Vector3 localPosition, Quaternion localRotation)
        {
            var root = ctx.Board.Root;
            var position = localPosition;
            if (Time.time < shakeUntil && shakeDuration > 0f)
            {
                float fade = (shakeUntil - Time.time) / shakeDuration;
                float s = shakeAmount * fade;
                float time = Time.time * 25f;
                position += new Vector3(Mathf.PerlinNoise(time, 0f) - 0.5f, Mathf.PerlinNoise(0f, time) - 0.5f, Mathf.PerlinNoise(time, time) - 0.5f) * 2f * s;
            }
            ctx.Camera.transform.SetPositionAndRotation(root.TransformPoint(position), root.rotation * localRotation);
        }

        static Quaternion LookRotation(Vector3 localDirection) =>
            localDirection.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(localDirection, Vector3.up);

        void Shake(float amount, float duration)
        {
            shakeAmount = amount;
            shakeDuration = Mathf.Max(0.01f, duration);
            shakeUntil = Time.time + shakeDuration;
        }

        static bool SkipPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame);
        }

        void PlayMusic()
        {
            if (music == null) return;
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.playOnAwake = false;
                musicSource.spatialBlend = 0f;
            }
            musicSource.clip = music;
            musicSource.loop = loopMusic;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        /// <summary>开新局时停掉第三阶段的音乐。</summary>
        public void StopMusic()
        {
            if (musicSource != null) musicSource.Stop();
        }

        void HideHud(bool hide)
        {
            if (hide)
            {
                hiddenCanvases.Clear();
                if (ctx.HudRoot == null) return;
                foreach (var canvas in ctx.HudRoot.GetComponentsInChildren<Canvas>())
                    if (canvas.isRootCanvas && canvas.enabled && canvas.renderMode != RenderMode.WorldSpace)
                    {
                        canvas.enabled = false;
                        hiddenCanvases.Add(canvas);
                    }
            }
            else
            {
                foreach (var canvas in hiddenCanvases)
                    if (canvas != null) canvas.enabled = true;
                hiddenCanvases.Clear();
            }
        }

        void Cleanup()
        {
            foreach (var go in temporary)
                if (go != null) Destroy(go);
            temporary.Clear();
            shakeUntil = 0f;
        }

        /// <summary>跳过时有的棋子还在被吹飞的半路上（歪着、缩着），摆正。</summary>
        void ResetPieceTransforms()
        {
            foreach (var piece in ctx.Board.Board.AllPieces)
                if (ctx.Board.TryGetPieceView(piece.Id, out var view))
                {
                    view.localRotation = Quaternion.identity;
                    view.localScale = Vector3.one;
                }
        }

        void OnGUI()
        {
            if (!running || !allowSkip || skipped) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = Mathf.Max(14, Screen.height / 40) };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(0, 0, Screen.width - 20, Screen.height - 16), "Space / Esc: skip", style);
        }
    }
}
