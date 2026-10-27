import Testing

/// 本机只装 Command Line Tools 时 `swift test` 只编译不执行，
/// 所以测试编成可执行目标，用 `swift run BreakCupCoreTests` 直接跑 swift-testing。
@main
struct TestRunner {
    static func main() async {
        await Testing.__swiftPMEntryPoint() as Never
    }
}
