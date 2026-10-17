using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11 = Vortice.Direct3D11.D3D11;

namespace CaptureZoo;

/// <summary>Captures an HDR desktop frame as FP16 scRGB and tone-maps it to sharing-safe sRGB.</summary>
internal static class HdrDesktopCapture
{
    public static bool TryCapture(Rectangle region, out Bitmap image)
    {
        image = null!;
        try
        {
            AppLog.Info($"HDR capture request: {region}.");
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                var adapterResult = factory.EnumAdapters(adapterIndex, out IDXGIAdapter? adapter);
                if (adapterResult.Failure || adapter is null) break;
                using (adapter)
                {
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        var outputResult = adapter.EnumOutputs(outputIndex, out IDXGIOutput? output);
                        if (outputResult.Failure || output is null) break;
                        using (output)
                        {
                            var desktop = output.Description.DesktopCoordinates;
                            AppLog.Info($"DXGI output: {desktop}; device={output.Description.DeviceName}.");
                            if (region.Right > desktop.Right || region.Bottom > desktop.Bottom || region.Left < desktop.Left || region.Top < desktop.Top) continue;
                            return CaptureOutput(adapter, output, region, out image);
                        }
                    }
                }
            }
        }
        catch (Exception error)
        {
            // SDR displays, unsupported drivers, and secure desktops fall back to the GDI preview capture.
            AppLog.Error(error);
        }
        return false;
    }

    private static bool CaptureOutput(IDXGIAdapter adapter, IDXGIOutput output, Rectangle region, out Bitmap image)
    {
        image = null!;
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
        var createResult = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, levels, out var device);
        if (createResult.Failure || device is null) return false;
        using (device)
        {
            var context = device.ImmediateContext;
            using var output5 = output.QueryInterface<IDXGIOutput5>();
            using var duplication = output5.DuplicateOutput1(device, new[] { Format.R16G16B16A16_Float });
            var desktop = output.Description.DesktopCoordinates;
            var width = desktop.Right - desktop.Left;
            var height = desktop.Bottom - desktop.Top;
            var description = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R16G16B16A16_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read
            };
            using var staging = device.CreateTexture2D(description);
            IDXGIResource? resource = null;
            var frameAcquired = false;
            try
            {
                duplication.AcquireNextFrame(1000, out _, out resource).CheckError();
                frameAcquired = true;
                if (resource is null) return false;
                using var texture = resource.QueryInterface<ID3D11Texture2D>();
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    image = ToneMapRegion(mapped, region, desktop.Left, desktop.Top, out var maximum);
                    if (maximum < 0.00001f)
                    {
                        image.Dispose();
                        image = null!;
                        AppLog.Info("FP16 frame is entirely zero; retrying with DirectX BGRA desktop duplication.");
                        return CaptureBgra(adapter, output, region, out image);
                    }
                }
                finally
                {
                    context.Unmap(staging, 0);
                }
                return true;
            }
            finally
            {
                resource?.Dispose();
                if (frameAcquired) duplication.ReleaseFrame();
            }
        }
    }

    private static bool CaptureBgra(IDXGIAdapter adapter, IDXGIOutput output, Rectangle region, out Bitmap image)
    {
        image = null!;
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
        var createResult = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, levels, out var device);
        if (createResult.Failure || device is null) return false;
        using (device)
        {
            var context = device.ImmediateContext;
            using var output1 = output.QueryInterface<IDXGIOutput1>();
            using var duplication = output1.DuplicateOutput(device);
            var desktop = output.Description.DesktopCoordinates;
            var description = new Texture2DDescription
            {
                Width = (uint)(desktop.Right - desktop.Left), Height = (uint)(desktop.Bottom - desktop.Top),
                MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read
            };
            using var staging = device.CreateTexture2D(description);
            IDXGIResource? resource = null;
            var frameAcquired = false;
            try
            {
                duplication.AcquireNextFrame(1000, out _, out resource).CheckError();
                frameAcquired = true;
                if (resource is null) return false;
                using var texture = resource.QueryInterface<ID3D11Texture2D>();
                context.CopyResource(staging, texture);
                var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    image = CopyBgraRegion(mapped, region, desktop.Left, desktop.Top);
                    AppLog.Info("BGRA DirectX fallback succeeded.");
                    return true;
                }
                finally { context.Unmap(staging, 0); }
            }
            finally
            {
                resource?.Dispose();
                if (frameAcquired) duplication.ReleaseFrame();
            }
        }
    }

    private static unsafe Bitmap ToneMapRegion(MappedSubresource mapped, Rectangle region, int outputLeft, int outputTop, out float maximum)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var offsetX = region.Left - outputLeft;
            var offsetY = region.Top - outputTop;
            maximum = 0f;
            for (var y = 0; y < region.Height; y++)
            {
                var source = (ushort*)((byte*)mapped.DataPointer + (offsetY + y) * mapped.RowPitch) + offsetX * 4;
                var destination = (byte*)bits.Scan0 + y * bits.Stride;
                for (var x = 0; x < region.Width; x++)
                {
                    // DXGI FP16 capture is scRGB linear. Apply a compact HDR-to-SDR tone curve then sRGB encode.
                    destination[x * 4] = ToSrgb(source[x * 4 + 2]);
                    destination[x * 4 + 1] = ToSrgb(source[x * 4 + 1]);
                    destination[x * 4 + 2] = ToSrgb(source[x * 4]);
                    destination[x * 4 + 3] = 255;
                    maximum = Math.Max(maximum, Math.Max((float)BitConverter.UInt16BitsToHalf(source[x * 4]), Math.Max((float)BitConverter.UInt16BitsToHalf(source[x * 4 + 1]), (float)BitConverter.UInt16BitsToHalf(source[x * 4 + 2]))));
                }
            }
            AppLog.Info($"HDR FP16 maximum linear component: {maximum:F5}.");
        }
        finally { bitmap.UnlockBits(bits); }
        return bitmap;
    }

    private static unsafe Bitmap CopyBgraRegion(MappedSubresource mapped, Rectangle region, int outputLeft, int outputTop)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
        var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var offsetX = region.Left - outputLeft;
            var offsetY = region.Top - outputTop;
            var rowLength = region.Width * 4;
            for (var y = 0; y < region.Height; y++)
            {
                var source = (byte*)mapped.DataPointer + (offsetY + y) * mapped.RowPitch + offsetX * 4;
                var destination = (byte*)bits.Scan0 + y * bits.Stride;
                Buffer.MemoryCopy(source, destination, bits.Stride, rowLength);
            }
        }
        finally { bitmap.UnlockBits(bits); }
        return bitmap;
    }

    private static byte ToSrgb(ushort raw)
    {
        var linear = Math.Max(0f, (float)BitConverter.UInt16BitsToHalf(raw));
        var mapped = linear / (1f + linear); // Reinhard: keeps HDR highlights instead of clipping them.
        var srgb = mapped <= 0.0031308f ? 12.92f * mapped : 1.055f * MathF.Pow(mapped, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(srgb * 255f), 0, 255);
    }
}
