// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Media;
using QRCoder;
using QRCoder.Xaml;

namespace Foundry.AutopilotInteractive.Services.QrCode;

/// <summary>
/// Renders text as a QR code <see cref="ImageSource"/>.
/// </summary>
public static class QrCodeImageFactory
{
    private const int PixelsPerModule = 10;

    /// <summary>
    /// Creates a frozen vector image of the QR code (error correction level M, quiet zone included, black on white).
    /// </summary>
    /// <remarks>
    /// Frozen so the image can be created on a worker thread and bound from the UI thread. Black on white regardless of
    /// the theme: scanners need dark modules on a light background.
    /// </remarks>
    /// <param name="content">Text to encode.</param>
    /// <returns>The frozen QR code image.</returns>
    public static ImageSource Create(string content)
    {
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        XamlQRCode qrCode = new(data);

        DrawingImage image = qrCode.GetGraphic(
            new Size(data.ModuleMatrix.Count * PixelsPerModule, data.ModuleMatrix.Count * PixelsPerModule),
            Brushes.Black,
            Brushes.White,
            drawQuietZones: true);
        image.Freeze();
        return image;
    }
}
