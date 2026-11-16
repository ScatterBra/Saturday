using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 规则书拿起来看：鼠标停在桌上的规则书上时描一圈高光轮廓，点一下规则书就飞到镜头前（正对相机、占画面大半），
    /// 再点规则书外面（或按 Esc）就放回桌上。规则文字和被吃区是挂在规则书下面的画布，跟着一起飞，
    /// 所以举在镜头前时被吃区的棋子照样能点（复活技能）。
    /// 举着时本子后面的东西虚化（URP 高斯景深，只在举着时打开相机的后处理），相机不能转（控制器里锁）。
    /// 由 ChessGameController 每帧调用 Tick；挂在 ChessDesk.ruleBook 上，art 是本子的模型。
    /// </summary>
    public class RuleBookFocus : MonoBehaviour
    {
        [Tooltip("轮廓颜色")]
        public Color outlineColor = new Color(1f, 0.85f, 0.35f);
        [Tooltip("轮廓宽度（世界单位，桌上的规则书 4 × 9 大小）")]
        public float outlineWidth = 0.08f;
        [Tooltip("举到镜头前时，下面这段本子占画面高度（或宽度）的比例")]
        [Range(0.3f, 1f)] public float screenFill = 0.88f;
        [Tooltip("举到镜头前时对准本子的哪一段：从本子上边往下数，到这个比例为止（0.71 ≈ 从 Rule Book 标题到 Lost pieces 标题；1 = 整本）。下面的被吃棋子会在画面外")]
        [Range(0.2f, 1f)] public float focusBottom = 0.71f;

        [Header("Background Blur")]
        [Tooltip("举着时把本子后面的东西虚化（URP 高斯景深）")]
        public bool blurBackground = true;
        [Tooltip("虚化有多糊（URP 高斯景深的 Max Radius，0.5–1.5）")]
        [Range(0.5f, 1.5f)] public float blurRadius = 1f;
        [Tooltip("举起、放下各用多久（秒）")]
        public float moveDuration = 0.35f;
        [Tooltip("举起来时离相机多远（世界单位）。要比相机近裁剪面远，也要比棋子、雷雨云近，免得穿模")]
        public float focusDistance = 3f;

        Camera cam;
        Renderer art;
        GameObject outline;
        /// <summary>本子模型在规则书自己坐标系里的包围盒（点选、算放多大用）。</summary>
        Bounds localBounds;
        Vector3 homePosition, homeScale;
        Quaternion homeRotation;
        bool focused;
        /// <summary>0 = 在桌上，1 = 在镜头前。</summary>
        float progress;
        Volume blurVolume;
        DepthOfField blur;
        UniversalAdditionalCameraData cameraData;
        bool originalPostProcessing;

        /// <summary>举着或正在飞：这时棋盘不响应点击、不弹棋子说明。</summary>
        public bool Active => focused || progress > 0f;

        public static RuleBookFocus Attach(Transform ruleBook, Camera camera, Material outlineMaterial)
        {
            if (ruleBook == null || camera == null) return null;
            Renderer artRenderer = null;
            foreach (var r in ruleBook.GetComponentsInChildren<Renderer>())
                if (r is MeshRenderer || r is SpriteRenderer) { artRenderer = r; break; }
            if (artRenderer == null) return null;
            var focus = ruleBook.gameObject.AddComponent<RuleBookFocus>();
            focus.cam = camera;
            focus.art = artRenderer;
            focus.Init(outlineMaterial);
            return focus;
        }

        void Init(Material outlineMaterial)
        {
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            homeScale = transform.localScale;

            // 本子的包围盒换到规则书自己的坐标系（八个角逐个换过来）
            var b = LocalBoundsOf(art);
            var artToBook = transform.worldToLocalMatrix * art.transform.localToWorldMatrix;
            localBounds = new Bounds(artToBook.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                localBounds.Encapsulate(artToBook.MultiplyPoint3x4(corner));
            }

            if (outlineMaterial != null && art.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                outline = BuildOutline(filter.sharedMesh, outlineMaterial);
            SetUpBlur();
        }

        /// <summary>
        /// 后面虚化：一个全局 Volume，里面是高斯景深，从本子后面一点开始糊。相机后处理平时关着，只在举起、放下和举着时打开，
        /// 平时画面和原来完全一样。Unity 默认的后处理配置里有色调映射、暗角、泛光，打开后处理会让画面变灰，
        /// 所以这里一起把它们关掉；Volume 权重一直是 1，渐变靠"从多远开始糊"从很远拉近，不会一开后处理就跳色。
        /// 本子和它上面的字（字是画在本子上的画布，深度就是本子的深度）都在虚化起点之前，所以是清楚的。
        /// </summary>
        void SetUpBlur()
        {
            if (!blurBackground) return;
            cameraData = cam.GetUniversalAdditionalCameraData();
            if (cameraData == null) return;
            originalPostProcessing = cameraData.renderPostProcessing;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            blur = profile.Add<DepthOfField>(true);
            blur.mode.Override(DepthOfFieldMode.Gaussian);
            blur.highQualitySampling.Override(true);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            profile.Add<Vignette>(true).intensity.Override(0f);
            profile.Add<Bloom>(true).intensity.Override(0f);
            var go = new GameObject("Rule Book Blur") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform.parent, false);
            go.layer = cam.gameObject.layer;
            blurVolume = go.AddComponent<Volume>();
            blurVolume.isGlobal = true;
            blurVolume.priority = 100;
            blurVolume.sharedProfile = profile;
            blurVolume.weight = 1f;
        }

        void ApplyBlur(float t)
        {
            if (blurVolume == null) return;
            float distance = Mathf.Max(focusDistance, cam.nearClipPlane * 2f);
            // 本子在 distance 处：举好时从它后面一点开始糊，再往后一点糊满；飞的过程中起点从相机最远处慢慢拉近
            float start = Mathf.Lerp(cam.farClipPlane, distance + 0.5f, t);
            blur.gaussianStart.Override(start);
            blur.gaussianEnd.Override(Mathf.Lerp(cam.farClipPlane + 1f, distance + 6f, t));
            blur.gaussianMaxRadius.Override(blurRadius);
            bool on = t > 0f;
            // 不举的时候 Volume 也关掉，别影响其他开着后处理的相机
            blurVolume.enabled = on;
            cameraData.renderPostProcessing = on || originalPostProcessing;
        }

        void OnDestroy()
        {
            if (blurVolume != null)
            {
                if (blurVolume.sharedProfile != null) Destroy(blurVolume.sharedProfile);
                Destroy(blurVolume.gameObject);
            }
            if (cameraData != null) cameraData.renderPostProcessing = originalPostProcessing;
        }

        static Bounds LocalBoundsOf(Renderer r)
        {
            if (r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null) return f.sharedMesh.bounds;
            if (r is SpriteRenderer s && s.sprite != null) return s.sprite.bounds;
            return new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>
        /// 轮廓：同一个模型复制一份、每个方向放大一圈（各边多出 outlineWidth）、只画背面，
        /// 本子本身挡住中间，露出来的一圈边就是轮廓。本子是个扁盒子，这样描出来很整齐。
        /// </summary>
        GameObject BuildOutline(Mesh mesh, Material material)
        {
            var go = new GameObject("Hover Outline", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(art.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(material) { name = material.name + " (runtime)", hideFlags = HideFlags.DontSave };
            renderer.sharedMaterial.color = outlineColor;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var lossy = art.transform.lossyScale;
            var e = mesh.bounds.extents;
            var c = mesh.bounds.center;
            // 前后左右各多出 outlineWidth，以包围盒中心为中心放大
            var scale = new Vector3(Grow(e.x, lossy.x), 1f, Grow(e.z, lossy.z));
            var position = c - Vector3.Scale(c, scale);
            // 上下不一样：从上往下看时露出来的那圈其实是外壳的底面，底面要在桌面上面一点点（不然被桌子挡住），顶面多出 outlineWidth
            if (e.y > 1e-5f && Mathf.Abs(lossy.y) > 1e-5f)
            {
                float lift = 0.01f / Mathf.Abs(lossy.y), grow = outlineWidth / Mathf.Abs(lossy.y);
                float bottom = c.y - e.y;
                scale.y = (2f * e.y + grow - lift) / (2f * e.y);
                position.y = bottom + lift - scale.y * bottom;
            }
            go.transform.localScale = scale;
            go.transform.localPosition = position;
            go.SetActive(false);
            return go;
        }

        float Grow(float extent, float lossy)
        {
            if (extent < 1e-5f || Mathf.Abs(lossy) < 1e-5f) return 1f;
            return (extent + outlineWidth / Mathf.Abs(lossy)) / extent;
        }

        /// <summary>
        /// 每帧调用。返回 true 表示这一帧的鼠标归规则书管（棋盘别再处理点击）。
        /// canInteract = 现在能不能拿起 / 放下（演出、转相机时不行）。
        /// </summary>
        public bool Tick(Ray ray, bool clicked, bool cancel, bool canInteract)
        {
            bool over = Hit(ray);
            if (outline != null) outline.SetActive(canInteract && !Active && over);
            if (Active)
            {
                // 举着的时候：点本子外面或按 Esc 放回去；点本子里面交给画布（被吃区的棋子）
                if (focused && canInteract && ((clicked && !over) || cancel)) focused = false;
                return true;
            }
            if (canInteract && clicked && over)
            {
                focused = true;
                return true;
            }
            return false;
        }

        /// <summary>马上放回桌上（重开一局时用）。</summary>
        public void PutBack()
        {
            focused = false;
            progress = 0f;
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            transform.localScale = homeScale;
            ApplyBlur(0f);
        }

        /// <summary>视线有没有打到本子（按本子的包围盒算，本子转到哪儿都准）。</summary>
        bool Hit(Ray ray)
        {
            var origin = transform.InverseTransformPoint(ray.origin);
            var direction = transform.InverseTransformDirection(ray.direction);
            return localBounds.IntersectRay(new Ray(origin, direction));
        }

        void LateUpdate()
        {
            float target = focused ? 1f : 0f;
            if (progress == target && !focused) return;
            progress = Mathf.MoveTowards(progress, target, moveDuration > 0f ? Time.unscaledDeltaTime / moveDuration : 1f);
            float t = Mathf.SmoothStep(0f, 1f, progress);

            var parent = transform.parent;
            var homeWorldPos = parent != null ? parent.TransformPoint(homePosition) : homePosition;
            var homeWorldRot = parent != null ? parent.rotation * homeRotation : homeRotation;
            var homeWorldScale = parent != null ? Vector3.Scale(parent.lossyScale, homeScale) : homeScale;

            FocusPose(homeWorldScale, out var focusPos, out var focusRot, out var focusScale);
            var worldPos = Vector3.Lerp(homeWorldPos, focusPos, t);
            var worldRot = Quaternion.Slerp(homeWorldRot, focusRot, t);
            var worldScale = Vector3.Lerp(homeWorldScale, focusScale, t);

            transform.SetPositionAndRotation(worldPos, worldRot);
            transform.localScale = parent != null ? Div(worldScale, parent.lossyScale) : worldScale;
            ApplyBlur(t);
        }

        /// <summary>
        /// 镜头前的位置：本子顶面正对相机、本子上边朝画面上方，离相机 focusDistance，
        /// 本子从上边往下 focusBottom 那一段缩放到占画面 screenFill、对准画面中心。
        /// 每帧按相机现在的位置算，转视角时本子跟着。
        /// </summary>
        void FocusPose(Vector3 homeWorldScale, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var camT = cam.transform;
            // 规则书自己的 +Y 是顶面（朝相机），+Z 是本子上边（朝画面上方）
            rotation = Quaternion.LookRotation(camT.up, -camT.forward);
            float distance = Mathf.Max(focusDistance, cam.nearClipPlane * 2f);
            float halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * cam.aspect;
            // 要看的那段：本子全宽，上边到往下 focusBottom 处（规则书自己的 +Z 是上边）
            float zTop = localBounds.max.z, zBottom = localBounds.max.z - localBounds.size.z * focusBottom;
            var size = Vector3.Scale(new Vector3(localBounds.extents.x, 0f, (zTop - zBottom) * 0.5f), homeWorldScale);
            float k = screenFill * Mathf.Min(halfHeight / Mathf.Max(size.z, 1e-4f), halfWidth / Mathf.Max(size.x, 1e-4f));
            scale = homeWorldScale * k;
            // 这段的中心（不是规则书的原点）对准画面中心；厚度方向让顶面落在 distance 处
            var center = Vector3.Scale(new Vector3(localBounds.center.x, localBounds.max.y, (zTop + zBottom) * 0.5f), scale);
            position = camT.position + camT.forward * distance - rotation * center;
        }

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
    }
}
