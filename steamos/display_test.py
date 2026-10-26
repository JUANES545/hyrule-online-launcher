"""Tests for display.py with small fixture packs: no Cemu, no display, no GPU. Run: python3 steamos/display_test.py"""
import os
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import display as D

XRANDR = """Screen 0: minimum 1 x 1, current 1920 x 1080, maximum 16384 x 16384
XWAYLAND0 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 0mm x 0mm
   1920x1080    119.93*+  60.00
   1280x800      59.99
HDMI-1 connected 2560x1440+0+0
   2560x1440     59.95*+
DP-2 disconnected (normal left inverted right x axis y axis)
"""

STOCK = """[Definition]
titleIds = 00050000101C9400,00050000101C9500
name = Graphics
path = "The Legend of Zelda: Breath of the Wild/Graphics"
version = 5

[Preset]
name = Low (Low Draw Distance, Sharp Shadows)
category = Shadows
$shadowRes = 1

[Preset]
name = Medium (100%, Default)
category = Shadows
default = 1

[Preset]
name = High (200%)
category = Shadows

[Preset]
name = Very High (Recommended)
category = Shadow Draw Distance

[Preset]
name = Normal FXAA (Default)
category = Anti-Aliasing
default = 1
"""

FPS = """[Definition]
name = FPS++

[Preset]
name = 60FPS Limit
category = FPS Limit
$fpsLimitNormal = 60

[Preset]
name = 120FPS Limit
category = FPS Limit
$fpsLimitNormal = 120

[Preset]
name = 144FPS Limit
category = FPS Limit
$fpsLimitNormal = 144

[Preset]
name = 20FPS Limit
category = FPS Limit
$fpsLimitNormal = 20
"""

SETTINGS = """<?xml version="1.0" encoding="UTF-8"?>
<content>
    <console_language>1</console_language>
    <GraphicPack>
        <Entry filename="graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Graphics\\rules.txt"/>
        <Entry filename="graphicPacks\\HyruleOnline\\Graphics\\rules.txt">
            <Preset><category>Aspect Ratio</category><preset>Old</preset></Preset>
            <Preset><category>Square HUD Mode</category><preset>Enabled</preset></Preset>
            <Preset><category>Extra</category><preset>Kept</preset></Preset>
        </Entry>
    </GraphicPack>
    <Graphic><api>0</api></Graphic>
    <Audio><TVVolume>5</TVVolume></Audio>
</content>
"""


def install(root, fps=True):
    stock = os.path.join(root, "cemu", D.STOCK)
    os.makedirs(stock)
    with open(os.path.join(stock, "rules.txt"), "w", newline="") as f:
        f.write(STOCK)
    open(os.path.join(stock, "patch.asm"), "w").close()
    if fps:
        os.makedirs(os.path.dirname(os.path.join(root, "cemu", D.FPS_PACK)))
        with open(os.path.join(root, "cemu", D.FPS_PACK), "w") as f:
            f.write(FPS)
    with open(os.path.join(root, "cemu", "settings.xml"), "w") as f:
        f.write(SETTINGS)


class Screen(unittest.TestCase):
    def test_xrandr_takes_the_primary_output_and_rounds_the_rate(self):
        self.assertEqual(D.parse_xrandr(XRANDR), (1920, 1080, 120))

    def test_xrandr_without_a_connected_output(self):
        self.assertIsNone(D.parse_xrandr("Screen 0: minimum 1 x 1\nDP-2 disconnected\n   1920x1080 60.00*\n"))

    def test_the_panel_display_is_asked_before_the_game_display(self):
        seen = []

        def run(cmd, env, **kw):
            seen.append(env["DISPLAY"])
            return type("R", (), {"stdout": XRANDR if env["DISPLAY"] == ":0" else ""})()
        self.assertEqual(D.detect_screen({"DISPLAY": ":1"}, run), (1920, 1080, 120))
        self.assertEqual(seen, [":0"])

    def test_the_session_display_is_the_fallback(self):
        run = lambda cmd, env, **kw: type("R", (), {"stdout": XRANDR if env["DISPLAY"] == ":3" else ""})()
        self.assertEqual(D.detect_screen({"DISPLAY": ":3"}, run), (1920, 1080, 120))


