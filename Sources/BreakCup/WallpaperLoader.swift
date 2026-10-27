import AppKit
import CoreImage
import ImageIO

/// 读取每块屏幕的桌面壁纸，缩小并模糊后做休息屏背景。
/// 动态或航拍壁纸没有对应图片文件时，系统给的是默认航拍的静帧。
@MainActor
enum WallpaperLoader {
    private static var cache: [URL: NSImage] = [:]
    private static let context = CIContext(options: [.useSoftwareRenderer: false])

    static func background(for screen: NSScreen) -> NSImage? {
        guard let url = NSWorkspace.shared.desktopImageURL(for: screen) else { return nil }
        if let cached = cache[url] { return cached }
        let image = render(url)
        if let image { cache[url] = image }
        return image
    }

    private static func render(_ url: URL) -> NSImage? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: 1600
        ]
        guard let thumbnail = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        let input = CIImage(cgImage: thumbnail)
        let output = input
            .clampedToExtent()
            .applyingGaussianBlur(sigma: 16)
            .cropped(to: input.extent)
            .applyingFilter("CIColorControls", parameters: [
                kCIInputSaturationKey: 0.9,
                kCIInputBrightnessKey: -0.04
            ])
        guard let cgImage = context.createCGImage(output, from: input.extent) else { return nil }
        return NSImage(cgImage: cgImage, size: NSSize(width: cgImage.width, height: cgImage.height))
    }
}
