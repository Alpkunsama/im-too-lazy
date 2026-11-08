using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using libomtnet;

namespace VmixSubtitleController;

internal sealed class OmtVideoReceiver : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly OMTReceive _receiver;

    public event Action<string>? FrameReady;

    public OmtVideoReceiver(string address)
    {
        _receiver = new OMTReceive(address, OMTFrameType.Video, OMTPreferredVideoFormat.BGRA, OMTReceiveFlags.None);
        _receiver.SetSuggestedQuality(OMTQuality.High);
        _ = Task.Run(ReceiveLoop);
    }

    private void ReceiveLoop()
    {
        var token = _stop.Token;
        var frame = new OMTMediaFrame();
        var nextFrameAt = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_receiver.Receive(OMTFrameType.Video, 250, ref frame)) continue;
                if (DateTime.UtcNow < nextFrameAt) continue;
                nextFrameAt = DateTime.UtcNow + TimeSpan.FromSeconds(1d / 59.94d);
                if (frame.Data == IntPtr.Zero || frame.Width <= 0 || frame.Height <= 0 || frame.Stride < frame.Width * 4 || frame.DataLength < frame.Stride * frame.Height) continue;

                var pixels = new byte[frame.DataLength];
                Marshal.Copy(frame.Data, pixels, 0, pixels.Length);
                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Stride);
                bitmap.Freeze();
                BitmapSource image = bitmap;
                var scale = Math.Min(1d, Math.Min(1920d / frame.Width, 1080d / frame.Height));
                if (scale < 1d)
                {
                    var resized = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
                    resized.Freeze();
                    image = resized;
                }

                var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = new MemoryStream();
                encoder.Save(output);
                FrameReady?.Invoke("data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray()));
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _receiver.Dispose();
        _stop.Dispose();
    }
}
