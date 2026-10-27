import AppKit
import BreakCupCore
import SwiftUI

/// 菜单栏弹出的参数设置面板
struct SettingsView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            statusCard

            section("时间") {
                stepperRow("可使用时间", value: $model.settings.workMinutes,
                           range: BreakSettings.workRange, step: 5) { BreakFormat.minutes($0) }
                stepperRow("休息时间", value: $model.settings.restMinutes,
                           range: BreakSettings.restRange, step: 1) { BreakFormat.minutes($0) }
                stepperRow("到点前预告", value: $model.settings.warningSeconds,
                           range: BreakSettings.warningRange, step: 15) { $0 == 0 ? "不预告" : "\(Int($0)) 秒" }
            }

            section("智能判断") {
                switchRow(
                    "离开电脑视为已休息",
                    hint: "键鼠闲置满 \(Int(model.settings.awaySeconds / 60)) 分钟就重新计时，开会时不算",
                    isOn: $model.settings.resetWhenAway
                )
                switchRow("开会时推迟休息", hint: model.meetingHint, isOn: $model.settings.deferWhenMediaInUse)
            }

            section("休息屏") {
                stepperRow("提示轮换间隔", value: $model.settings.tipIntervalSeconds,
                           range: BreakSettings.tipIntervalRange, step: 5) { "\(Int($0)) 秒" }
                categoryGrid
            }

            section("其他") {
                switchRow("菜单栏显示剩余时间", isOn: $model.settings.showCountdownInMenuBar)
                switchRow(
                    "登录时自动启动",
                    hint: model.loginItemNote,
                    attention: true,
                    isOn: Binding(get: { model.launchAtLogin }, set: { model.setLaunchAtLogin($0) })
                )
            }

            footer
        }
        .padding(16)
        .frame(width: 360)
        .controlSize(.small)
    }

    private var header: some View {
        HStack(spacing: 10) {
            Image(systemName: "cup.and.saucer.fill")
                .font(.system(size: 22))
                .foregroundStyle(Color.coffee)
            VStack(alignment: .leading, spacing: 1) {
                Text("歇一杯").font(.headline)
                Text("BreakCup · 久坐强制休息").font(.caption2).foregroundStyle(.secondary)
            }
            Spacer()
            PhasePill(phase: model.phase)
        }
    }

    private var statusCard: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 4) {
                Text(model.countdownCaption)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                HintMark(text: model.detail)
            }
            Text(model.countdown)
                .font(.system(size: 38, weight: .light, design: .rounded))
                .monospacedDigit()
            ProgressView(value: model.progress)
                .tint(Color.coffee)
            HStack(spacing: 8) {
                Button(model.confirmingRest ? "确认开始休息" : "立即休息") {
                    model.requestRestNow()
                }
                .buttonStyle(.borderedProminent)
                .tint(model.confirmingRest ? .orange : Color.coffee)
                .help("开始后要等休息时间结束才能解锁，需点两次确认")
                Button("重新计时") { model.onRestartWork?() }
                Spacer()
                Text("今日已休息 \(model.todayRestCount) 次")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .disabled(model.phase == .resting)
            .padding(.top, 4)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.coffee.opacity(0.10)))
    }

    private var categoryGrid: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("提示类别")
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 3), spacing: 6) {
                ForEach(TipCategory.allCases, id: \.self) { category in
                    let selected = model.settings.enabledCategories.contains(category)
                    Button {
                        model.toggle(category)
                    } label: {
                        Label(category.title, systemImage: category.symbol)
                            .font(.caption)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 5)
                            .foregroundStyle(selected ? Color.coffee : Color.secondary)
                            .background(
                                RoundedRectangle(cornerRadius: 7, style: .continuous)
                                    .fill(selected ? Color.coffee.opacity(0.16) : Color.secondary.opacity(0.08))
                            )
                            .overlay(
                                RoundedRectangle(cornerRadius: 7, style: .continuous)
                                    .strokeBorder(selected ? Color.coffee.opacity(0.6) : .clear)
                            )
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
        }
    }

    private var footer: some View {
        HStack {
            Text("v\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev")")
                .font(.caption2)
                .foregroundStyle(.tertiary)
            Spacer()
            Button("退出歇一杯") { NSApp.terminate(nil) }
                .disabled(model.phase == .resting)
        }
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 10) {
                content()
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.secondary.opacity(0.07)))
        }
    }

    private func stepperRow(
        _ title: String,
        value: Binding<Double>,
        range: ClosedRange<Double>,
        step: Double,
        format: @escaping (Double) -> String
    ) -> some View {
        HStack {
            Text(title)
            Spacer()
            Text(format(value.wrappedValue))
                .monospacedDigit()
                .foregroundStyle(.secondary)
            Stepper("", value: value, in: range, step: step)
                .labelsHidden()
        }
    }

    private func switchRow(_ title: String, hint: String? = nil, attention: Bool = false, isOn: Binding<Bool>) -> some View {
        HStack(alignment: .center, spacing: 4) {
            Text(title)
            if let hint { HintMark(text: hint, attention: attention) }
            Spacer(minLength: 12)
            Toggle("", isOn: isOn)
                .labelsHidden()
                .toggleStyle(.switch)
        }
    }
}

/// 说明文字收进感叹号，鼠标指上去才显示，面板保持干净
private struct HintMark: View {
    let text: String
    /// 需要处理的问题（如登录项待批准）用橙色提醒
    var attention = false

    var body: some View {
        if !text.isEmpty {
            Image(systemName: "exclamationmark.circle")
                .font(.caption)
                .foregroundStyle(attention ? Color.orange : Color.secondary)
                .padding(3)
                .overlay(ToolTipArea(text: text))
                .accessibilityLabel(text)
        }
    }
}

/// 透明的原生视图，只负责挂系统悬停提示：走 AppKit 的 toolTip，提示延迟受 NSInitialToolTipDelay 控制
private struct ToolTipArea: NSViewRepresentable {
    let text: String

    func makeNSView(context: Context) -> NSView { NSView() }

    func updateNSView(_ view: NSView, context: Context) {
        if view.toolTip != text { view.toolTip = text }
    }
}

private struct PhasePill: View {
    let phase: PhaseKind

    var body: some View {
        Text(phase.title)
            .font(.caption.weight(.medium))
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .foregroundStyle(color)
            .background(Capsule().fill(color.opacity(0.15)))
    }

    private var color: Color {
        switch phase {
        case .working: return .green
        case .warning: return .orange
        case .deferred: return .blue
        case .resting: return Color.coffee
        }
    }
}
