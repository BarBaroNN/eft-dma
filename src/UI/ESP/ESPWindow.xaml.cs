using System;
using System.Collections.Generic;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld;
using LoneEftDmaRadar.Tarkov.GameWorld.Exits;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LoneEftDmaRadar.UI.Skia;

namespace LoneEftDmaRadar.UI.ESP
{
    public partial class ESPWindow : Window
    {
        #region Fields/Properties

        public static bool ShowESP { get; set; } = true;

        private readonly System.Diagnostics.Stopwatch _fpsSw = new();
        private int _fpsCounter;
        private int _fps;
        private readonly System.Diagnostics.Stopwatch _frameTimeSw = new();
        private double _lastFrameTime;
        
        // Cached Fonts/Paints
        private readonly SKFont _textFont;
        private readonly SKPaint _textPaint;
        private readonly SKPaint _textBackgroundPaint;
        private readonly SKPaint _skeletonPaint;
        private readonly SKPaint _boxPaint;
        private readonly SKPaint _lootPaint;
        private readonly SKFont _lootTextFont;
        private readonly SKPaint _lootTextPaint;
        private readonly SKPaint _crosshairPaint;

        private Vector3 _camPos;
        private bool _isFullscreen;
        private readonly CameraManager _cameraManager = new();

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<AbstractPlayer> AllPlayers => Memory.Players;

        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        private static bool InRaid => Memory.InRaid;

        // Bone Connections for Skeleton
        private static readonly (Bones From, Bones To)[] _boneConnections = new[]
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),
            
            // Left Arm
            (Bones.HumanNeck, Bones.HumanLUpperarm), // Shoulder approx
            (Bones.HumanLUpperarm, Bones.HumanLForearm1),
            (Bones.HumanLForearm1, Bones.HumanLForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),
            
            // Right Arm
            (Bones.HumanNeck, Bones.HumanRUpperarm), // Shoulder approx
            (Bones.HumanRUpperarm, Bones.HumanRForearm1),
            (Bones.HumanRForearm1, Bones.HumanRForearm2),
            (Bones.HumanRForearm2, Bones.HumanRPalm),
            
            // Left Leg
            (Bones.HumanPelvis, Bones.HumanLThigh1),
            (Bones.HumanLThigh1, Bones.HumanLThigh2),
            (Bones.HumanLThigh2, Bones.HumanLCalf),
            (Bones.HumanLCalf, Bones.HumanLFoot),
            
