import Foundation

public struct Tip: Equatable, Hashable, Identifiable, Sendable {
    public let id: String
    public let category: TipCategory
    public let text: String

    public init(id: String, category: TipCategory, text: String) {
        self.id = id
        self.category = category
        self.text = text
    }
}

/// 内置健康提示。数字类说法只保留有公开出处的：
/// 20-20-20 用眼法则（美国眼科学会）、WHO 2020《身体活动和久坐行为指南》、《中国居民膳食指南（2022）》。
public enum TipLibrary {
    public static let all: [Tip] = exercise + eyes + hydration + sedentary + breathing

    static let exercise: [Tip] = make(.exercise, [
        "站起来，双手十指交叉举过头顶，向上伸展 10 秒，重复 3 次。",
        "耸肩放松：双肩用力上提靠近耳朵，停 3 秒再放下，重复 10 次。",
        "扩胸：双手在背后十指相扣，挺胸，肩胛骨向中间夹紧，保持 15 秒。",
        "踮脚尖：扶着桌子，脚跟慢慢抬起再放下，做 15 次，唤醒小腿肌肉。",
        "颈部侧拉：头慢慢向左肩倾斜，停 10 秒，再换右侧；动作要慢，别甩头。",
        "去走廊或楼梯走一圈。让身体动起来，比做什么标准动作都重要。",
        "靠墙站：后脑、肩胛、臀部、脚跟贴住墙面，保持 30 秒，找回挺拔的站姿。",
        "弓步拉伸髋部：一腿在前屈膝，后腿伸直，身体缓慢前移，每侧保持 20 秒。",
        "转腰：双脚与肩同宽，双手叉腰，上身缓慢向左右各转 10 次。",
        "手腕放松：握拳 5 秒再用力张开五指，重复 10 次，再轻轻拉伸前臂。",
        "原地高抬腿 30 秒，让心率稍微升起来一点。"
    ])

    static let eyes: [Tip] = make(.eyes, [
        "20-20-20 法则：每用眼 20 分钟，看向约 6 米外的物体至少 20 秒。",
        "走到窗边，看看最远处的楼顶或树梢，让一直绷紧的睫状肌放松下来。",
        "盯屏幕时眨眼会明显变少。现在有意识地慢慢眨眼 10 次。",
        "双手搓热后轻轻捂住双眼 30 秒，只捂不压，别按压眼球。",
        "远近交替：先看远处 10 秒，再看眼前的手指 10 秒，重复 5 次。",
        "闭上眼睛，眼球缓慢地上下、左右各转动几次。"
    ])

    static let hydration: [Tip] = make(.hydration, [
        "去接一杯水吧：小口慢慢喝，别等口渴了才想起来。",
        "《中国居民膳食指南（2022）》建议，身体活动少的成年人每天饮水约 1500–1700 毫升。",
        "尿液颜色是简单的补水参考：淡黄色通常说明喝得够。",
        "咖啡和浓茶可以喝，但别只喝它们，白开水是最好的补水方式。",
        "去更远一点的饮水机接水，顺便多走几步。",
        "口渴是身体已经需要补水的信号，在桌上放一杯水，随手就能喝。"
    ])

    static let sedentary: [Tip] = make(.sedentary, [
        "久坐时小腿肌肉几乎不收缩，下肢血液回流变慢。起身走动，能让小腿重新当起“第二心脏”。",
        "WHO 2020 年指南建议减少久坐：用任何强度的身体活动替代久坐，都对健康有益。",
        "长时间保持同一坐姿，颈椎和腰椎持续受力，是颈肩酸痛、腰背痛的常见诱因。",
        "研究发现，久坐时间越长，肥胖、2 型糖尿病和心血管疾病的风险越高。",
        "久坐时髋部前侧的肌肉一直处于缩短状态，时间长了容易僵紧，起身拉一拉。",
        "饭后坐着不动血糖更容易升高，起身慢走几分钟有助于餐后血糖平稳。",
        "就算有锻炼习惯，也建议隔一阵就起身，打断连续的久坐。"
    ])

    static let breathing: [Tip] = make(.breathing, [
        "腹式呼吸：用 4 秒吸气让腹部鼓起，再用 6 秒慢慢呼出，重复 6 次。",
        "这几分钟别拿起手机，让眼睛和大脑一起真正休息。",
        "放下手头的事，什么都不想，只去感受双脚踩在地面上的感觉。",
        "慢慢地深吸一口气，憋 2 秒，再把肩膀随着呼气一起沉下去。",
        "看看窗外的天色。你已经专注了很久，值得这几分钟的空白。"
    ])

    private static func make(_ category: TipCategory, _ texts: [String]) -> [Tip] {
        texts.enumerated().map { index, text in
            Tip(id: "\(category.rawValue)-\(index + 1)", category: category, text: text)
        }
    }
}

/// 按类别轮流出题：类别顺序固定（先动起来），同一类别内随机且一轮之内不重复。
public struct TipDeck {
    private let categories: [TipCategory]
    private let tipsByCategory: [TipCategory: [Tip]]
    private let shuffle: ([Tip]) -> [Tip]
    private var queues: [TipCategory: [Tip]] = [:]
    private var cursor = 0

    public init(
        categories: [TipCategory],
        tips: [Tip] = TipLibrary.all,
        shuffle: @escaping ([Tip]) -> [Tip] = { $0.shuffled() }
    ) {
        let grouped = Dictionary(grouping: tips, by: \.category)
        let usable = categories.filter { !(grouped[$0] ?? []).isEmpty }
        let fallback = TipCategory.allCases.filter { !(grouped[$0] ?? []).isEmpty }
        self.categories = usable.isEmpty ? fallback : usable
        self.tipsByCategory = grouped
        self.shuffle = shuffle
    }

    public mutating func next() -> Tip {
        let category = categories[cursor % categories.count]
        cursor += 1
        if queues[category, default: []].isEmpty {
            queues[category] = shuffle(tipsByCategory[category] ?? [])
        }
        return queues[category]!.removeFirst()
    }
}
