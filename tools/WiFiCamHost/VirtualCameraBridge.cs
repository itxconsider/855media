using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace WiFiCamHost;

public class VirtualCameraBridge : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private readonly object _lock = new();

    // OBS VirtualCam standard shared memory map name
    private const string MMF_NAME = "OBSVirtualCamSharedMemory";

    public VirtualCameraBridge(int width, int height)
    {
        _width = width;
        _height = height;
        InitSharedMemory();
    }

    private void InitSharedMemory()
    {
        try
        {
            // Frame size for BGRA 32bpp + header offset
            long bufferSize = (long)_width * _height * 4 + 64;
            if (OperatingSystem.IsWindows())
            {
                _mmf = MemoryMappedFile.CreateOrOpen(MMF_NAME, bufferSize);
                _accessor = _mmf.CreateViewAccessor();
                Console.WriteLine(
                    $"[VCam] Shared memory bridge initialized: '{MMF_NAME}' ({_width}x{_height})"
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VCam] Warning: Shared memory init error: {ex.Message}");
        }
    }

    public unsafe void PushFrame(IntPtr samplePtr, int width, int height, int stride)
    {
        if (_accessor == null || samplePtr == IntPtr.Zero)
            return;

        lock (_lock)
        {
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            try
            {
                int* intPtr = (int*)ptr;
                intPtr[0] = width;
                intPtr[1] = height;

                byte* frameDataPtr = ptr + 16;
                int length = width * height * 4;
                Buffer.MemoryCopy((void*)samplePtr, frameDataPtr, length, length);
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    public unsafe void PushFrame(byte[] sample, int width, int height, int stride)
    {
        if (_accessor == null || sample == null)
            return;

        lock (_lock)
        {
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            try
            {
                // Write simple frame header: [width (4 bytes), height (4 bytes), timestamp (8 bytes)]
                int* intPtr = (int*)ptr;
                intPtr[0] = width;
                intPtr[1] = height;

                byte* frameDataPtr = ptr + 16;
                Marshal.Copy(
                    sample,
                    0,
                    (IntPtr)frameDataPtr,
                    Math.Min(sample.Length, width * height * 4)
                );
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    public unsafe void PushI420Frame(byte[] i420Data, int width, int height)
    {
        if (_accessor == null || i420Data == null || i420Data.Length < width * height * 3 / 2)
            return;

        lock (_lock)
        {
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            try
            {
                int* intPtr = (int*)ptr;
                intPtr[0] = width;
                intPtr[1] = height;

                byte* frameDataPtr = ptr + 16;

                // High performance unrolled I420 (YUV420p) to BGRA32 direct conversion
                int ySize = width * height;
                int uvStride = width / 2;
                int uOffset = ySize;
                int vOffset = ySize + (ySize / 4);

                fixed (byte* yPtr = i420Data)
                {
                    byte* uPtr = yPtr + uOffset;
                    byte* vPtr = yPtr + vOffset;

                    for (int y = 0; y < height; y++)
                    {
                        int yLine = y * width;
                        int uvLine = (y / 2) * uvStride;
                        byte* bgraRow = frameDataPtr + (y * width * 4);

                        for (int x = 0; x < width; x++)
                        {
                            int yVal = yPtr[yLine + x] - 16;
                            if (yVal < 0)
                                yVal = 0;

                            int uvIdx = uvLine + (x / 2);
                            int uVal = uPtr[uvIdx] - 128;
                            int vVal = vPtr[uvIdx] - 128;

                            int r = (298 * yVal + 409 * vVal + 128) >> 8;
                            int g = (298 * yVal - 100 * uVal - 208 * vVal + 128) >> 8;
                            int b = (298 * yVal + 516 * uVal + 128) >> 8;

                            int pixelIdx = x * 4;
                            bgraRow[pixelIdx + 0] = (byte)(b < 0 ? 0 : (b > 255 ? 255 : b)); // B
                            bgraRow[pixelIdx + 1] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g)); // G
                            bgraRow[pixelIdx + 2] = (byte)(r < 0 ? 0 : (r > 255 ? 255 : r)); // R
                            bgraRow[pixelIdx + 3] = 255; // A
                        }
                    }
                }
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    public void Dispose()
    {
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}
