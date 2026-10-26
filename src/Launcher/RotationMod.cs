using System;
using System.IO;
using System.Text;

namespace HyruleOnline {
 // Turns one player's picture by a quarter turn. ReShade is loaded as a Vulkan layer by that player's Cemu only,
 // with a single shader that resamples the finished frame. A player who is not turned gets no ReShade at all:
 // the files are removed here and Start-Online.ps1 only points the Vulkan loader at an instance that still has them.
 static class RotationMod {
  public const string Marker = "; Written by the Hyrule Online launcher";
  public const string ConfigFile = "ReShade.ini", Folder = "hyrule-rotation", ShaderFile = "HyruleRotate.fx", PresetFile = "HyruleRotate.ini";
  public static readonly string[] LayerFiles = { "ReShade64.dll", "ReShade64.json" };
  static readonly string[] Leftovers = { "ReShade64.log", "ReShade.log" };

  // Same fullscreen triangle and back buffer sampler as ReShade.fxh, so the effect needs no other file.
  const string Shader = "// Written by the Hyrule Online launcher: turns the finished frame by a quarter turn, clockwise.\r\n"
   + "uniform int HyruleRotation < hidden = true; > = 0;\r\n\r\n"
   + "texture BackBufferTex : COLOR;\r\nsampler BackBuffer { Texture = BackBufferTex; };\r\n\r\n"
   + "void PostProcessVS(in uint id : SV_VertexID, out float4 position : SV_Position, out float2 texcoord : TEXCOORD)\r\n{\r\n"
   + "\ttexcoord.x = (id == 2) ? 2.0 : 0.0;\r\n\ttexcoord.y = (id == 1) ? 2.0 : 0.0;\r\n"
   + "\tposition = float4(texcoord * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);\r\n}\r\n\r\n"
   + "float4 PS_Rotate(float4 position : SV_Position, float2 texcoord : TEXCOORD) : SV_Target\r\n{\r\n"
   + "\tfloat2 uv = texcoord;\r\n"
   + "\tif (HyruleRotation == 1) uv = float2(texcoord.y, 1.0 - texcoord.x);\r\n"
   + "\telse if (HyruleRotation == 2) uv = 1.0 - texcoord;\r\n"
   + "\telse if (HyruleRotation == 3) uv = float2(1.0 - texcoord.y, texcoord.x);\r\n"
   + "\treturn tex2D(BackBuffer, uv);\r\n}\r\n\r\n"
   + "technique HyruleRotate\r\n{\r\n\tpass\r\n\t{\r\n\t\tVertexShader = PostProcessVS;\r\n\t\tPixelShader = PS_Rotate;\r\n\t}\r\n}\r\n";

  public static string ShaderText { get { return Shader; } }

  // The overlay key is unbound and the tutorial is marked done, so nothing ever opens on top of the game.
  public static string ConfigText() {
   return Marker + "\r\n[GENERAL]\r\nEffectSearchPaths=.\\" + Folder + "\r\nTextureSearchPaths=.\\" + Folder + "\r\nIntermediateCachePath=.\\" + Folder + "\\cache\r\n"
    + "PresetPath=.\\" + Folder + "\\" + PresetFile + "\r\nStartupPresetPath=.\\" + Folder + "\\" + PresetFile + "\r\nNoDebugInfo=1\r\nPerformanceMode=0\r\n\r\n"
    + "[INPUT]\r\nKeyOverlay=0,0,0,0\r\nKeyEffects=0,0,0,0\r\nKeyReload=0,0,0,0\r\nKeyScreenshot=0,0,0,0\r\nGamepadNavigation=0\r\n\r\n"
    + "[OVERLAY]\r\nShowClock=0\r\nShowFPS=0\r\nShowFrameTime=0\r\nShowPresetName=0\r\nShowPresetTransitionMessage=0\r\nShowScreenshotMessage=0\r\nTutorialProgress=4\r\n";
  }

  public static string PresetText(int degrees) {
   return Marker + "\r\nTechniques=HyruleRotate@" + ShaderFile + "\r\nTechniqueSorting=HyruleRotate@" + ShaderFile + "\r\n\r\n[" + ShaderFile + "]\r\nHyruleRotation=" + ScreenLayout.CleanRotation(degrees) / 90 + "\r\n";
  }

  // ReShade rewrites its ini on exit and drops comments, so ownership is the preset path only this class writes.
  public static bool Managed(string instance) {
   string config = Path.Combine(instance, ConfigFile);
   return File.Exists(config) && File.ReadAllText(config).Contains("StartupPresetPath=.\\" + Folder + "\\" + PresetFile);
  }

  public static bool LayerReady(string source) {
   foreach (var name in LayerFiles) if (!File.Exists(Path.Combine(source, name))) return false;
   return true;
  }

  // Sets one instance up for the turn, or removes every file this class wrote when the player is not turned.
  // The ReShade files come from `source` (a local folder; ReShade is never part of the repository).
  public static void Apply(string instance, int degrees, string source) {
   degrees = ScreenLayout.CleanRotation(degrees);
   if (degrees == 0) { Remove(instance); return; }
   if (File.Exists(Path.Combine(instance, ConfigFile)) && !Managed(instance)) throw new InvalidDataException(Lang.T("Cemu already has its own ReShade setup in {0}.", instance));
   if (!LayerReady(source)) throw new FileNotFoundException(Lang.T("ReShade64.dll and ReShade64.json are missing from {0}.", source));
   foreach (var name in LayerFiles) File.Copy(Path.Combine(source, name), Path.Combine(instance, name), true);
   string folder = Path.Combine(instance, Folder);
   Directory.CreateDirectory(folder);
   var utf8 = new UTF8Encoding(false);
   File.WriteAllText(Path.Combine(folder, ShaderFile), Shader, utf8);
   File.WriteAllText(Path.Combine(folder, PresetFile), PresetText(degrees), utf8);
   File.WriteAllText(Path.Combine(instance, ConfigFile), ConfigText(), utf8);
  }

  public static void Remove(string instance) {
   if (!Managed(instance)) return;
   File.Delete(Path.Combine(instance, ConfigFile));
   foreach (var name in LayerFiles) File.Delete(Path.Combine(instance, name));
   foreach (var name in Leftovers) File.Delete(Path.Combine(instance, name));
   string folder = Path.Combine(instance, Folder);
   if (Directory.Exists(folder)) Directory.Delete(folder, true);
  }
 }
}
