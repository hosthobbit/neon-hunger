# Attribution and licences

## Original to this project

Everything below was made for NEON HUNGER by Host Hobbit, in code. No third-party models, textures, sounds, music or code snippets are included.

- **Characters and models.** The energy creature, the four hunters (Stalker, Ambusher, Warden, Listener), the maze walls, living walls, risk doors, exit gate and energy pickups are built from primitive shapes at runtime by `Assets/NeonHunger/Scripts/View/` (using `MeshKit.cs`).
- **Textures.** Floor plates, wall panels, hazard stripes, warning plates, beams and glows are drawn procedurally by `Assets/NeonHunger/Editor/TextureKit.cs`.
- **Icons.** The UI icons (lives, dash bolt, threat arrows) are drawn at start-up by `Assets/NeonHunger/Scripts/UI/UiKit.cs`. The browser icon was drawn with ImageMagick for this project.
- **Sound and music.** The ambient soundtrack, the surge track, the hunters' voices and every effect are synthesised at runtime by `Assets/NeonHunger/Scripts/Audio/Sfx.cs`. No recorded audio is used.
- **Maze designs.** The three levels (INTAKE, FURNACE, CORE) are original layouts in `Assets/NeonHunger/Scripts/Core/Levels.cs`.
- **Game design and code.** This covers the game rules, the hunter AI, the UI, the scoreboard server (`server/neon-scores.php`) and the WebGL loading page.

NEON HUNGER is an original maze-chase game. It doesn't use any existing game's name, characters, artwork, sounds or maze layouts.

## Fonts

All fonts are used under the [SIL Open Font License 1.1](https://openfontlicense.org), which allows them to be bundled and redistributed with software. Their licence texts are next to the font files in `Assets/NeonHunger/Resources/Fonts/`.

| Font | Used for | Copyright | Source |
|---|---|---|---|
| Audiowide | titles and headings | © 2012 Brian J. Bonislawsky DBA Astigmatic (AOETI), Reserved Font Name "Audiowide" | [google/fonts](https://github.com/google/fonts/tree/main/ofl/audiowide) |
| Noto Sans Bold / Black | UI text | © 2022 The Noto Project Authors | [notofonts/latin-greek-cyrillic](https://github.com/notofonts/latin-greek-cyrillic) |

## Engine and packages

Made with Unity 6000.6.3f1 and Unity's own packages: Universal Render Pipeline, Input System, uGUI and Test Framework. These are used under the [Unity Terms of Service / Companion License](https://unity.com/legal).

- **Packages:** not stored in this repository; Unity Package Manager downloads them from the versions in `Packages/manifest.json` and `Packages/packages-lock.json`.
- **Unity runtime:** the committed browser build in `docs/` contains Unity's WebGL runtime, which may be distributed as part of a game made with Unity.

---

Developed by [Host Hobbit](https://hosthobbit.com).