            // Right Leg
            (Bones.HumanPelvis, Bones.HumanRThigh1),
            (Bones.HumanRThigh1, Bones.HumanRThigh2),
            (Bones.HumanRThigh2, Bones.HumanRCalf),
            (Bones.HumanRCalf, Bones.HumanRFoot),
        };

        #endregion

        public ESPWindow()
        {
            InitializeComponent();
            CameraManager.TryInitialize();
            
            // Initial sizes
            this.Width = 400;
            this.Height = 300;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Cache paints/fonts
            _textFont = new SKFont
            {
                Size = 12,
                Edging = SKFontEdging.Antialias
            };

            _textPaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill
            };

            _textBackgroundPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 128),
                Style = SKPaintStyle.Fill
            };

            _skeletonPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _boxPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.0f,
                IsAntialias = false, // Crisper boxes
                Style = SKPaintStyle.Stroke
            };

            _lootPaint = new SKPaint
            {
                Color = SKColors.LightGray,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            _lootTextFont = new SKFont
            {
                Size = 10,
                Edging = SKFontEdging.Antialias
            };

             _lootTextPaint = new SKPaint
            {
                Color = SKColors.Silver,
                Style = SKPaintStyle.Fill
            };

            _crosshairPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _fpsSw.Start();
            _frameTimeSw.Start();
            
            // Use CompositionTarget.Rendering for smoother 60fps (or refresh rate) loop
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            int maxFPS = App.Config.UI.EspMaxFPS;
            if (maxFPS > 0)
            {
                double targetFrameTime = 1000.0 / maxFPS;
                double elapsed = _frameTimeSw.Elapsed.TotalMilliseconds - _lastFrameTime;
                
                if (elapsed < targetFrameTime)
                    return;
                
                _lastFrameTime = _frameTimeSw.Elapsed.TotalMilliseconds;
            }
            
            RefreshESP();
        }

        #region Rendering Methods

        /// <summary>
        /// Record the Rendering FPS.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SetFPS()
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _fps = System.Threading.Interlocked.Exchange(ref _fpsCounter, 0);
                _fpsSw.Restart();
            }
            else
            {
                _fpsCounter++;
            }
        }

        /// <summary>
        /// Main ESP Render Event.
        /// </summary>
        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            SetFPS();
            
            // Clear with black background (transparent for fuser)
            canvas.Clear(SKColors.Black);
            
            try
            {
                if (!InRaid)
                    return;

                var localPlayer = LocalPlayer;
                var allPlayers = AllPlayers;
                
                if (localPlayer is not null && allPlayers is not null)
                {
                    if (!ShowESP)
                    {
                        DrawNotShown(canvas, e.Info.Width, e.Info.Height);
                    }
                    else
                    {
                        _cameraManager.Update(localPlayer);
                        UpdateCameraPositionFromMatrix();

                        ApplyResolutionOverrideIfNeeded();

                        // Render Loot (background layer)
                        if (App.Config.Loot.Enabled && App.Config.UI.EspLoot)
                        {
                            DrawLoot(canvas, e.Info.Width, e.Info.Height);
                        }

                        // Render Exfils
                        if (Exits is not null && App.Config.UI.EspExfils)
                        {
                            foreach (var exit in Exits)
                            {
                                if (exit is Exfil exfil && exfil.Status != Exfil.EStatus.Closed)
                                {
                                     if (WorldToScreen2(exfil.Position, out var screen, e.Info.Width, e.Info.Height))
                                     {
                                         var paint = exfil.Status switch
                                         {
                                             Exfil.EStatus.Open => SKPaints.PaintExfilOpen,
                                             Exfil.EStatus.Pending => SKPaints.PaintExfilPending,
                                             _ => SKPaints.PaintExfilOpen
                                         };
                                         
                                         canvas.DrawCircle(screen, 4f, paint);
                                         canvas.DrawText(exfil.Name, screen.X + 6, screen.Y + 4, _textFont, SKPaints.TextExfil);
                                     }
                                }
                            }
                        }

                        // Render players
                        foreach (var player in allPlayers)
                        {
                            DrawPlayerESP(canvas, player, localPlayer, e.Info.Width, e.Info.Height);
                        }

                        if (App.Config.UI.EspCrosshair)
                        {
                            DrawCrosshair(canvas, e.Info.Width, e.Info.Height);
                        }

                        DrawFPS(canvas, e.Info.Width, e.Info.Height);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ESP RENDER ERROR: {ex}");
            }
        }

        private void DrawLoot(SKCanvas canvas, float screenWidth, float screenHeight)
        {
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            var viewMatrix = _cameraManager.ViewMatrix;
            var forward = new Vector3(viewMatrix.M13, viewMatrix.M23, viewMatrix.M33);

            foreach (var item in lootItems)
            {
                if (WorldToScreen2(item.Position, out var screen, screenWidth, screenHeight))
                {
                     var dirToItem = Vector3.Normalize(item.Position - _camPos);
                     var dot = Vector3.Dot(forward, dirToItem);
                     dot = Math.Clamp(dot, -1f, 1f);
                     var angle = MathF.Acos(dot) * (180f / MathF.PI);
                     
                     bool coneEnabled = App.Config.UI.EspLootConeEnabled && App.Config.UI.EspLootConeAngle > 0f;
                     bool inCone = !coneEnabled || angle <= App.Config.UI.EspLootConeAngle;

                     canvas.DrawCircle(screen, 2f, _lootPaint);
                     
                     if (item.Important || inCone)
                     {
                         var text = item.ShortName;
                         if (App.Config.UI.EspLootPrice)
                         {
                             text = item.Important ? item.ShortName : $"{item.ShortName} ({Utilities.FormatNumberKM(item.Price)})";
                         }
                         canvas.DrawText(text, screen.X + 4, screen.Y + 4, _lootTextFont, _lootTextPaint);
                     }
                }
            }
        }

        /// <summary>
        /// Renders player on ESP
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerESP(SKCanvas canvas, AbstractPlayer player, LocalPlayer localPlayer, float screenWidth, float screenHeight)
        {
            if (player is null || player == localPlayer || !player.IsAlive || !player.IsActive)
                return;

            // Optimization: Skip players that are too far before W2S
            float distance = Vector3.Distance(localPlayer.Position, player.Position);
            if (distance > App.Config.UI.MaxDistance) // Max render distance
                return;

            // Get Color
            var color = GetPlayerColor(player).Color;
            _skeletonPaint.Color = color;
            _boxPaint.Color = color;
            _textPaint.Color = color;

            bool isAI = player.Type is PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss or PlayerType.PScav; // treat PScav as AI for now or separate? Config says "AI"
            bool drawSkeleton = isAI ? App.Config.UI.EspAISkeletons : App.Config.UI.EspPlayerSkeletons;
            bool drawBox = isAI ? App.Config.UI.EspAIBoxes : App.Config.UI.EspPlayerBoxes;
            bool drawName = isAI ? App.Config.UI.EspAINames : App.Config.UI.EspPlayerNames;

            // Draw Skeleton
            if (drawSkeleton)
            {
                DrawSkeleton(canvas, player, screenWidth, screenHeight);
            }
            
            // Draw Box
            if (drawBox)
            {
                DrawBoundingBox(canvas, player, screenWidth, screenHeight);
            }

            if (drawName && TryProject(player.GetBonePos(Bones.HumanHead), screenWidth, screenHeight, out var headScreen))
            {
                DrawPlayerName(canvas, headScreen, player, distance);
            }
        }

        private void DrawSkeleton(SKCanvas canvas, AbstractPlayer player, float w, float h)
        {
            foreach (var (from, to) in _boneConnections)
            {
                var p1 = player.GetBonePos(from);
                var p2 = player.GetBonePos(to);

                if (TryProject(p1, w, h, out var s1) && TryProject(p2, w, h, out var s2))
                {
                    canvas.DrawLine(s1, s2, _skeletonPaint);
                }
            }
        }

        private void DrawBoundingBox(SKCanvas canvas, AbstractPlayer player, float w, float h)
        {
            var projectedPoints = new List<SKPoint>();

            foreach (var boneKvp in player.PlayerBones)
            {
                if (TryProject(boneKvp.Value.Position, w, h, out var s))
                    projectedPoints.Add(s);
            }

            if (projectedPoints.Count < 2)
                return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var point in projectedPoints)
            {
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;

            if (boxWidth < 1f || boxHeight < 1f || boxWidth > w * 2f || boxHeight > h * 2f)
                return;

            minX = Math.Clamp(minX, -50f, w + 50f);
            maxX = Math.Clamp(maxX, -50f, w + 50f);
            minY = Math.Clamp(minY, -50f, h + 50f);
            maxY = Math.Clamp(maxY, -50f, h + 50f);

            float padding = 2f;
            var rect = new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
            canvas.DrawRect(rect, _boxPaint);
        }

        /// <summary>
        /// Determines player color based on type
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerColor(AbstractPlayer player)
        {
             if (player.IsFocused)
                return SKPaints.PaintAimviewWidgetFocused;
            if (player is LocalPlayer)
                return SKPaints.PaintAimviewWidgetLocalPlayer;

            return player.Type switch
            {
                PlayerType.Teammate => SKPaints.PaintAimviewWidgetTeammate,
                PlayerType.PMC => SKPaints.PaintAimviewWidgetPMC,
                PlayerType.AIScav => SKPaints.PaintAimviewWidgetScav,
                PlayerType.AIRaider => SKPaints.PaintAimviewWidgetRaider,
                PlayerType.AIBoss => SKPaints.PaintAimviewWidgetBoss,
                PlayerType.PScav => SKPaints.PaintAimviewWidgetPScav,
                PlayerType.SpecialPlayer => SKPaints.PaintAimviewWidgetWatchlist,
                PlayerType.Streamer => SKPaints.PaintAimviewWidgetStreamer,
                _ => SKPaints.PaintAimviewWidgetPMC
            };
        }

        /// <summary>
        /// Draws player name and distance
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerName(SKCanvas canvas, SKPoint screenPos, AbstractPlayer player, float distance)
        {
            var name = player.Name ?? "Unknown";
            var text = $"{name} ({distance:F0}m)";
            
            var textWidth = _textFont.MeasureText(text);
            var textHeight = _textFont.Size;
            
            canvas.DrawText(text, screenPos.X - textWidth / 2, screenPos.Y - 20 + textHeight, _textFont, _textPaint);
        }

        /// <summary>
        /// Draw 'ESP Hidden' notification.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(SKCanvas canvas, float width, float height)
        {
            using var textFont = new SKFont
            {
                Size = 24,
                Edging = SKFontEdging.Antialias
            };

            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            var text = "ESP Hidden";
            var x = width / 2;
            var y = height / 2;
            
            canvas.DrawText(text, x, y, SKTextAlign.Center, textFont, textPaint);
        }

        private void DrawCrosshair(SKCanvas canvas, float width, float height)
        {
            float centerX = width / 2f;
            float centerY = height / 2f;
            float length = MathF.Max(2f, App.Config.UI.EspCrosshairLength);

            canvas.DrawLine(centerX - length, centerY, centerX + length, centerY, _crosshairPaint);
            canvas.DrawLine(centerX, centerY - length, centerX, centerY + length, _crosshairPaint);
        }

        private void DrawFPS(SKCanvas canvas, float width, float height)
        {
            var fpsText = $"FPS: {_fps}";
   
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                TextSize = 8,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };
            
            canvas.DrawText(fpsText, 10, 25, paint);
        }

        #endregion

        #region WorldToScreen Conversion

        private void UpdateCameraPositionFromMatrix()
        {
            var viewMatrix = _cameraManager.ViewMatrix;
            _camPos = new Vector3(viewMatrix.M14, viewMatrix.M24, viewMatrix.M34);
        }

        private bool WorldToScreen2(in Vector3 world, out SKPoint scr, float screenWidth, float screenHeight)
        {
            scr = default;

            var viewMatrix = _cameraManager.ViewMatrix;
            
            var worldPos = new Vector4(world.X, world.Y, world.Z, 1f);
            var clipCoords = Vector4.Transform(worldPos, viewMatrix);

            if (clipCoords.W < 0.1f)
                return false;

            var ndc = new Vector3(
                clipCoords.X / clipCoords.W,
                clipCoords.Y / clipCoords.W,
                clipCoords.Z / clipCoords.W
            );

            if (ndc.Z < 0f || ndc.Z > 1f)
                return false;

            scr.X = (ndc.X + 1f) * 0.5f * screenWidth;
            scr.Y = (1f - ndc.Y) * 0.5f * screenHeight;

            return true;
        }

        private bool TryProject(in Vector3 world, float w, float h, out SKPoint screen)
        {
            screen = default;
            if (world == Vector3.Zero)
                return false;
            if (!WorldToScreen2(world, out screen, w, h))
                return false;
            if (float.IsNaN(screen.X) || float.IsInfinity(screen.X) ||
                float.IsNaN(screen.Y) || float.IsInfinity(screen.Y))
                return false;

            const float margin = 200f; 
            if (screen.X < -margin || screen.X > w + margin ||
                screen.Y < -margin || screen.Y > h + margin)
                return false;

            return true;
        }

        #endregion

        #region Window Management

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            skElement.PaintSurface -= OnPaintSurface;
            _textPaint.Dispose();
            _textBackgroundPaint.Dispose();
            _crosshairPaint.Dispose();
            base.OnClosed(e);
        }

        // Method to force refresh
        public void RefreshESP()
        {
            skElement.InvalidateVisual();
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleFullscreen();
        }

        // Handler for keys (ESC to exit fullscreen)
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && this.WindowState == WindowState.Maximized)
            {
                ToggleFullscreen();
            }
        }

        // Simple fullscreen toggle
        public void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.Topmost = false;
                this.ResizeMode = ResizeMode.CanResize;
                this.Width = 400;
                this.Height = 300;
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _isFullscreen = false;
            }
            else
            {
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.NoResize;
                this.Topmost = true;
                this.WindowState = WindowState.Normal;
                var (width, height) = GetConfiguredResolution();
                this.Left = 0;
                this.Top = 0;
                this.Width = width;
                this.Height = height;
                _isFullscreen = true;
            }
            
            this.RefreshESP();
        }

        public void ApplyResolutionOverride()
        {
            if (!_isFullscreen)
                return;

            var (width, height) = GetConfiguredResolution();
            this.Left = 0;
            this.Top = 0;
            this.Width = width;
            this.Height = height;
            this.RefreshESP();
        }

        private (double width, double height) GetConfiguredResolution()
        {
            double width = App.Config.UI.EspScreenWidth > 0
                ? App.Config.UI.EspScreenWidth
                : SystemParameters.PrimaryScreenWidth;
            double height = App.Config.UI.EspScreenHeight > 0
                ? App.Config.UI.EspScreenHeight
                : SystemParameters.PrimaryScreenHeight;
            return (width, height);
        }

        private void ApplyResolutionOverrideIfNeeded()
        {
            if (!_isFullscreen)
                return;

            if (App.Config.UI.EspScreenWidth <= 0 && App.Config.UI.EspScreenHeight <= 0)
                return;

            var target = GetConfiguredResolution();
            if (Math.Abs(Width - target.width) > 0.5 || Math.Abs(Height - target.height) > 0.5)
            {
                Width = target.width;
                Height = target.height;
                Left = 0;
                Top = 0;
            }
        }

        #endregion
    }
}