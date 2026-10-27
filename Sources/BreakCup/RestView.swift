import AppKit
import BreakCupCore
import SwiftUI

/// 一次休息的共享状态；多块屏幕上的休息屏观察同一个会话
@MainActor
final class RestSession: ObservableObject {
    @Published var tip: Tip
    @Published var finished = false
    let startedAt: Date
    let until: Date

    init(tip: Tip, startedAt: Date, until: Date) {
        self.tip = tip
        self.startedAt = startedAt
        self.until = until
    }
}

struct RestView: View {
    @ObservedObject var session: RestSession
    let size: CGSize
    /// 只有离屏截图时传入；实际休息屏的背景在 SwiftUI 之下由图层绘制
    var staticBackground: NSImage?? = nil

    /// 以 16:9 高度为基准的排版单位，带鱼屏和竖屏也不至于失衡
    private var unit: CGFloat { min(size.height, size.width * 0.5625) }

    var body: some View {
        ZStack {
            if let staticBackground {
                StaticBackground(image: staticBackground)
            }
            LinearGradient(
                colors: [.black.opacity(0.55), .black.opacity(0.30), .black.opacity(0.62)],
                startPoint: .top,
                endPoint: .bottom
            )
            if session.finished {
                welcome.transition(.opacity)
            } else {
                content.transition(.opacity)
            }
            footer
        }
        .frame(width: size.width, height: size.height)
        .environment(\.colorScheme, .dark)
    }

    private var content: some View {
        VStack(spacing: unit * 0.034) {
            Label("歇一杯 · 起身休息中", systemImage: "cup.and.heat.waves.fill")
                .font(.system(size: unit * 0.021, weight: .medium))
                .foregroundStyle(.white.opacity(0.78))

            TimelineView(.periodic(from: .now, by: 0.5)) { context in
                let total = max(1, session.until.timeIntervalSince(session.startedAt))
                let remaining = max(0, session.until.timeIntervalSince(context.date))
                VStack(spacing: unit * 0.026) {
                    Text(BreakFormat.clock(remaining))
                        .font(.system(size: unit * 0.15, weight: .thin, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(.white)
                    RestProgressBar(progress: 1 - remaining / total)
                        .frame(width: unit * 0.56, height: max(4, unit * 0.0045))
                }
            }

            TipCard(tip: session.tip, unit: unit)
                .id(session.tip.id)
                .transition(.opacity.combined(with: .offset(y: unit * 0.01)))
                .padding(.top, unit * 0.02)
        }
        .padding(.bottom, unit * 0.04)
    }

    private var welcome: some View {
        VStack(spacing: unit * 0.03) {
            Image(systemName: "cup.and.saucer.fill")
                .font(.system(size: unit * 0.08, weight: .light))
                .foregroundStyle(Color.latte)
            Text("休息结束，欢迎回来")
                .font(.system(size: unit * 0.05, weight: .light))
                .foregroundStyle(.white)
        }
    }

    private var footer: some View {
        VStack {
            Spacer()
            Text("\(session.until.formatted(date: .omitted, time: .shortened)) 自动结束 · 休息期间键盘和鼠标暂停使用")
                .font(.system(size: unit * 0.016))
                .foregroundStyle(.white.opacity(0.5))
                .padding(.bottom, unit * 0.05)
        }
        .opacity(session.finished ? 0 : 1)
    }
}

private struct TipCard: View {
    let tip: Tip
    let unit: CGFloat

    var body: some View {
        VStack(spacing: unit * 0.02) {
            Label(tip.category.title, systemImage: tip.category.symbol)
                .font(.system(size: unit * 0.019, weight: .semibold))
                .foregroundStyle(Color.latte)
                .padding(.horizontal, unit * 0.016)
                .padding(.vertical, unit * 0.007)
                .background(Capsule().fill(.white.opacity(0.10)))
            Text(tip.text)
                .font(.system(size: unit * 0.032))
                .foregroundStyle(.white.opacity(0.95))
                .multilineTextAlignment(.center)
                .lineSpacing(unit * 0.009)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, unit * 0.05)
        .padding(.vertical, unit * 0.036)
        .frame(width: unit * 1.1)
        .background(
            RoundedRectangle(cornerRadius: unit * 0.026, style: .continuous)
                .fill(.black.opacity(0.30))
                .overlay(
                    RoundedRectangle(cornerRadius: unit * 0.026, style: .continuous)
                        .strokeBorder(.white.opacity(0.10))
                )
        )
    }
}

private struct RestProgressBar: View {
    let progress: Double

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.16))
                Capsule()
                    .fill(Color.latte)
                    .frame(width: geo.size.width * min(1, max(0, progress)))
            }
        }
    }
}

/// 静态背景：只用于离屏截图。实际休息屏的背景由 `DriftingBackgroundView` 用图层动画绘制。
private struct StaticBackground: View {
    let image: NSImage?

    var body: some View {
        GeometryReader { geo in
            Group {
                if let image {
                    Image(nsImage: image)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                } else {
                    LinearGradient(
                        colors: [Color(nsColor: DriftingBackgroundView.fallbackTop), Color(nsColor: DriftingBackgroundView.fallbackBottom)],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    )
                }
            }
            .frame(width: geo.size.width, height: geo.size.height)
            .scaleEffect(1.03)
            .clipped()
        }
    }
}

/// 壁纸模糊图做底，40 秒一个来回缓慢推拉平移；没有壁纸时退回咖啡色渐变。
/// 用 Core Animation 而不是 SwiftUI 动画：动画在渲染服务里跑，不占主线程，6K 双屏也省电。
final class DriftingBackgroundView: NSView {
    static let fallbackTop = NSColor(srgbRed: 0.17, green: 0.12, blue: 0.10, alpha: 1)
    static let fallbackBottom = NSColor(srgbRed: 0.36, green: 0.25, blue: 0.18, alpha: 1)

    init(frame: NSRect, image: NSImage?) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.masksToBounds = true
        layer?.backgroundColor = Self.fallbackTop.cgColor

        let content: CALayer
        if let cgImage = image?.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            content = CALayer()
            content.contents = cgImage
            content.contentsGravity = .resizeAspectFill
        } else {
            let gradient = CAGradientLayer()
            gradient.colors = [Self.fallbackTop.cgColor, Self.fallbackBottom.cgColor]
            gradient.startPoint = CGPoint(x: 0, y: 1)
            gradient.endPoint = CGPoint(x: 1, y: 0)
            content = gradient
        }
        content.frame = bounds
        content.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        layer?.addSublayer(content)

        let shift = bounds.width * 0.015
        let drift = CABasicAnimation(keyPath: "transform")
        drift.fromValue = CATransform3DConcat(CATransform3DMakeScale(1.03, 1.03, 1), CATransform3DMakeTranslation(shift, 0, 0))
        drift.toValue = CATransform3DConcat(CATransform3DMakeScale(1.12, 1.12, 1), CATransform3DMakeTranslation(-shift, 0, 0))
        drift.duration = 40
        drift.autoreverses = true
        drift.repeatCount = .infinity
        drift.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        content.add(drift, forKey: "drift")
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
}
