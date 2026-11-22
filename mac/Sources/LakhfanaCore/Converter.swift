import Foundation

/// Re-types text as if the same keys had been pressed in the other layout.
/// Direction is decided per word (whitespace-separated), so mixed text converts both ways.
/// Port of the Windows Converter.cs.
public final class Converter {
    private static let maxCandidates = 256

    private let mapA: LayoutMap
    private let mapB: LayoutMap

    public var idA: String { return mapA.id }
    public var idB: String { return mapB.id }

    public init?(a: InputSourceInfo, b: InputSourceInfo) {
        guard a.id != b.id, let ma = LayoutMap(a), let mb = LayoutMap(b) else { return nil }
        mapA = ma
        mapB = mb
    }

    public func convert(_ text: String, fallbackSourceID: String?) -> (text: String, targetID: String) {
        let scalars = Array(text.unicodeScalars)
        if scalars.isEmpty { return (text, mapB.id) }

        // Runs of whitespace / non-whitespace; whitespace is copied exactly.
        var runs: [ArraySlice<Unicode.Scalar>] = []
        var isWord: [Bool] = []
        var start = 0
        for i in 1...scalars.count {
            let boundary = i == scalars.count
                || scalars[i].properties.isWhitespace != scalars[i - 1].properties.isWhitespace
            if !boundary { continue }
            runs.append(scalars[start..<i])
            isWord.append(!scalars[start].properties.isWhitespace)
            start = i
        }

        var totalA = 0, totalB = 0
        var wordDir = [Int](repeating: 0, count: runs.count)   // +1: A→B, -1: B→A
        for r in 0..<runs.count where isWord[r] {
            let (a, b) = count(runs[r])
            totalA += a
            totalB += b
            wordDir[r] = a > b ? 1 : (b > a ? -1 : 0)
        }
        var globalDir = totalA > totalB ? 1 : (totalB > totalA ? -1 : 0)
        if globalDir == 0 {
            globalDir = (fallbackSourceID != nil && fallbackSourceID == mapB.id) ? -1 : 1
        }

        var out: [Unicode.Scalar] = []
        out.reserveCapacity(scalars.count + 8)
        var toB = 0, toA = 0
        for r in 0..<runs.count {
            if !isWord[r] { out.append(contentsOf: runs[r]); continue }
            let dir = wordDir[r] != 0 ? wordDir[r] : globalDir
            let src = dir > 0 ? mapA : mapB
            let dst = dir > 0 ? mapB : mapA
            out.append(contentsOf: Converter.convertWord(Array(runs[r]), src, dst))
            if dir > 0 { toB += runs[r].count } else { toA += runs[r].count }
        }
        return (LayoutMap.string(out), toB >= toA ? mapB.id : mapA.id)
    }

    private func count(_ word: ArraySlice<Unicode.Scalar>) -> (Int, Int) {
        var a = 0, b = 0
        for s in word {
            let inA = mapA.scalars.contains(s)
            let inB = mapB.scalars.contains(s)
            if inA && !inB { a += 1 } else if inB && !inA { b += 1 }
        }
        return (a, b)
    }

    /// One segmentation step: a keystroke of the source layout covering `length` scalars, or pass-through (-1).
    private struct Step {
        let length: Int
        let keystroke: Int
    }

    private static func options(_ word: [Unicode.Scalar], _ pos: Int, _ src: LayoutMap) -> [Step] {
        var result: [Step] = []
        let maxLen = min(src.maxLength, word.count - pos)
        if maxLen >= 1 {
            for len in stride(from: maxLen, through: 1, by: -1) {
                if let ks = src.reverse[LayoutMap.string(word[pos..<(pos + len)])] {
                    result.append(Step(length: len, keystroke: ks))
                }
            }
        }
        if result.isEmpty { result.append(Step(length: 1, keystroke: -1)) }
        return result
    }

    private static func append(_ out: inout [Unicode.Scalar], _ word: [Unicode.Scalar], _ pos: Int,
                               _ step: Step, _ dst: LayoutMap) {
        if step.keystroke >= 0, let rendered = dst.render(step.keystroke) {
            out.append(contentsOf: rendered)
        } else {
            out.append(contentsOf: word[pos..<(pos + step.length)])   // key types nothing in target: keep original
        }
    }

    private static func greedy(_ word: [Unicode.Scalar], _ src: LayoutMap, _ dst: LayoutMap) -> [Unicode.Scalar] {
        var out: [Unicode.Scalar] = []
        var i = 0
        while i < word.count {
            let step = options(word, i, src)[0]
            append(&out, word, i, step, dst)
            i += step.length
        }
        return out
    }

    static func convertWord(_ word: [Unicode.Scalar], _ src: LayoutMap, _ dst: LayoutMap) -> [Unicode.Scalar] {
        let best0 = greedy(word, src, dst)
        if !dst.isLatin { return best0 }

        var ambiguous = false
        var i = 0
        while i < word.count && !ambiguous {
            if options(word, i, src).count > 1 { ambiguous = true }
            i += 1
        }
        if !ambiguous { return best0 }

        var candidates: [[Unicode.Scalar]] = []
        var acc: [Unicode.Scalar] = []
        enumerate(word, 0, src, dst, &acc, &candidates)
        if candidates.isEmpty || candidates.count >= maxCandidates { return best0 }

        var best = best0
        var bestScore = EnglishScorer.score(best0)
        for c in candidates {
            let s = EnglishScorer.score(c)
            if s > bestScore + 1e-9 {
                best = c
                bestScore = s
            }
        }
        return best
    }

    private static func enumerate(_ word: [Unicode.Scalar], _ pos: Int, _ src: LayoutMap, _ dst: LayoutMap,
                                  _ acc: inout [Unicode.Scalar], _ results: inout [[Unicode.Scalar]]) {
        if results.count >= maxCandidates { return }
        if pos >= word.count {
            results.append(acc)
            return
        }
        for step in options(word, pos, src) {
            let mark = acc.count
            append(&acc, word, pos, step, dst)
            enumerate(word, pos + step.length, src, dst, &acc, &results)
            acc.removeSubrange(mark..<acc.count)
            if results.count >= maxCandidates { return }
        }
    }
}