class Sizes(unittest.TestCase):
    def test_render_size_follows_the_scale_with_even_sides(self):
        self.assertEqual(D.render_size(1920, 1080, 1.0), (1920, 1080))
        self.assertEqual(D.render_size(1920, 1080, 0.75), (1440, 810))
        self.assertEqual(D.render_size(1280, 800, 1.25), (1600, 1000))
        self.assertEqual(D.render_size(1366, 768, 0.5), (682, 384))

    def test_render_size_never_passes_the_limit(self):
        w, h = D.render_size(7680, 4320, 2.0)
        self.assertLessEqual(max(w, h), D.MAX_RENDER_SIDE)

    def test_aspect_ratio_is_reduced_and_kept_small(self):
        self.assertEqual(D.aspect_ratio(1920, 1080), (16, 9))
        self.assertEqual(D.aspect_ratio(1280, 800), (8, 5))
        self.assertLessEqual(max(D.aspect_ratio(1366, 768)), 1000)

    def test_view_flags(self):
        self.assertFalse(D.make_view(1920, 1080, 1.0)["ultrawide"])
        self.assertTrue(D.make_view(3440, 1440, 1.0)["ultrawide"])
        self.assertTrue(D.make_view(1000, 1000, 1.0)["square"])
        self.assertFalse(D.make_view(1280, 800, 1.0)["square"])

    def test_scale_labels(self):
        self.assertEqual(D.scale_label(1.0), "100% (window size)")
        self.assertEqual(D.scale_label(1.5), "150% (sharper)")
        self.assertEqual(D.scale_label(0.5), "50% (faster)")
        self.assertEqual(D.nearest_scale("0.8"), 0.75)
        self.assertEqual(D.nearest_scale("junk"), 1.0)


class Pack(unittest.TestCase):
    def test_rules_get_the_two_presets(self):
        rules = D.build_rules(STOCK, D.make_view(1920, 1080, 1.0))
        self.assertIn("name = Graphics (online display)", rules)
        self.assertIn('path = "The Legend of Zelda: Breath of the Wild/Online Display"', rules)
        self.assertIn("name = Online Display\ncategory = Aspect Ratio\n$aspectRatioWidth = 16\n$aspectRatioHeight = 9", rules)
        self.assertIn("$showSquareHUDOption:int = 0\n$ultrawideHUDMode:int = 0", rules)
        self.assertTrue(rules.endswith("name = Online Resolution\ncategory = Resolution\n$width = 1920\n$height = 1080\n"))

    def test_crlf_packs_keep_their_line_ends(self):
        rules = D.build_rules(STOCK.replace("\n", "\r\n"), D.make_view(1920, 1080, 1.0))
        self.assertNotIn("\n[", rules.replace("\r\n[", ""))

    def test_an_unexpected_pack_is_refused(self):
        with self.assertRaises(ValueError):
            D.build_rules("[Definition]\nname = Other\n", D.make_view(1920, 1080, 1.0))

    def test_presets_labels_and_picks(self):
        shadows = D.parse_presets(STOCK, "Shadows")
        self.assertEqual([n for n, _ in shadows], ["Low (Low Draw Distance, Sharp Shadows)", "Medium (100%, Default)", "High (200%)"])
        self.assertEqual(D.label("Low (Low Draw Distance, Sharp Shadows)"), "Low")
        self.assertEqual(D.label("Medium (100%, Default)"), "Medium (100%, Default)")
        self.assertEqual(D.pick_preset(shadows, "High"), "High (200%)")
        self.assertEqual(D.pick_preset(shadows, "Missing"), "Medium (100%, Default)")
        self.assertIsNone(D.pick_preset([], "High"))
        self.assertEqual(D.choose(shadows, "Low (Low Draw Distance, Sharp Shadows)", "Shadows"), "Low (Low Draw Distance, Sharp Shadows)")
        self.assertEqual(D.choose(shadows, "gone", "Shadows"), "High (200%)")


