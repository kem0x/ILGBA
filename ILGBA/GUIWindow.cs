using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace GUI
{
    class Window
    {
        private static Sdl2Window? _window;
        private static GraphicsDevice? _gd;
        private static CommandList? _cl;
        private static ImGuiController? _controller;

        private static Vector3 _clearColor = new Vector3(0.45f, 0.55f, 0.6f);

        public static int Width => _window?.Width ?? 0;
        public static int Height => _window?.Height ?? 0;

        public static Action OnTick = () => { };

        static public void Create()
        {
            // Create window, GraphicsDevice, and all resources necessary for the demo.
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "ImGui.NET Sample Program"),
                new GraphicsDeviceOptions(true, null, true, ResourceBindingModel.Improved, true, true),
                out _window,
                out _gd);

            _controller = new ImGuiController(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription, _window.Width, _window.Height);

            _window.Resized += static () =>
            {
                _gd.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _controller.WindowResized(_window.Width, _window.Height);

                //Width = _window.Width;
                //Height = _window.Height;
            };
            _cl = _gd.ResourceFactory.CreateCommandList();

            var stopwatch = Stopwatch.StartNew();
            float deltaTime = 0f;
            // Main application loop
            while (_window.Exists)
            {
                deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
                stopwatch.Restart();
                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) { break; }
                _controller.Update(deltaTime, snapshot); // Feed the input events to our ImGui controller, which passes them through to ImGui.

                OnTick();

                _cl.Begin();
                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, new RgbaFloat(_clearColor.X, _clearColor.Y, _clearColor.Z, 1f));
                _controller.Render(_gd, _cl);
                _cl.End();
                _gd.SubmitCommands(_cl);
                _gd.SwapBuffers(_gd.MainSwapchain);
            }

            // Clean up Veldrid resources
            _gd.WaitForIdle();
            _controller.Dispose();
            _cl.Dispose();
            _gd.Dispose();
        }
    }
}