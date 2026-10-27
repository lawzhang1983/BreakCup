// 生成歇一杯的应用图标：拿铁色圆角底 + 冒热气的咖啡杯（SF Symbols）
// 用法：swiftc generate-icon.swift -o generate-icon && ./generate-icon <输出目录>/AppIcon.iconset
import AppKit

let output = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.iconset")
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

func color(_ hex: UInt32) -> NSColor {
    NSColor(
        srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
        green: CGFloat((hex >> 8) & 0xFF) / 255,
        blue: CGFloat(hex & 0xFF) / 255,
        alpha: 1
    )
}

func renderIcon(pixels: Int) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    rep.size = NSSize(width: pixels, height: pixels)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

    let size = CGFloat(pixels)
    // macOS 图标网格：1024 画布里主体约 824，圆角约 185
    let inset = size * 0.0977
    let body = NSRect(x: inset, y: inset, width: size - inset * 2, height: size - inset * 2)
    let radius = body.width * 0.2247
    let shape = NSBezierPath(roundedRect: body, xRadius: radius, yRadius: radius)

    NSGraphicsContext.saveGraphicsState()
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.28)
    shadow.shadowBlurRadius = size * 0.02
    shadow.shadowOffset = NSSize(width: 0, height: -size * 0.008)
    shadow.set()
    color(0xE9CCA8).setFill()
    shape.fill()
    NSGraphicsContext.restoreGraphicsState()

    NSGradient(colors: [color(0xF8EBDB), color(0xE4C29C)])!.draw(in: shape, angle: -90)

    // 杯子：深咖啡色杯身，热气用浅一档的焦糖色
    let symbolConfig = NSImage.SymbolConfiguration(pointSize: size * 0.42, weight: .medium)
        .applying(NSImage.SymbolConfiguration(paletteColors: [color(0xA7714B), color(0x5A3622)]))
    if let cup = NSImage(systemSymbolName: "cup.and.heat.waves.fill", accessibilityDescription: nil)?
        .withSymbolConfiguration(symbolConfig) {
        let cupSize = cup.size
        let scale = min(body.width * 0.62 / cupSize.width, body.height * 0.62 / cupSize.height)
        let drawSize = NSSize(width: cupSize.width * scale, height: cupSize.height * scale)
        let rect = NSRect(
            x: body.midX - drawSize.width / 2,
            y: body.midY - drawSize.height / 2 - body.height * 0.02,
            width: drawSize.width,
            height: drawSize.height
        )
        cup.draw(in: rect)
    }

    NSGraphicsContext.restoreGraphicsState()
    return rep
}

let sizes: [(String, Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32),
    ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256),
    ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024)
]
for (name, pixels) in sizes {
    let data = renderIcon(pixels: pixels).representation(using: .png, properties: [:])!
    try data.write(to: output.appendingPathComponent("\(name).png"))
}
print("iconset -> \(output.path)")