class Fps(unittest.TestCase):
    limits = [(30, "a"), (60, "b"), (90, "c"), (120, "d"), (144, "e")]

    def test_never_above_the_display(self):
        self.assertEqual([f for f, _ in D.available_fps(self.limits, 60)], [30, 60])
        self.assertEqual([f for f, _ in D.available_fps(self.limits, 120)], [30, 60, 90, 120])
        self.assertEqual([f for f, _ in D.available_fps(self.limits, 0)], [30, 60])
        self.assertEqual(D.available_fps([(20, "x"), (45, "y")], 30), [(45, "y")])

    def test_best_divides_the_refresh_rate(self):
        self.assertEqual(D.best_fps(D.available_fps(self.limits, 120), 120)[0], 120)
        self.assertEqual(D.best_fps(D.available_fps(self.limits, 90), 90)[0], 90)
        self.assertEqual(D.best_fps(D.available_fps(self.limits, 100), 100)[0], 90)

    def test_pick(self):
        options = D.available_fps(self.limits, 120)
        self.assertEqual(D.pick_fps(options, D.AUTO_FPS, 120)[0], 120)
        self.assertEqual(D.pick_fps(options, 75, 120)[0], 60)
        self.assertEqual(D.pick_fps(options, 10, 120)[0], 30)
        self.assertIsNone(D.pick_fps([], 0, 120))


class Hardware(unittest.TestCase):
    def test_gpu_classes(self):
        self.assertEqual(D.gpu_tier("NVIDIA GeForce RTX 4080"), 3)
        self.assertEqual(D.gpu_tier("NVIDIA GeForce RTX 4080 Laptop GPU"), 2)
        self.assertEqual(D.gpu_tier("AMD Radeon RX 6600"), 2)
        self.assertEqual(D.gpu_tier("Intel UHD Graphics 620"), 0)
        self.assertEqual(D.gpu_tier("AMD/ATI VanGogh [AMD Custom GPU 0405]"), 1)
        self.assertEqual(D.gpu_tier("AMD Radeon 780M"), 1)
        self.assertEqual(D.gpu_tier(""), 1)

    def test_little_memory_lowers_the_class(self):
        self.assertEqual(D.hardware_tier("NVIDIA GeForce RTX 4080", 0, 16, 32), 3)
        self.assertEqual(D.hardware_tier("NVIDIA GeForce RTX 4080", 0, 8, 9.8), 1)
        self.assertEqual(D.hardware_tier("NVIDIA GeForce RTX 4080", 1024, 16, 32), 0)

    def test_recommended_scale_by_class(self):
        self.assertEqual(D.recommended_scale(0, 1920, 1080), 0.5)
        self.assertEqual(D.recommended_scale(1, 1920, 1080), 1.0)
        self.assertEqual(D.recommended_scale(1, 2560, 1440), 0.75)
        self.assertEqual(D.recommended_scale(3, 1920, 1080), 1.0)

    def test_recommended_presets_come_from_the_pack(self):
        with tempfile.TemporaryDirectory() as root:
            install(root)
            got = D.recommended(os.path.join(root, "cemu"), 0, (1920, 1080, 120))
        self.assertEqual((got["shadows"], got["upscale"], got["scale"]), ("Low (Low Draw Distance, Sharp Shadows)", 0, 0.5))
        self.assertEqual(got["aa"], "Normal FXAA (Default)")


class Prefs(unittest.TestCase):
    def test_defaults(self):
        self.assertEqual(D.read_prefs({}), D.DEFAULTS)

    def test_saved_values_are_cleaned(self):
        got = D.read_prefs({"gfx_scale": "1.4", "gfx_fps": "-3", "gfx_api": "Metal", "gfx_upscale": "9", "gfx_volume": "60", "gfx_vsync": "1", "gfx_async": "x",
                            "gfx_shadows": "High (200%)"})
        self.assertEqual((got["scale"], got["fps"], got["api"], got["upscale"], got["volume"]), (1.5, 0, "Auto", 1, 50))
        self.assertTrue(got["vsync"])
        self.assertTrue(got["async"])
        self.assertEqual(got["shadows"], "High (200%)")


