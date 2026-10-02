using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace AgentCapture;

internal static class WgcCapture
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess { nint GetInterface(in Guid iid); }
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint graphicsDevice);

    public static CapturedPixels Capture(nint hwnd, int timeoutMs)
    {
        Windows.Validate(Windows.Read(hwnd), "wgc");
        if (!GraphicsCaptureSession.IsSupported()) throw new CaptureError("wgc_not_supported", "WGC is unavailable in this system/session.");
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        nint itemPointer = interop.CreateForWindow(hwnd, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"));
        GraphicsCaptureItem item;
        try { item = GraphicsCaptureItem.FromAbi(itemPointer); }
        finally { Marshal.Release(itemPointer); }
        if (item.Size.Width <= 0 || item.Size.Height <= 0 || (long)item.Size.Width * item.Size.Height > 40_000_000)
            throw new CaptureError("invalid_dimensions", "WGC surface dimensions are invalid.");
        using var device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        using var context = device.ImmediateContext;
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out nint winrtPointer));
        IDirect3DDevice winrtDevice;
        try { winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtPointer); }
        finally { Marshal.Release(winrtPointer); }
        using (winrtDevice)
        using (var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size))
        using (var session = pool.CreateCaptureSession(item))
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new object();
            Direct3D11CaptureFrame? latest = null;
            bool stopped = false;
            void Arrived(Direct3D11CaptureFramePool sender, object args)
            {
                try
                {
                    var frame = sender.TryGetNextFrame();
                    if (frame is not null)
                    {
                        lock (gate)
                        {
                            if (stopped) frame.Dispose();
                            else { latest?.Dispose(); latest = frame; completion.TrySetResult(); }
                        }
                    }
                }
                catch (Exception error) { completion.TrySetException(error); }
            }
            pool.FrameArrived += Arrived;
            session.IsCursorCaptureEnabled = false;
            try
            {
                session.StartCapture();
                completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).GetAwaiter().GetResult();
                // Starting capture can initially expose an older compositor surface. Briefly retain the latest frame.
                Thread.Sleep(100);
                Direct3D11CaptureFrame selected;
                lock (gate) { stopped = true; selected = latest!; latest = null; }
                using var frame = selected;
                int width = frame.ContentSize.Width, height = frame.ContentSize.Height;
                if (width <= 0 || height <= 0 || (long)width * height > 40_000_000)
                    throw new CaptureError("invalid_frame", "WGC supplied an invalid content size.");
                var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
                using var texture = new ID3D11Texture2D(access.GetInterface(typeof(ID3D11Texture2D).GUID));
                var desc = texture.Description;
                if (width > desc.Width || height > desc.Height)
                    throw new CaptureError("window_resized", "The window resized during capture. Retry without changing its state.");
                desc.Usage = ResourceUsage.Staging;
                desc.BindFlags = BindFlags.None;
                desc.CPUAccessFlags = CpuAccessFlags.Read;
                desc.MiscFlags = ResourceOptionFlags.None;
                using var staging = device.CreateTexture2D(desc);
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var bytes = new byte[checked(width * height * 4)];
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(mapped.DataPointer + checked(y * (int)mapped.RowPitch), bytes, y * width * 4, width * 4);
                    return new(width, height, bytes);
                }
                finally { context.Unmap(staging, 0); }
            }
            catch (TimeoutException) { throw new CaptureError("timeout", "WGC did not produce a frame before the deadline.", 4); }
            finally
            {
                pool.FrameArrived -= Arrived;
                lock (gate) { stopped = true; latest?.Dispose(); latest = null; }
                GC.KeepAlive(item);
            }
        }
    }
}
