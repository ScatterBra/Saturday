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
    ///   镜头 0：像一枚棋子的视角，手持晃动着从 b1 滑到 f1，沙尘暴卷进来。
    ///   镜头 1：贴着棋盘对准朋友的王，王身上放特效，镜头往上摇看天空，乌云聚起来、天变暗。
    ///   镜头 2：战地记者视角，站在 a1 外面手持拍摄：第一颗陨石突然砸在眼前，镜头剧烈晃动；再一颗一颗追着剩下的陨石砸下来。
    ///          每颗砸出一个岩浆坑。然后硬切到云下绕圈的直升机拍剩下的；棋子不动，趁切镜头直接换到第三阶段的位置。
    ///   镜头 3：棋子视角站在 e2（手持），山一层一层升起来、棋子跟着升；升完后放大变焦看向城堡。
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

        [Header("Handheld (shared by every handheld shot)")]
        [Tooltip("手持晃动：位置晃多少（世界单位）、角度晃多少（度）、晃得多快。每个镜头再乘自己的倍数")]
        public float handheldPosition = 0.025f;
        public float handheldAngle = 1.2f;
        public float handheldSpeed = 1.3f;

        [Header("Music")]
        [Tooltip("过场开始时播放（stage3.wav）。跳过过场不会停")]
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = 1f;
        public bool loopMusic;

        [Header("Shot 0: piece's-eye view sliding along rank 1, dust storm rolls in")]
        [Tooltip("关掉就跳过这个镜头，从镜头 1 开始")]
        public bool playIntroShot = true;
        [Tooltip("镜头总长（秒）")]
        public float introDuration = 6f;
        [Tooltip("从哪一格滑到哪一格（a1 = (0, 0)，b1 = (1, 0)，f1 = (5, 0)）")]
        public Vector2Int introFrom = new Vector2Int(1, 0);
        public Vector2Int introTo = new Vector2Int(5, 0);
        [Tooltip("相机离棋盘多高（棋子眼睛的高度）")]
        public float introHeight = 0.45f;
        [Tooltip("相机从格子中心往玩家那边退多少（0 = 正好在格子中间；0.5 = 棋盘边上；再大就退到棋盘外，不会被第一排的棋子挡住）")]
        public float introBack = 0.9f;
        [Tooltip("相机往下看多少度（0 = 平视棋盘对面）")]
        [Range(-30f, 45f)] public float introPitch = 6f;
        [Tooltip("手持晃动的倍数（1 = Handheld 里的值）")]
        public float introHandheld = 1f;
        [Tooltip("沙尘暴（ParticlePack 的 DustStorm）")]
        public GameObject dustStorm;
        public float dustStormScale = 0.6f;
        [Tooltip("沙尘暴放在棋盘哪里（x 左右、y 高度、z 前后，0 = 棋盘中心）")]
        public Vector3 dustStormPosition = new Vector3(0f, 0.3f, 0.5f);
        [Tooltip("镜头开始后第几秒出现沙尘暴")]
        public float dustStormAt = 2f;
        [Tooltip("沙尘暴里夹着飞的石头（特效里的 RockDebris）。不勾就只有风沙")]
        public bool dustStormRocks;

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
        [Tooltip("手持晃动的倍数（1 = Handheld 里的值，0 = 不晃）")]
        public float shot1Handheld = 0.8f;

        [Header("Shot 2: meteor shower, war-reporter view from a1")]
        [Tooltip("陨石的拖尾（ParticlePack 的 RocketTrail：只用里面的火、烟、火星三条拖尾，没有实体）")]
        public GameObject meteorEffect;
        public float meteorEffectScale = 0.15f;
        [Tooltip("几颗陨石。先砸岩浆格（每颗砸出一个岩浆坑），岩浆格不够就砸空地（只爆炸）；陨石不够时剩下的岩浆格由小火球溅过去")]
        public int meteorCount = 5;
        [Tooltip("每颗陨石在这个镜头开始后第几秒落地（不够就按最后一个间隔往后排）。第一颗要快：镜头一开始没看见它，它就突然砸下来")]
        public float[] meteorLandTimes = { 1f, 3.6f, 5.8f, 7.4f, 9f };
        [Tooltip("每颗陨石从天上落到地面用多久")]
        public float meteorFallDuration = 1.8f;
        [Tooltip("越落越快：1 = 匀速，越大最后越快")]
        [Range(1f, 3f)] public float fallAcceleration = 1.6f;
        [Tooltip("陨石从多高的地方出发（随机在两个值之间）")]
        public Vector2 meteorStartHeight = new Vector2(10f, 14f);
        [Tooltip("陨石出发点在落点的哪个方向（会随机偏一点）：x 左右、z 前后（正数 = 棋盘对面，从远处斜着砸过来，镜头看得见）")]
        public Vector2 meteorStartDirection = new Vector2(0.3f, 1f);
        [Tooltip("陨石出发点离落点水平多远（随机在两个值之间）")]
        public Vector2 meteorStartDistance = new Vector2(4f, 8f);
        [Tooltip("相机站在哪一格（a1 = (0, 0)）")]
        public Vector2Int shot2Square = new Vector2Int(0, 0);
        [Tooltip("相机相对这一格中心的位置：x 左右、y 高度（棋子眼睛）、z 前后（负数 = 往棋盘外退，不会被棋子挡住）")]
        public Vector3 shot2Offset = new Vector3(-0.6f, 0.45f, -0.6f);
        [Tooltip("镜头跟着陨石转的平滑时间：越大越跟不上（像人手转过去追），越小越跟手")]
        public float trackSmooth = 0.22f;
        [Tooltip("陨石落地后镜头在落点上停多久，再去追下一颗")]
        public float trackLinger = 0.35f;
        [Tooltip("手持晃动的倍数（战地记者：比别的镜头晃得厉害）")]
        public float shot2Handheld = 1.6f;
        [Tooltip("战地记者机位拍前几颗陨石，之后切到直升机机位")]
        public int reporterMeteors = 2;
        [Tooltip("第一颗砸下来后相机没拿稳：猛地往地面压下去再抬起来。整个过程多久")]
        public float slamDuration = 1.1f;
        [Tooltip("压下去用了整个过程的多少（越小压得越猛，剩下的时间慢慢抬起来）")]
        [Range(0.05f, 0.9f)] public float slamAttack = 0.18f;
        [Tooltip("最多往下压多少度、歪多少度、往下沉多少")]
        public float slamPitch = 18f;
        public float slamRoll = 5f;
        public float slamDrop = 0.08f;
        [Tooltip("抬起来以后追第二颗时放大变焦：视野角变到多少（越小放得越大）、用多久")]
        [Range(5f, 90f)] public float reporterZoomFov = 22f;
        public float reporterZoomDuration = 1.4f;
        [Tooltip("砸到棋盘时的爆炸（BigExplosion）")]
        public GameObject impactEffect;
        public float impactScale = 0.45f;
        [Tooltip("保留爆炸特效里的冲击波扭曲圈（很大，会盖住一片棋盘）")]
        public bool impactShockwave;
        [Tooltip("第一颗砸下来时镜头剧烈震多厉害、震多久")]
        public float firstImpactShake = 0.35f;
        public float firstImpactShakeDuration = 1.2f;
        [Tooltip("后面每颗砸下来时镜头震多厉害、震多久")]
        public float impactShake = 0.18f;
        public float impactShakeDuration = 0.7f;

        [Header("Shot 2b: helicopter circling below the clouds")]
        [Tooltip("绕棋盘中心转的半径、离棋盘多高（要比云低，云大约在 5 左右）")]
        public float heliRadius = 6.5f;
        public float heliHeight = 3.2f;
        [Tooltip("从哪个角度开始绕（0 = 玩家正后方，正数往右）、每秒转多少度（负数反着绕）")]
        public float heliStartAngle = -40f;
        public float heliSpeed = 18f;
        [Tooltip("直升机机位的视野角")]
        [Range(10f, 90f)] public float heliFieldOfView = 45f;
        [Tooltip("镜头看陨石还是看棋盘中心：1 = 盯着陨石，0 = 只看棋盘中心")]
        [Range(0f, 1f)] public float heliTrackWeight = 0.5f;
        [Tooltip("镜头最多抬到看这个高度（陨石还在高处时不跟上去，棋盘一直在画面里，陨石从画面上方砸进来）")]
        public float heliLookMaxHeight = 1.5f;
        [Tooltip("镜头跟着转的平滑时间（直升机上的镜头转得比手持慢）")]
        public float heliTrackSmooth = 0.5f;
        [Tooltip("机身往转弯里面倾斜多少度")]
        public float heliBank = 6f;
        [Tooltip("手持晃动的倍数（直升机：轻一点）")]
        public float heliHandheld = 0.5f;
        [Tooltip("溅到其他岩浆格的小火球（FireBall），留空就直接冒出岩浆")]
        public GameObject splashEffect;
        public float splashScale = 0.12f;
        [Tooltip("小火球飞多久、飞多高")]
        public float splashDuration = 0.8f;
        public float splashHeight = 2f;
        [Tooltip("最后一颗砸完后停多久（镜头慢慢转回棋盘中间，看着一片火海）")]
        public float shot2Hold = 2f;
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
        [Tooltip("每升一级镜头震多厉害")]
        public float riseShake = 0.1f;
        [Tooltip("相机站在哪一格（e2 = (4, 1)），棋子视角")]
        public Vector2Int shot3Square = new Vector2Int(4, 1);
        [Tooltip("相机相对这一格中心的位置：x 左右、y 高度（棋子眼睛）、z 前后")]
        public Vector3 shot3Offset = new Vector3(0f, 0.45f, 0f);
        [Tooltip("把站在相机这一格上的棋子先藏起来（不然相机在它身体里面），镜头结束再出来")]
        public bool hidePieceAtCamera = true;
        [Tooltip("手持晃动的倍数")]
        public float shot3Handheld = 1f;
        [Tooltip("山升完后放大变焦看向城堡：视野角变到多少（越小放得越大）、用多久")]
        [Range(5f, 90f)] public float shot3ZoomFov = 18f;
        public float shot3ZoomDuration = 1.5f;
        [Tooltip("放大到城堡后停多久")]
        public float shot3Hold = 0.8f;

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
        float handheldSeed;
        /// <summary>棋子贴图、文字朝着它摆（DeskCameraFit.ViewReference）。过场里跟着过场相机转，演完还原。</summary>
        Transform viewReference;
        Quaternion viewReferenceRotation;
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
            handheldSeed = UnityEngine.Random.Range(0f, 100f);
            viewReference = ctx.CameraFit != null ? ctx.CameraFit.ViewReference : null;
            if (viewReference != null) viewReferenceRotation = viewReference.rotation;
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

            if (playIntroShot) yield return ShotIntro();
            yield return ShotKing(king);
            yield return ShotMeteor(board, royals, royalIds, temp);
            yield return ShotMountains(board, royals, royalIds, temp);
            yield return ShotEscape(board, royals, temp);
            running = false;
        }

        // ---------- 镜头 0 ----------

        /// <summary>像一枚棋子的视角，贴着第一排从 introFrom 滑到 introTo，带手持晃动；第 dustStormAt 秒沙尘暴卷进来。</summary>
        IEnumerator ShotIntro()
        {
            var from = BoardView.SquareCenter(new Coord(introFrom.x, introFrom.y)) + new Vector3(0f, introHeight, -introBack);
            var to = BoardView.SquareCenter(new Coord(introTo.x, introTo.y)) + new Vector3(0f, introHeight, -introBack);
            var baseRotation = Quaternion.Euler(introPitch, 0f, 0f);
            GameObject storm = null;
            float duration = Mathf.Max(0.1f, introDuration);
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                if (storm == null && dustStorm != null && t >= dustStormAt) storm = SpawnDustStorm();
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                SetHandheldCamera(Vector3.Lerp(from, to, k), baseRotation, introHandheld);
                yield return null;
            }
            // 沙尘暴不再卷新的沙子，已经卷起来的慢慢散掉（镜头 1 开始时还看得到一点）
            if (storm != null)
                foreach (var ps in storm.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        GameObject SpawnDustStorm()
        {
            var fx = ParticleFx.Spawn(dustStorm, ctx.Board.Root, dustStormPosition, dustStormScale);
            fx.name = "Cutscene Dust Storm";
            if (!dustStormRocks)
            {
                var rocks = fx.transform.Find("RockDebris");
                if (rocks != null) rocks.gameObject.SetActive(false);
            }
            // 原特效是预热好的（一出现就满屏），关掉预热，沙尘从无到有卷起来
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.prewarm = false;
                ps.Clear(false);
                ps.Play(false);
            }
            temporary.Add(fx);
            return fx;
        }

        /// <summary>
        /// 手持镜头：在给定的位置和朝向上叠一层晃动（几层不同频率的柏林噪声，位置和角度一起轻轻晃）。
        /// amount 是这个镜头的倍数。每次过场的噪声起点不一样，晃法不重复。
        /// </summary>
        void SetHandheldCamera(Vector3 localPosition, Quaternion localRotation, float amount)
        {
            float s = Time.time * handheldSpeed;
            float p = handheldPosition * amount, a = handheldAngle * amount;
            var wobble = new Vector3(Noise(handheldSeed, s), Noise(handheldSeed + 10f, s), Noise(handheldSeed + 20f, s)) * p;
            var tilt = Quaternion.Euler(Noise(handheldSeed + 30f, s) * a, Noise(handheldSeed + 40f, s) * a, Noise(handheldSeed + 50f, s) * a * 0.5f);
            SetCamera(localPosition + wobble, localRotation * tilt);
        }

        /// <summary>-1 到 1 之间平滑变化的噪声：两层频率叠加，比单层更像手抖。</summary>
        static float Noise(float seed, float time) =>
            (Mathf.PerlinNoise(seed, time) - 0.5f) * 1.4f + (Mathf.PerlinNoise(seed + 5f, time * 2.7f) - 0.5f) * 0.6f;

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
                SetHandheldCamera(camPos, startRotation, shot1Handheld);
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
                SetHandheldCamera(camPos, startRotation, shot1Handheld);
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
                SetHandheldCamera(camPos + Vector3.up * tiltRise * tilt, rotation, shot1Handheld);
                ctx.Board.SetStormCloudOpacity(cloudOpacity * form);
                ctx.BlendLighting?.Invoke(form);
                yield return null;
            }
        }

        // ---------- 镜头 2 ----------

        /// <summary>一颗陨石：从 Start 斜着砸到 End，第 Land 秒落地。</summary>
        sealed class Meteor
        {
            public Coord Target;
            public Vector3 Start, End;
            public float Land;
            public GameObject Trail;
            public bool Landed;
            public Vector3 Position(float time, float fall, float acceleration)
            {
                float u = Mathf.Clamp01((time - (Land - fall)) / Mathf.Max(0.05f, fall));
                return Vector3.Lerp(Start, End, Mathf.Pow(u, acceleration));
            }
        }

        /// <summary>
        /// 两个机位：
        /// A. 战地记者：站在 a1 外面（棋子的高度），手持。第一颗陨石突然砸在眼前，镜头剧烈晃动、猛地往地面压再抬起来，
        ///    然后抬头追第二颗，边追边放大变焦。
        /// B. 硬切到直升机：在云下面绕着棋盘转圈，边转边拍剩下的陨石，最后看着一片火海。
        /// 每颗砸在一格岩浆上，砸出岩浆坑。棋子在画面里一直不动，切到直升机的那一瞬间直接换到第三阶段的位置。
        /// </summary>
        IEnumerator ShotMeteor(Board board, List<Piece> royals, HashSet<int> royalIds, Dictionary<int, Coord> temp)
        {
            var camPos = BoardView.SquareCenter(new Coord(shot2Square.x, shot2Square.y)) + shot2Offset;
            var boardCenter = new Vector3(0f, 0.3f, 0f);
            var meteors = PlanMeteors(board, camPos, boardCenter, out var leftoverLava);
            if (meteors.Count == 0) yield break;

            // 一开始看着第一颗要砸的地方（它从画面外突然砸进来）
            var firstEnd = meteors[0].End + Vector3.up * 0.3f;
            var look = Vector3.Lerp(boardCenter, firstEnd, 0.75f);
            var lookVelocity = Vector3.zero;
            float lastLand = meteors[meteors.Count - 1].Land;
            float total = lastLand + shot2Hold;
            bool scattered = false;
            // 记者机位拍前 reporterCount 颗，追的最后一颗落地、停一会儿后切到直升机
            int reporterCount = Mathf.Clamp(reporterMeteors, 1, meteors.Count);
            float cutTime = reporterCount < meteors.Count ? meteors[reporterCount - 1].Land + trackLinger : float.MaxValue;
            bool heliStarted = false;

            for (float t = 0; t < total; t += Time.deltaTime)
            {
                // 陨石：到时间就出现，按轨迹往下砸，落地就爆炸
                for (int i = 0; i < meteors.Count; i++)
                {
                    var m = meteors[i];
                    if (m.Landed) continue;
                    if (t >= m.Land - meteorFallDuration && m.Trail == null) m.Trail = SpawnMeteorTrail(m.Position(t, meteorFallDuration, fallAcceleration));
                    if (m.Trail != null) m.Trail.transform.localPosition = m.Position(t, meteorFallDuration, fallAcceleration);
                    if (t < m.Land) continue;

                    m.Landed = true;
                    StopTrail(m.Trail);
                    Explode(m.End);
                    if (board.IsLava(m.Target)) ctx.Board.RevealLava(m.Target);
                    ctx.Board.Highlight(null, null, null, null);
                    if (i == 0) Shake(firstImpactShake, firstImpactShakeDuration);
                    else Shake(impactShake, impactShakeDuration);
                    // 最后一颗砸完，陨石没砸到的岩浆格由小火球溅过去
                    if (i == meteors.Count - 1)
                    {
                        float delay = 0f;
                        foreach (var c in leftoverLava)
                        {
                            StartCoroutine(Splash(m.End, c, delay));
                            delay += 0.08f;
                        }
                    }
                }

                if (t < cutTime)
                {
                    // ---- 机位 A：战地记者（a1 外面，手持） ----
                    // 第一颗砸完 + 相机被震得压下去再抬起来之后，才去追下一颗；追第二颗时放大变焦
                    Vector3 target;
                    float slamEnd = meteors[0].Land + slamDuration;
                    if (t < slamEnd) target = Vector3.Lerp(boardCenter, firstEnd, 0.75f);
                    else
                    {
                        var next = meteors.Find(m => m != meteors[0] && t < m.Land + trackLinger);
                        target = next == null ? boardCenter : TrackPoint(next, t);
                        // 不切直升机（陨石全由记者机位拍）时：全砸完后云也要淡下来
                        if (next == null && t > lastLand)
                            ctx.Board.SetStormCloudOpacity(Mathf.Lerp(cloudOpacity, cloudOpacityAfterImpact, (t - lastLand) / Mathf.Max(0.01f, shot2Hold)));
                    }
                    look = Vector3.SmoothDamp(look, target, ref lookVelocity, trackSmooth);

                    // 没拿稳：猛地往下压（很快），再慢慢抬起来
                    float slam = Slam(t - meteors[0].Land);
                    var rotation = LookRotation(look - camPos) * Quaternion.Euler(slam * slamPitch, 0f, slam * slamRoll);
                    SetHandheldCamera(camPos + Vector3.down * slam * slamDrop, rotation, shot2Handheld);

                    float zoom = Mathf.SmoothStep(0f, 1f, (t - slamEnd) / Mathf.Max(0.05f, reporterZoomDuration));
                    ctx.Camera.fieldOfView = Mathf.Lerp(fieldOfView, reporterZoomFov, zoom);
                }
                else
                {
                    // ---- 机位 B：直升机在云下面绕着棋盘转圈 ----
                    float since = t - cutTime;
                    float angle = (heliStartAngle + heliSpeed * since) * Mathf.Deg2Rad;
                    var heliPos = new Vector3(Mathf.Sin(angle) * heliRadius, heliHeight, -Mathf.Cos(angle) * heliRadius);

                    var next = meteors.Find(m => meteors.IndexOf(m) >= reporterCount && t < m.Land + trackLinger);
                    Vector3 target;
                    if (next != null)
                    {
                        target = Vector3.Lerp(boardCenter, TrackPoint(next, t), heliTrackWeight);
                        target.y = Mathf.Min(target.y, heliLookMaxHeight);
                    }
                    else
                    {
                        // 全砸完了：看着棋盘中间的一片火海，云慢慢淡下来
                        target = boardCenter;
                        float u = Mathf.Clamp01((t - lastLand - trackLinger) / Mathf.Max(0.01f, shot2Hold));
                        ctx.Board.SetStormCloudOpacity(Mathf.Lerp(cloudOpacity, cloudOpacityAfterImpact, u));
                    }
                    if (!heliStarted)
                    {
                        // 硬切：镜头直接对好，不从上一个机位转过来。
                        // 棋子趁着切镜头直接换到第三阶段的位置（画面里不跳、不飞）
                        heliStarted = true;
                        if (!scattered)
                        {
                            scattered = true;
                            PlacePiecesForStage3(board, royals, royalIds, temp);
                        }
                        look = target;
                        lookVelocity = Vector3.zero;
                        ctx.Camera.fieldOfView = heliFieldOfView;
                    }
                    look = Vector3.SmoothDamp(look, target, ref lookVelocity, heliTrackSmooth);
                    // 机身往转弯里面倾斜：顺时针转（heliSpeed > 0）往右倾
                    var bank = Quaternion.Euler(0f, 0f, -Mathf.Sign(heliSpeed) * heliBank);
                    SetHandheldCamera(heliPos, LookRotation(look - heliPos) * bank, heliHandheld);
                }
                yield return null;
            }
            // 没切直升机（陨石全由记者机位拍）：镜头 2 结束、切到镜头 3 时再换位置
            if (!scattered) PlacePiecesForStage3(board, royals, royalIds, temp);
        }

        /// <summary>镜头追一颗陨石时看哪里：还没出现就先往它要来的天上看（像听见声音先抬头），在落就看它，落地了看落点。</summary>
        Vector3 TrackPoint(Meteor m, float t)
        {
            if (m.Landed) return m.End + Vector3.up * 0.3f;
            return m.Position(Mathf.Max(t, m.Land - meteorFallDuration * 0.85f), meteorFallDuration, fallAcceleration);
        }

        /// <summary>
        /// 相机被震得往下压的程度（0–1）：since = 第一颗落地后过了多久。
        /// 前 slamAttack 那一小段猛地压到底，剩下的时间慢慢抬回来。
        /// </summary>
        float Slam(float since)
        {
            if (since < 0f || since >= slamDuration) return 0f;
            float u = since / Mathf.Max(0.01f, slamDuration);
            if (u < slamAttack)
            {
                float k = u / slamAttack;
                return 1f - (1f - k) * (1f - k);
            }
            return 1f - Mathf.SmoothStep(0f, 1f, (u - slamAttack) / (1f - slamAttack));
        }

        /// <summary>
        /// 定好每颗陨石砸哪里、什么时候落地。先砸岩浆格，岩浆格不够就砸普通空格（不砸城堡）。
        /// 第一颗砸在镜头正前方；第二颗挑离第一颗最远的方向，镜头要转过去追；其余随机。
        /// leftoverLava = 陨石不够、没砸到的岩浆格。
        /// </summary>
        List<Meteor> PlanMeteors(Board board, Vector3 camPos, Vector3 boardCenter, out List<Coord> leftoverLava)
        {
            var lava = new List<Coord>();
            var plain = new List<Coord>();
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (board.IsLava(c)) lava.Add(c);
                else if (!board.IsCastle(c) && board.Height(c) == 0) plain.Add(c);
            }
            Shuffle(lava);
            Shuffle(plain);
            var targets = new List<Coord>(lava);
            targets.AddRange(plain);
            int count = Mathf.Clamp(meteorCount, 0, targets.Count);
            targets.RemoveRange(count, targets.Count - count);
            leftoverLava = lava.FindAll(c => !targets.Contains(c));
            if (count == 0) return new List<Meteor>();

            Vector3 Direction(Coord c) => (BoardView.SquareCenter(c) - camPos).normalized;
            var forward = (boardCenter - camPos).normalized;
            // 记者机位拍的那几颗砸下来时棋子还在风暴前的位置：尽量挑现在没有棋子站着的格子，
            // 不然岩浆坑会出现在一枚还站着的棋子底下
            var occupied = new bool[64];
            foreach (var piece in board.AllPieces)
                if (piece.OnBoard && ctx.Board.TryGetPieceView(piece.Id, out var view) && view.gameObject.activeSelf)
                    occupied[ToCoord(view.localPosition).Index] = true;
            float Occupied(Coord c) => occupied[c.Index] ? 2f : 0f;
            // 第一颗：在镜头正前方、不要太近
            int first = 0;
            float best = float.MinValue;
            for (int i = 0; i < targets.Count; i++)
            {
                float distance = (BoardView.SquareCenter(targets[i]) - camPos).magnitude;
                float score = Vector3.Dot(Direction(targets[i]), forward) - (distance < 2.5f ? 1f : 0f) - Occupied(targets[i]);
                if (score > best) { best = score; first = i; }
            }
            (targets[0], targets[first]) = (targets[first], targets[0]);
            // 第二颗：和第一颗方向差得最远，镜头要转过去
            if (targets.Count > 2)
            {
                int second = 1;
                float least = float.MaxValue;
                for (int i = 1; i < targets.Count; i++)
                {
                    float dot = Vector3.Dot(Direction(targets[i]), Direction(targets[0])) + Occupied(targets[i]);
                    if (dot < least) { least = dot; second = i; }
                }
                (targets[1], targets[second]) = (targets[second], targets[1]);
            }

            var meteors = new List<Meteor>();
            var away = new Vector3(meteorStartDirection.x, 0f, meteorStartDirection.y);
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            away.Normalize();
            for (int i = 0; i < targets.Count; i++)
            {
                var end = ctx.Board.PiecePosition(targets[i]);
                var direction = Quaternion.Euler(0f, UnityEngine.Random.Range(-35f, 35f), 0f) * away;
                var start = end + direction * UnityEngine.Random.Range(meteorStartDistance.x, meteorStartDistance.y)
                                + Vector3.up * UnityEngine.Random.Range(meteorStartHeight.x, meteorStartHeight.y);
                meteors.Add(new Meteor { Target = targets[i], Start = start, End = end, Land = LandTime(i) });
            }
            return meteors;
        }

        /// <summary>第 i 颗陨石的落地时间：按 meteorLandTimes，不够就按最后一个间隔往后排。</summary>
        float LandTime(int i)
        {
            var times = meteorLandTimes != null && meteorLandTimes.Length > 0 ? meteorLandTimes : new[] { 1f };
            if (i < times.Length) return times[i];
            float step = times.Length > 1 ? times[times.Length - 1] - times[times.Length - 2] : 1.2f;
            return times[times.Length - 1] + step * (i - times.Length + 1);
        }

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int k = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[k]) = (list[k], list[i]);
            }
        }

        /// <summary>
        /// 陨石的拖尾：RocketTrail 本来是发射火箭粒子、火和烟挂在火箭粒子上当子发射器。
        /// 这里只留火（Fire Small）、火星（Embers）、烟（Smoke）三条，改成普通的世界空间发射器挂在一个物体上，
        /// 按飞过的距离发射粒子，物体往下砸就拖出一道火和烟。没有实体。
        /// </summary>
        GameObject SpawnMeteorTrail(Vector3 localPosition)
        {
            var go = new GameObject("Cutscene Meteor");
            go.transform.SetParent(ctx.Board.Root, false);
            go.transform.localPosition = localPosition;
            temporary.Add(go);
            if (meteorEffect == null) return go;

            var fx = Instantiate(meteorEffect, go.transform);
            fx.transform.localPosition = Vector3.zero;
            fx.transform.localScale = Vector3.one * meteorEffectScale;
            var root = fx.GetComponent<ParticleSystem>();
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.prewarm = false;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (ps == root)
                {
                    // 火箭本身不要：不发射、不显示，也不再带子发射器
                    var emission = ps.emission;
                    emission.enabled = false;
                    var sub = ps.subEmitters;
                    sub.enabled = false;
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null) renderer.enabled = false;
                    continue;
                }
                if (ps.name != "Fire Small" && ps.name != "Embers" && ps.name != "Smoke")
                {
                    ps.gameObject.SetActive(false);
                    continue;
                }
                // 贴在拖尾物体上，粒子留在世界里，才拖得出一道
                ps.transform.SetParent(go.transform, false);
                ps.transform.localPosition = Vector3.zero;
                ps.transform.localScale = Vector3.one * meteorEffectScale;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true;
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Play(false);
            return go;
        }

        /// <summary>陨石落地：拖尾不再发射，已经拖出去的烟慢慢散掉再删。</summary>
        void StopTrail(GameObject trail)
        {
            if (trail == null) return;
            foreach (var ps in trail.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(trail, 5f);
        }

        void Explode(Vector3 at)
        {
            if (impactEffect == null) return;
            var fx = ParticleFx.Spawn(impactEffect, ctx.Board.Root, at + Vector3.up * 0.2f, impactScale, UnityEngine.Random.Range(0f, 360f));
            fx.name = "Cutscene Meteor Impact";
            if (!impactShockwave)
            {
                var shockwave = fx.transform.Find("Shockwave");
                if (shockwave != null) shockwave.gameObject.SetActive(false);
            }
            ParticleFx.PlayOnce(fx);
            temporary.Add(fx);
        }

        /// <summary>
        /// 切镜头的那一瞬间把所有棋子直接摆到第三阶段的位置（画面里看不到它们移动）。
        /// 王和王后先待在风暴前位置附近的空格，镜头 4 再逃进城堡。补回来的王后在这时出现。
        /// </summary>
        void PlacePiecesForStage3(Board board, List<Piece> royals, HashSet<int> royalIds, Dictionary<int, Coord> temp)
        {
            PlaceRoyals(board, royals, temp);
            foreach (var piece in board.AllPieces)
            {
                if (!piece.OnBoard || !ctx.Board.TryGetPieceView(piece.Id, out var view)) continue;
                view.gameObject.SetActive(true);
                view.localPosition = ctx.Board.PiecePosition(royalIds.Contains(piece.Id) ? temp[piece.Id] : piece.Pos);
            }
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

        /// <summary>
        /// 棋子视角站在 e2，手持晃动：看着山一层一层升起来（每升一级镜头震一下），
        /// 升完后镜头转向城堡、放大变焦。
        /// </summary>
        IEnumerator ShotMountains(Board board, List<Piece> royals, HashSet<int> royalIds, Dictionary<int, Coord> temp)
        {
            // 镜头 2 里变过焦、换过直升机的视野角，这里回到过场的默认值
            ctx.Camera.fieldOfView = fieldOfView;
            int maxLevel = ctx.Board.MaxTerrainLevel;
            var raised = new List<Coord>();
            Vector3 sum = Vector3.zero, castleSum = Vector3.zero;
            int castleCount = 0;
            for (int i = 0; i < 64; i++)
            {
                var c = Coord.FromIndex(i);
                if (board.IsCastle(c))
                {
                    castleSum += BoardView.SquareCenter(c) + Vector3.up * board.Height(c) * BoardView.LevelHeight;
                    castleCount++;
                }
                if (board.Height(c) <= 0) continue;
                raised.Add(c);
                sum += BoardView.SquareCenter(c);
            }
            if (raised.Count == 0 || maxLevel <= 0) yield break;
            var mountain = sum / raised.Count;
            // 城堡顶面中间再往上一点（站在上面的棋子大概的高度）；没有城堡就看山的最高处
            var castle = castleCount > 0 ? castleSum / castleCount + Vector3.up * 0.4f : mountain + Vector3.up * (maxLevel * BoardView.LevelHeight);

            var square = new Coord(shot3Square.x, shot3Square.y);
            // 相机站的那一格上要是有棋子，先藏起来（相机就在它的位置上）
            var hidden = new List<Transform>();
            if (hidePieceAtCamera)
                foreach (var piece in board.AllPieces)
                    if (piece.OnBoard && ctx.Board.TryGetPieceView(piece.Id, out var view) && view.gameObject.activeSelf &&
                        ToCoord(view.localPosition) == square)
                    {
                        view.gameObject.SetActive(false);
                        hidden.Add(view);
                    }

            float rise = 0f;
            var look = mountain;
            var lookVelocity = Vector3.zero;
            void Frame(Vector3 target)
            {
                // 相机跟着脚下的格子（e2 要是也在升，相机一起升）
                var camPos = ctx.Board.PiecePosition(square) + shot3Offset;
                look = Vector3.SmoothDamp(look, target, ref lookVelocity, 0.25f);
                SetHandheldCamera(camPos, LookRotation(look - camPos), shot3Handheld);
                foreach (var royal in royals)
                    if (ctx.Board.TryGetPieceView(royal.Id, out var view)) view.localPosition = ctx.Board.PiecePosition(temp[royal.Id]);
            }
            // 升的时候看着山的半腰，山越高看得越高
            Vector3 MountainLook() => mountain + Vector3.up * (rise * BoardView.LevelHeight * 0.6f);
            look = MountainLook();

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
                    rise = level + (1f - (1f - t) * (1f - t));
                    ctx.Board.SetTerrainRise(rise, royalIds);
                    Frame(MountainLook());
                    yield return null;
                }
                rise = level + 1;
                ctx.Board.SetTerrainRise(rise, royalIds);
                for (float t = 0; t < levelPause; t += Time.deltaTime)
                {
                    Frame(MountainLook());
                    yield return null;
                }
            }

            // 升完：镜头转向城堡，同时放大变焦
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, shot3ZoomDuration))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                ctx.Camera.fieldOfView = Mathf.Lerp(fieldOfView, shot3ZoomFov, k);
                Frame(Vector3.Lerp(MountainLook(), castle, k));
                yield return null;
            }
            ctx.Camera.fieldOfView = shot3ZoomFov;
            for (float t = 0; t < shot3Hold; t += Time.deltaTime)
            {
                Frame(castle);
                yield return null;
            }
            foreach (var view in hidden)
                if (view != null) view.gameObject.SetActive(true);
        }

        // ---------- 镜头 4 ----------

        IEnumerator ShotEscape(Board board, List<Piece> royals, Dictionary<int, Coord> temp)
        {
            // 镜头 3 最后放大看城堡，这里回到默认视野角
            ctx.Camera.fieldOfView = fieldOfView;
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
            var rotation = localRotation;
            if (Time.time < shakeUntil && shakeDuration > 0f)
            {
                float fade = (shakeUntil - Time.time) / shakeDuration;
                float s = shakeAmount * fade;
                float time = Time.time * 25f;
                position += new Vector3(Mathf.PerlinNoise(time, 0f) - 0.5f, Mathf.PerlinNoise(0f, time) - 0.5f, Mathf.PerlinNoise(time, time) - 0.5f) * 2f * s;
                // 角度也一起抖（每 0.1 的震动约 4 度），震得厉害时画面会明显歪一下
                float degrees = s * 40f;
                rotation *= Quaternion.Euler((Mathf.PerlinNoise(time, 3f) - 0.5f) * degrees, (Mathf.PerlinNoise(3f, time) - 0.5f) * degrees, (Mathf.PerlinNoise(time, 7f) - 0.5f) * degrees);
            }
            ctx.Camera.transform.SetPositionAndRotation(root.TransformPoint(position), root.rotation * rotation);
            // 文字、贴图平时朝着游戏视角摆；过场里改成朝着过场相机，从背面拍也不会是反的字
            if (viewReference != null) viewReference.rotation = ctx.Camera.transform.rotation;
        }

        static Quaternion LookRotation(Vector3 localDirection) =>
            localDirection.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(localDirection, Vector3.up);

        void Shake(float amount, float duration)
        {
            // 上一次震得更厉害、还没震完时不要被小的盖掉
            float remaining = Time.time < shakeUntil ? shakeAmount * (shakeUntil - Time.time) / shakeDuration : 0f;
            if (amount < remaining) return;
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
            // 文字、贴图回到朝着游戏视角（之后补建的棋子模型也按它摆正面）
            if (viewReference != null) viewReference.rotation = viewReferenceRotation;
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