class Apply(unittest.TestCase):
    def run_apply(self, own, screen=(1920, 1080, 120), fps=True):
        root = tempfile.mkdtemp()
        self.addCleanup(lambda: __import__("shutil").rmtree(root, True))
        install(root, fps)
        conf = os.path.join(root, "launcher.conf")
        with open(conf, "w") as f:
            f.write("".join(f"{k}={v}\n" for k, v in own.items()))
        summary = D.apply(root, conf, screen, vulkan=True)
        return root, summary, ET.parse(os.path.join(root, "cemu", "settings.xml")).getroot()

    def test_writes_pack_and_settings(self):
        root, summary, content = self.run_apply({"gfx_scale": "0.75", "gfx_showfps": "0"})
        self.assertEqual(summary, "Display 1920x1080 at 120 Hz: rendering 1440x810 at 120 FPS")
        pack = D.read_file(os.path.join(root, "cemu", D.PACK))
        self.assertIn("$width = 1440\n$height = 810", pack)
        self.assertTrue(os.path.isfile(os.path.join(root, "cemu", os.path.dirname(D.PACK), "patch.asm")))
        packs = content.find("GraphicPack")
        files = [e.get("filename") for e in packs.findall("Entry")]
        self.assertNotIn("graphicPacks\\downloadedGraphicPacks\\BreathOfTheWild\\Graphics\\rules.txt", files)
        ours = packs.findall("Entry")[0]
        got = {p.findtext("category"): p.findtext("preset") for p in ours.findall("Preset")}
        self.assertEqual(got["Aspect Ratio"], "Online Display")
        self.assertEqual(got["Resolution"], "Online Resolution")
        self.assertEqual(got["Extra"], "Kept")
        self.assertNotIn("Square HUD Mode", got)
        self.assertEqual(got["Shadows"], "High (200%)")
        fps = {p.findtext("category"): p.findtext("preset") for p in packs.findall("Entry")[1].findall("Preset")}
        self.assertEqual(fps, {"Mode": "Normal Settings", "FPS Limit": "120FPS Limit"})
        self.assertEqual(content.findtext("fullscreen"), "true")
        self.assertEqual(content.findtext("console_language"), "1")
        self.assertEqual(content.findtext("Graphic/FullscreenScaling"), "1")
        self.assertEqual(content.findtext("Graphic/api"), "1")
        self.assertEqual(content.findtext("Graphic/Overlay/FPS"), "false")
        self.assertEqual(content.findtext("Audio/TVVolume"), "100")

    def test_a_square_screen_turns_the_square_hud_on(self):
        _, _, content = self.run_apply({}, screen=(1000, 1000, 60))
        ours = content.find("GraphicPack").findall("Entry")[0]
        self.assertIn("Square HUD Mode", [p.findtext("category") for p in ours.findall("Preset")])

    def test_unknown_refresh_rate_assumes_60(self):
        self.assertIn("at 60 FPS", self.run_apply({}, screen=(1280, 800, 0))[1])

    def test_without_fps_pack_or_screen_the_session_still_gets_a_picture(self):
        _, summary, _ = self.run_apply({}, screen=None, fps=False)
        self.assertEqual(summary, "Display 1920x1080: rendering 1920x1080")

    def test_apply_twice_is_stable(self):
        root, _, _ = self.run_apply({})
        before = D.read_file(os.path.join(root, "cemu", "settings.xml"))
        D.apply(root, os.path.join(root, "launcher.conf"), (1920, 1080, 120), vulkan=True)
        self.assertEqual(D.read_file(os.path.join(root, "cemu", "settings.xml")), before)

    def test_missing_pack_or_settings_raise(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(FileNotFoundError):
                D.apply(root, os.path.join(root, "c"), (1920, 1080, 60))


if __name__ == "__main__":
    unittest.main()
