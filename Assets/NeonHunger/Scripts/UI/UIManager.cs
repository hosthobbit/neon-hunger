using System;
using System.Collections;
using System.Collections.Generic;
using NeonHunger.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NeonHunger
{
    /// <summary>
    /// All screens, built in code: title, controls, settings, credits, scoreboard, HUD (score, nodes,
    /// lives, dash meter, surge timer, threat pointers, hints, popups), pause, level clear, game over
    /// and victory with scoreboard entry. Keyboard, gamepad, mouse and touch all work.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public const string Credit = "Developed by Host Hobbit · hosthobbit.com";

        Canvas canvas;
        CanvasScaler scaler;
        RectTransform root;
        GameManager game;
        GameObject title, hud, pause, panel, modal, touchControls;
        GameObject modalReturnFocus;
        int modalClosedFrame = -1;
        PointerEventData pointer;
        readonly List<RaycastResult> hits = new List<RaycastResult>();

        // HUD
        Text scoreText, bestText, levelText, nodesText, hintText, introTitle, introSub, surgeText;
        Image dashFill, surgeFill, hintBack;
        RectTransform livesRow, surgeBar, popupLayer, threatLayer;
        CanvasGroup hintGroup, introGroup;
        readonly List<Image> lifeIcons = new List<Image>();
        readonly List<Image> threats = new List<Image>();
        readonly List<(Text t, Vector3 world, float age)> popups = new List<(Text, Vector3, float)>();
        float hintUntil;
        bool? portraitLayout;
        RectTransform titleMenu, titleLogo, titleTag, titleShade;
        InputField nameField;
        bool submitted;

        public bool ModalOpen => modal != null || modalClosedFrame == Time.frameCount;
        public bool TypingName => nameField && nameField.isFocused;
        public bool TouchMode => game != null && game.Input.Last == InputKind.Touch;
        public bool PanelOpen => panel && panel.activeSelf;
        public bool TitleOpen => title && title.activeSelf;

        // ================================================================== build

        public void Build(GameManager g)
        {
            game = g;
            UiKit.Init();
            var cgo = new GameObject("UI Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            cgo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)cgo.transform;
            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            pointer = new PointerEventData(EventSystem.current);
            BuildHud();
            BuildTitle();
            BuildPause();
            panel = UiKit.Rect("Panel", root).Fill().gameObject;
            panel.SetActive(false);
            Relayout(Screen.height > Screen.width);
        }

        void Update()
        {
            bool portrait = Screen.height > Screen.width;
            if (portraitLayout != portrait) Relayout(portrait);
            bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (back && modal) CloseModal();
            var es = EventSystem.current;
            bool menus = modal || title.activeSelf || pause.activeSelf || panel.activeSelf;
            if (es && menus && (es.currentSelectedGameObject == null || !es.currentSelectedGameObject.activeInHierarchy))
            {
                bool nav = (Keyboard.current != null && (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame))
                         || (Gamepad.current != null && (Gamepad.current.dpad.ReadValue().sqrMagnitude > 0.2f || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.3f));
                // a click on empty space clears the selection; arrows or Enter / Space / A bring it back
                bool submit = (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
                           || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
                if (nav || submit) Select(First(modal ? modal : pause.activeSelf ? pause : panel.activeSelf ? panel : title));
            }
        }

        void Relayout(bool portrait)
        {
            portraitLayout = portrait;
            scaler.referenceResolution = portrait ? new Vector2(1200, 2000) : new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = portrait ? 0f : 1f;
            if (portrait)
            {
                titleShade.Place(new Vector2(0, 0), new Vector2(1, 0.48f), new Vector2(-40, -40), new Vector2(40, 0));
                titleLogo.Place(new Vector2(0, 0.82f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero);
                titleTag.Place(new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.83f), Vector2.zero, Vector2.zero);
                titleMenu.Place(new Vector2(0.15f, 0.08f), new Vector2(0.85f, 0.44f), Vector2.zero, Vector2.zero);
            }
            else
            {
                titleShade.Place(Vector2.zero, new Vector2(0.44f, 1f), new Vector2(-40, -40), new Vector2(0, 40));
                titleLogo.Place(new Vector2(0.02f, 0.73f), new Vector2(0.42f, 0.97f), Vector2.zero, Vector2.zero);
                titleTag.Place(new Vector2(0.03f, 0.64f), new Vector2(0.41f, 0.72f), Vector2.zero, Vector2.zero);
                titleMenu.Place(new Vector2(0.09f, 0.1f), new Vector2(0.35f, 0.64f), Vector2.zero, Vector2.zero);
            }
        }

        static Selectable First(GameObject p)
        {
            foreach (var s in p.GetComponentsInChildren<Selectable>()) if (s.interactable && s.gameObject.activeInHierarchy && !(s is InputField)) return s;
            return null;
        }

        public static void Select(Selectable s)
        {
            if (!s || !EventSystem.current) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        public bool PointerOverUi(Vector2 screen)
        {
            pointer.position = screen; hits.Clear();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count > 0;
        }

        /// <summary>Any key / tap / button to skip the level-clear screen.</summary>
        public bool ContinuePressed() =>
            (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

        // ================================================================== title

        void BuildTitle()
        {
            title = UiKit.Rect("Title", root).Fill().gameObject;
            var shade = UiKit.Box(title.transform, "Shade", new Color(0f, 0f, 0.02f, 0.78f), UiKit.RoundedSmall);
            titleShade = shade.rectTransform;
            var logo = UiKit.Label(title.transform, "NEON\nHUNGER", 108, UiKit.Cyan, UiKit.Title);
            logo.lineSpacing = 0.85f;
            logo.Outlined(new Color(0.9f, 0.05f, 0.25f, 0.85f), 4f);
            logo.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -8);
            titleLogo = logo.rectTransform;
            var tag = UiKit.Label(title.transform, "You're clearing the maze while the maze is trying to trap you.", 30, new Color(0.85f, 0.75f, 1f), UiKit.Bold);
            titleTag = tag.rectTransform;
            var menu = UiKit.Rect("Menu", title.transform);
            titleMenu = menu;
            UiKit.Stack(menu, 14, 0, TextAnchor.MiddleCenter);
            UiKit.Button(menu, "PLAY", UiKit.Cyan, () => game.NewRun(), 46).Size(96);
            UiKit.Button(menu, "Controls", UiKit.ButtonColour, ShowControls, 32).Size(70);
            UiKit.Button(menu, "Settings", UiKit.ButtonColour, ShowSettings, 32).Size(70);
            UiKit.Button(menu, "Scoreboard", UiKit.ButtonColour, ShowScoreboard, 32).Size(70);
            UiKit.Button(menu, "Credits", UiKit.ButtonColour, ShowCredits, 32).Size(70);
            if (Application.platform != RuntimePlatform.WebGLPlayer) UiKit.Button(menu, "Quit", UiKit.ButtonColour, Application.Quit, 32).Size(70);
            var best = UiKit.Label(title.transform, "", 28, UiKit.Muted, UiKit.Bold);
            best.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 60), new Vector2(0, 100));
            best.name = "Best";
            var credit = UiKit.Label(title.transform, Credit, 24, UiKit.Muted, UiKit.Bold);
            credit.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 14), new Vector2(0, 52));
        }

        public void ShowTitle()
        {
            CloseModal();
            HidePanel();
            title.SetActive(true);
            hud.SetActive(false);
            pause.SetActive(false);
            var best = title.transform.Find("Best").GetComponent<Text>();
            best.text = Settings.BestScore > 0 ? $"Personal best  {Settings.BestScore:N0}" : "";
            Select(First(title));
        }

        public void ShowGame()
        {
            CloseModal();
            HidePanel();
            title.SetActive(false);
            pause.SetActive(false);
            hud.SetActive(true);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        // ================================================================== modals

        RectTransform OpenModal(string heading, float width, float height)
        {
            CloseModal();
            modalReturnFocus = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            modal = UiKit.Rect(heading, root).Fill().gameObject;
            var shade = UiKit.Box(modal.transform, "Shade", new Color(0, 0, 0.02f, 0.8f), UiKit.RoundedSmall);
            shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            var p = UiKit.Panel(modal.transform, "Panel");
            p.rectTransform.Center(new Vector2(Mathf.Min(width, root.rect.width - 40f), Mathf.Min(height, root.rect.height - 40f)));
            var h = UiKit.Label(p.transform, heading, 54, UiKit.Cyan, UiKit.Title);
            h.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -96), new Vector2(0, -14));
            var body = UiKit.Rect("Body", p.transform).Place(Vector2.zero, Vector2.one, new Vector2(46, 108), new Vector2(-46, -104));
            var close = UiKit.Button(p.transform, "Done", UiKit.Cyan, CloseModal, 32);
            close.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-140, 22), new Vector2(140, 88));
            Select(close);
            return body;
        }

        public void CloseModal()
        {
            if (!modal) return;
            Destroy(modal);
            modal = null;
            modalClosedFrame = Time.frameCount;
            if (modalReturnFocus && modalReturnFocus.activeInHierarchy) Select(modalReturnFocus.GetComponent<Selectable>());
        }

        public void ShowControls()
        {
            var body = OpenModal("Controls", 1300, 900);
            var t = UiKit.Label(body,
                "<b>Steer</b>   WASD or arrow keys · gamepad stick / d-pad · <b>swipe</b> on touch screens\n" +
                "Turns are remembered for a moment, so press just before a junction.\n\n" +
                "<b>Dash</b>   SPACE or SHIFT · gamepad A · the DASH button on touch screens\n" +
                "A short burst of speed with a cooldown. It's <color=#FF3D55>loud</color>: hunters nearby turn to look, and the Listener comes from anywhere.\n\n" +
                "<b>Pause</b>   ESC or P · gamepad Start      <b>Restart</b>   R · gamepad Select\n\n" +
                "<b>The rules</b>   Eat every <color=#40EBFF>energy node</color>, then reach the <b>EXIT</b>. A <color=#A866FF>violet core</color> sets off a power surge: the hunters flee, and catching them scores 200, 400, 800, 1600. " +
                "<color=#FFB84D>Warning plates</color> mean walls are about to move. <color=#FF3D55>RISK</color> chambers hold big bonuses, but the door seals on a timer, so get out before it does.\n\n" +
                "<b>The hunters</b>   <color=#FF2944>Stalker</color> chases you.  <color=#B84DFF>Ambusher</color> cuts you off at the next junction.  " +
                "<color=#FFA833>Warden</color> guards cores, gems and the exit.  <color=#33FFB3>Listener</color> follows noise.",
                27, UiKit.TextColour, UiKit.Bold, TextAnchor.UpperLeft);
            t.rectTransform.Fill();
            t.lineSpacing = 1.1f;
        }

        public void ShowSettings()
        {
            var body = OpenModal("Settings", 1100, 900);
            var stack = UiKit.Rect("Stack", body).Fill();
            UiKit.Stack(stack, 14);
            void SliderRow(string label, float v, Action<float> set)
            {
                var row = UiKit.Rect(label, stack); row.Size(64);
                var l = UiKit.Label(row, label, 30, UiKit.TextColour, UiKit.Bold, TextAnchor.MiddleLeft);
                l.rectTransform.Place(Vector2.zero, new Vector2(0.35f, 1f), Vector2.zero, Vector2.zero);
                var s = UiKit.Slider(row, v, set);
                s.GetComponent<RectTransform>().Place(new Vector2(0.38f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0, -20), new Vector2(-10, 20));
            }
            SliderRow("Music", Settings.Music, v => { Settings.Music = v; game.ApplySettings(); });
            SliderRow("Sound effects", Settings.Sound, v => { Settings.Sound = v; game.ApplySettings(); Sfx.Play(Sfx.Kind.Node, 0.6f); });
            Button Toggle(string label, Func<string> text, Action flip)
            {
                Button b = null;
                b = UiKit.Button(stack, "", UiKit.ButtonColour, () => { flip(); Settings.Save(); game.ApplySettings(); UiKit.LabelOf(b).text = text(); }, 30);
                UiKit.LabelOf(b).text = text();
                b.Size(66);
                return b;
            }
            var first = Toggle("mute", () => "Sound: " + (Settings.Muted ? "Muted" : "On"), () => Settings.Muted = !Settings.Muted);
            Toggle("quality", () => $"Graphics: {Settings.QualityNames[Mathf.Clamp(Settings.Quality, 0, 2)]}", () => Settings.Quality = (Settings.Quality + 1) % 3);
            Toggle("shake", () => "Camera shake: " + (Settings.ReducedShake ? "Reduced" : "Full"), () => Settings.ReducedShake = !Settings.ReducedShake);
            Toggle("flash", () => "Flashing effects: " + (Settings.ReducedFlashing ? "Reduced" : "Full"), () => Settings.ReducedFlashing = !Settings.ReducedFlashing);
            var note = UiKit.Label(stack, "Graphics changes apply fully from the next level. Settings and your best score are saved on this device.", 22, UiKit.Muted, UiKit.Bold);
            note.Size(56);
            Select(first);
        }

        public void ShowCredits()
        {
            var body = OpenModal("Credits", 1100, 760);
            var t = UiKit.Label(body,
                "<b>NEON HUNGER</b>\nA game by <b>Host Hobbit</b> · hosthobbit.com\n\n" +
                "Design, code, 3D models, textures, music and sound: original, made for this game (procedurally generated in code).\n\n" +
                "Fonts: <b>Audiowide</b> by Astigmatic and <b>Noto Sans</b> by the Noto Project Authors, both under the SIL Open Font License.\n\n" +
                "Made with Unity.",
                28, UiKit.TextColour, UiKit.Bold, TextAnchor.UpperCenter);
            t.rectTransform.Fill();
        }

        public void ShowScoreboard()
        {
            var body = OpenModal("Scoreboard", 1100, 960);
            var t = UiKit.Label(body, "Loading the scoreboard…", 28, UiKit.Muted, UiKit.Bold, TextAnchor.UpperCenter);
            t.rectTransform.Fill();
            StartCoroutine(Scoreboard.Fetch((list, err) =>
            {
                if (!t) return;
                if (err != null) { t.text = err; return; }
                if (list.Count == 0) { t.text = "No scores yet. Be the first!"; return; }
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    string col = i == 0 ? "#40EBFF" : i < 3 ? "#A866FF" : "#E6EEFF";
                    sb.Append($"<color={col}>{i + 1,2}.  {e.name}</color>   <b>{e.score:N0}</b>   <color=#8C99B8>{(e.won ? "escaped" : "level " + e.level)}  {e.date}</color>\n");
                }
                t.text = sb.ToString();
                t.fontSize = 28;
                t.alignment = TextAnchor.UpperLeft;
            }));
        }

        // ================================================================== pause

        void BuildPause()
        {
            pause = UiKit.Rect("Pause", root).Fill().gameObject;
            var shade = UiKit.Box(pause.transform, "Shade", new Color(0, 0, 0.02f, 0.75f), UiKit.RoundedSmall);
            shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            var p = UiKit.Panel(pause.transform, "Panel");
            p.rectTransform.Center(new Vector2(620, 700));
            var col = UiKit.Rect("Menu", p.transform).Place(Vector2.zero, Vector2.one, new Vector2(56, 46), new Vector2(-56, -36));
            UiKit.Stack(col, 14);
            UiKit.Label(col, "PAUSED", 64, UiKit.Cyan, UiKit.Title).Size(96);
            UiKit.Button(col, "Resume", UiKit.Cyan, () => game.SetPaused(false), 36).Size(80);
            UiKit.Button(col, "Restart (R)", UiKit.ButtonColour, () => game.Restart(), 30).Size(68);
            UiKit.Button(col, "Controls", UiKit.ButtonColour, ShowControls, 30).Size(68);
            UiKit.Button(col, "Settings", UiKit.ButtonColour, ShowSettings, 30).Size(68);
            UiKit.Button(col, "Quit to Title", UiKit.ButtonColour, () => game.GoToTitle(), 30).Size(68);
            pause.SetActive(false);
        }

        public void ShowPause(bool on)
        {
            if (!on) CloseModal();
            pause.SetActive(on);
            if (on) Select(First(pause)); else if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        // ================================================================== HUD

        void BuildHud()
        {
            hud = UiKit.Rect("HUD", root).Fill().gameObject;
            scoreText = UiKit.Label(hud.transform, "0", 54, UiKit.TextColour, UiKit.Title, TextAnchor.UpperLeft);
            scoreText.rectTransform.Place(new Vector2(0, 1), new Vector2(0.4f, 1), new Vector2(30, -90), new Vector2(0, -18));
            scoreText.Outlined(UiKit.Ink, 3f);
            bestText = UiKit.Label(hud.transform, "", 22, UiKit.Muted, UiKit.Bold, TextAnchor.UpperLeft);
            bestText.rectTransform.Place(new Vector2(0, 1), new Vector2(0.4f, 1), new Vector2(32, -124), new Vector2(0, -88));
            levelText = UiKit.Label(hud.transform, "", 30, new Color(0.85f, 0.75f, 1f), UiKit.Title);
            levelText.rectTransform.Place(new Vector2(0.3f, 1), new Vector2(0.7f, 1), new Vector2(0, -62), new Vector2(0, -16));
            nodesText = UiKit.Label(hud.transform, "", 26, UiKit.Cyan, UiKit.Bold);
            nodesText.rectTransform.Place(new Vector2(0.3f, 1), new Vector2(0.7f, 1), new Vector2(0, -100), new Vector2(0, -60));
            livesRow = UiKit.Rect("Lives", hud.transform).Place(new Vector2(0.6f, 1), new Vector2(1, 1), new Vector2(0, -80), new Vector2(-30, -20));
            var h = livesRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleRight; h.spacing = 10; h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false;

            // dash meter (bottom left)
            var dash = UiKit.Rect("Dash", hud.transform).Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 28), new Vector2(330, 78));
            var bolt = UiKit.Icon(dash, UiKit.Bolt, UiKit.Cyan, 46);
            bolt.rectTransform.anchorMin = bolt.rectTransform.anchorMax = new Vector2(0, 0.5f); bolt.rectTransform.anchoredPosition = new Vector2(24, 0);
            var track = UiKit.Box(dash, "Track", new Color(0.1f, 0.12f, 0.2f), UiKit.RoundedSmall);
            track.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(56, -9), new Vector2(0, 9));
            dashFill = UiKit.Box(track.transform, "Fill", UiKit.Cyan, UiKit.RoundedSmall);
            dashFill.rectTransform.Fill();
            var dl = UiKit.Label(dash, "DASH", 18, UiKit.Muted, UiKit.Bold, TextAnchor.LowerLeft);
            dl.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(58, 34), new Vector2(0, 26));

            // surge timer (bottom centre)
            surgeBar = UiKit.Rect("Surge", hud.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-300, 30), new Vector2(300, 74));
            var st = UiKit.Box(surgeBar, "Track", new Color(0.12f, 0.06f, 0.2f), UiKit.RoundedSmall); st.rectTransform.Fill();
            surgeFill = UiKit.Box(st.transform, "Fill", UiKit.Purple, UiKit.RoundedSmall); surgeFill.rectTransform.Fill();
            surgeText = UiKit.Label(surgeBar, "POWER SURGE", 24, UiKit.TextColour, UiKit.Title);
            surgeText.rectTransform.Fill();

            // hint banner
            hintBack = UiKit.Box(hud.transform, "Hint", new Color(0.02f, 0.02f, 0.06f, 0.85f), UiKit.Rounded);
            hintBack.rectTransform.Place(new Vector2(0.2f, 1), new Vector2(0.8f, 1), new Vector2(0, -196), new Vector2(0, -116));
            hintText = UiKit.Label(hintBack.transform, "", 28, UiKit.TextColour, UiKit.Bold);
            hintText.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(20, 4), new Vector2(-20, -4));
            hintGroup = hintBack.gameObject.AddComponent<CanvasGroup>();
            hintGroup.alpha = 0f; hintGroup.blocksRaycasts = false;

            // level intro banner
            var intro = UiKit.Rect("Intro", hud.transform).Fill();
            introGroup = intro.gameObject.AddComponent<CanvasGroup>();
            introGroup.blocksRaycasts = false; introGroup.alpha = 0f;
            var band = UiKit.Box(intro, "Band", new Color(0, 0, 0.03f, 0.7f), UiKit.RoundedSmall);
            band.rectTransform.Place(new Vector2(0, 0.42f), new Vector2(1, 0.62f), new Vector2(-20, 0), new Vector2(20, 0));
            introTitle = UiKit.Label(intro, "", 84, UiKit.Cyan, UiKit.Title);
            introTitle.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.62f), Vector2.zero, Vector2.zero);
            introTitle.Outlined(new Color(0.9f, 0.05f, 0.25f, 0.7f), 3f);
            introSub = UiKit.Label(intro, "", 32, new Color(0.85f, 0.75f, 1f), UiKit.Bold);
            introSub.rectTransform.Place(new Vector2(0, 0.42f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);

            threatLayer = UiKit.Rect("Threats", hud.transform).Fill();
            popupLayer = UiKit.Rect("Popups", hud.transform).Fill();

            // touch: DASH and pause buttons
            touchControls = UiKit.Rect("Touch", hud.transform).Fill().gameObject;
            var dashBtn = UiKit.Button(touchControls.transform, "DASH", new Color(0.1f, 0.5f, 0.6f, 0.75f), () => game.Input.TouchDash = true, 34);
            dashBtn.GetComponent<RectTransform>().Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-250, 40), new Vector2(-40, 250));
            dashBtn.GetComponent<Image>().sprite = UiKit.Circle;
            var pauseBtn = UiKit.Button(touchControls.transform, "II", UiKit.ButtonColour, () => game.SetPaused(true), 30);
            pauseBtn.GetComponent<RectTransform>().Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -200), new Vector2(-30, -120));
            foreach (var b in touchControls.GetComponentsInChildren<Button>()) b.navigation = new Navigation { mode = Navigation.Mode.None };
            hud.SetActive(false);
        }

        public void LevelIntro(LevelDef def)
        {
            introTitle.text = $"LEVEL {def.Number}  ·  {def.Name}";
            introSub.text = def.Subtitle;
            StopCoroutine(nameof(IntroFade));
            StartCoroutine(nameof(IntroFade));
        }

        IEnumerator IntroFade()
        {
            for (float t = 0; t < 2.6f; t += Time.unscaledDeltaTime)
            {
                introGroup.alpha = Mathf.Clamp01(Mathf.Min(t * 5f, (2.6f - t) * 2.5f));
                yield return null;
            }
            introGroup.alpha = 0f;
        }

        public void Hint(string text, float seconds)
        {
            hintText.text = text;
            hintUntil = Time.unscaledTime + seconds;
        }

        public void Popup(Vector3 world, string text, Color c)
        {
            var t = UiKit.Label(popupLayer, text, 34, c, UiKit.Title);
            t.Outlined(UiKit.Ink, 2f);
            t.rectTransform.sizeDelta = new Vector2(400, 60);
            popups.Add((t, world, 0f));
        }

        public void UpdateHud(GameManager g)
        {
            // popups float up and fade (also on the title, harmlessly)
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var (t, w, age) = popups[i];
                age += Time.unscaledDeltaTime;
                if (age > 1.2f || !t) { if (t) Destroy(t.gameObject); popups.RemoveAt(i); continue; }
                Place(t.rectTransform, w + Vector3.up * age * 1.2f);
                t.color = new Color(t.color.r, t.color.g, t.color.b, 1f - age / 1.2f);
                popups[i] = (t, w, age);
            }
            hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, Time.unscaledTime < hintUntil && hud.activeSelf ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (!hud.activeSelf || g.Sim == null) return;
            var sim = g.Sim;
            scoreText.text = g.Run.Total.ToString("N0");
            bestText.text = $"BEST {Mathf.Max(Settings.BestScore, g.Run.Total):N0}";
            levelText.text = $"{sim.Def.Number} · {sim.Def.Name}";
            nodesText.text = sim.Maze.NodesLeft > 0 ? $"NODES {sim.Maze.NodesLeft}" : "<color=#FFFFFF>EXIT OPEN</color>";
            // lives
            int lives = Mathf.Clamp(g.Run.Lives, 0, 8);
            while (lifeIcons.Count < lives)
            {
                var ic = UiKit.Icon(livesRow, UiKit.Life, PlayerView.Warm, 44);
                lifeIcons.Add(ic);
            }
            for (int i = 0; i < lifeIcons.Count; i++) lifeIcons[i].gameObject.SetActive(i < lives);
            // dash
            float ready = 1f - sim.DashCooldownLeft / GameSim.DashCooldown;
            dashFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(ready), 1f);
            dashFill.color = ready >= 1f ? UiKit.Cyan : new Color(0.2f, 0.4f, 0.5f);
            // surge
            surgeBar.gameObject.SetActive(sim.SurgeActive);
            if (sim.SurgeActive)
            {
                float k = sim.SurgeLeft / sim.Def.SurgeSeconds;
                surgeFill.rectTransform.anchorMax = new Vector2(k, 1f);
                bool ending = sim.SurgeEnding;
                surgeFill.color = ending && !Settings.ReducedFlashing && Mathf.Repeat(Time.time * 5f, 1f) > 0.5f ? Color.white : UiKit.Purple;
                surgeText.text = ending ? "SURGE ENDING!" : "POWER SURGE";
            }
            touchControls.SetActive(TouchMode && (g.Current == GameManager.Phase.Playing || g.Current == GameManager.Phase.Intro) && !g.Paused);
            UpdateThreats(g);
        }

        bool Place(RectTransform r, Vector3 world)
        {
            var cam = game.Cam;
            var sp = cam.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var local);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = local;
            return sp.z > 0f;
        }

        /// <summary>Pointers at the screen edge for nearby hunters you can't see.</summary>
        void UpdateThreats(GameManager g)
        {
            var sim = g.Sim;
            int used = 0;
            if (g.Current == GameManager.Phase.Playing && g.HunterViews != null)
            {
                var me = sim.Player.Tile;
                for (int i = 0; i < sim.Hunters.Length; i++)
                {
                    var h = sim.Hunters[i];
                    if (h.Mode != HunterMode.Hunting || h.Mover.Tile.Manhattan(me) > 11) continue;
                    var sp = g.Cam.WorldToViewportPoint(Art.World(h.Mover.X, h.Mover.Y, 0.5f));
                    if (sp.z > 0 && sp.x > 0.02f && sp.x < 0.98f && sp.y > 0.02f && sp.y < 0.98f) continue;
                    while (threats.Count <= used)
                    {
                        var img = UiKit.Icon(threatLayer, UiKit.Arrow, Color.white, 56);
                        threats.Add(img);
                    }
                    var a = threats[used++];
                    a.gameObject.SetActive(true);
                    var dir = new Vector2(sp.x - 0.5f, sp.y - 0.5f);
                    if (sp.z < 0) dir = -dir;
                    float m = Mathf.Max(Mathf.Abs(dir.x) / 0.45f, Mathf.Abs(dir.y) / 0.42f);
                    var edge = dir / Mathf.Max(m, 0.0001f);
                    a.rectTransform.anchorMin = a.rectTransform.anchorMax = new Vector2(0.5f + edge.x, 0.5f + edge.y);
                    a.rectTransform.anchoredPosition = Vector2.zero;
                    a.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                    float near = 1f - Mathf.Clamp01((h.Mover.Tile.Manhattan(me) - 3) / 8f);
                    var c = Art.HunterColour(h.Kind);
                    a.color = new Color(c.r, c.g, c.b, 0.35f + 0.65f * near);
                    a.rectTransform.localScale = Vector3.one * (0.8f + 0.5f * near);
                }
            }
            for (int i = used; i < threats.Count; i++) threats[i].gameObject.SetActive(false);
        }

        // ================================================================== panels

        void HidePanel() { foreach (Transform c in panel.transform) Destroy(c.gameObject); panel.SetActive(false); }

        RectTransform OpenPanel(float w, float h)
        {
            HidePanel();
            panel.SetActive(true);
            var shade = UiKit.Box(panel.transform, "Shade", new Color(0, 0, 0.02f, 0.55f), UiKit.RoundedSmall);
            shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            var p = UiKit.Panel(panel.transform, "Panel");
            p.rectTransform.Center(new Vector2(Mathf.Min(w, root.rect.width - 40f), h));
            return (RectTransform)p.transform;
        }

        public void ShowLevelClear(GameManager g)
        {
            var s = g.Sim.Stats;
            var def = g.Sim.Def;
            var p = OpenPanel(900, 620);
            var h = UiKit.Label(p, $"{def.Name} CLEARED", 60, UiKit.Cyan, UiKit.Title);
            h.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -110), new Vector2(0, -20));
            int hunters = s.Combo[0] + s.Combo[1] + s.Combo[2] + s.Combo[3];
            var body = UiKit.Label(p,
                $"Energy nodes   <b>{s.Nodes}</b>\nCores · gems · risk bonus   <b>{s.Powers} · {s.Gems} · {s.RiskGems}</b>\nHunters caught   <b>{hunters}</b>\n" +
                $"Time   <b>{s.Seconds:0}s</b>   (par {def.ParSeconds:0}s, bonus +{Points.TimeBonus(s.Seconds, def.ParSeconds)})\nLevel bonus   <b>+{Points.LevelClear(def.Number)}</b>\n\n" +
                $"<size=40><color=#40EBFF>{g.Run.Total:N0}</color></size>",
                30, UiKit.TextColour, UiKit.Bold);
            body.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(40, 90), new Vector2(-40, -120));
            var next = UiKit.Label(p, def.Number < LevelDef.All.Length ? "The next level is loading… (press any key)" : "", 22, UiKit.Muted, UiKit.Bold);
            next.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 30), new Vector2(0, 80));
        }

        public void ShowRunEnd(GameManager g, bool won)
        {
            submitted = false;
            var p = OpenPanel(960, 800);
            var h = UiKit.Label(p, won ? "YOU ESCAPED" : "CONSUMED", 76, won ? UiKit.Cyan : UiKit.Red, UiKit.Title);
            h.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -120), new Vector2(0, -20));
            var sub = UiKit.Label(p, won ? "The machine goes dark behind you." : $"The machine caught you on level {g.LevelIndex + 1}.", 28, UiKit.Muted, UiKit.Bold);
            sub.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -170), new Vector2(0, -118));
            var score = UiKit.Label(p, $"{g.Run.Total:N0}" + (g.NewBest ? "\n<size=28><color=#A866FF>NEW PERSONAL BEST</color></size>" : $"\n<size=26><color=#8C99B8>best {Settings.BestScore:N0}</color></size>"), 72, UiKit.TextColour, UiKit.Title);
            score.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -340), new Vector2(0, -180));

            // scoreboard entry
            var row = UiKit.Rect("Submit", p).Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(50, 200), new Vector2(-50, 276));
            var fieldBg = UiKit.Box(row, "Name", new Color(0.08f, 0.09f, 0.16f));
            fieldBg.rectTransform.Place(Vector2.zero, new Vector2(0.55f, 1f), Vector2.zero, new Vector2(-10, 0));
            nameField = fieldBg.gameObject.AddComponent<InputField>();
            var txt = UiKit.Label(fieldBg.transform, "", 30, UiKit.TextColour, UiKit.Bold, TextAnchor.MiddleLeft);
            txt.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, 0));
            txt.supportRichText = false;
            var ph = UiKit.Label(fieldBg.transform, "Your name", 30, UiKit.Muted, UiKit.Bold, TextAnchor.MiddleLeft);
            ph.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-18, 0));
            nameField.textComponent = txt; nameField.placeholder = ph; nameField.characterLimit = 16;
            nameField.text = Settings.PlayerName;
            var status = UiKit.Label(p, "Put your run on the public scoreboard", 24, UiKit.Muted, UiKit.Bold);
            status.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 150), new Vector2(-40, 196));
            Button send = null;
            send = UiKit.Button(row, "Submit score", UiKit.Purple, () =>
            {
                if (submitted) return;
                var name = nameField.text.Trim();
                if (name.Length < 2) { status.text = "Names need at least 2 characters."; return; }
                Settings.PlayerName = name; Settings.Save();
                status.text = "Sending…";
                send.interactable = false;
                StartCoroutine(Scoreboard.Submit(name, g.Run, (ok, msg) =>
                {
                    status.text = msg;
                    submitted = ok;
                    if (send) send.interactable = !ok;
                }));
            }, 30);
            send.GetComponent<RectTransform>().Place(new Vector2(0.55f, 0), Vector2.one, new Vector2(10, 0), Vector2.zero);

            var buttons = UiKit.Rect("Buttons", p).Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(50, 40), new Vector2(-50, 128));
            UiKit.Row(buttons, 24);
            var again = UiKit.Button(buttons, "Play again (R)", UiKit.Cyan, () => game.NewRun(), 34);
            UiKit.Button(buttons, "Scoreboard", UiKit.ButtonColour, ShowScoreboard, 30);
            UiKit.Button(buttons, "Title", UiKit.ButtonColour, () => game.GoToTitle(), 30);
            Select(again);
        }
    }
}
