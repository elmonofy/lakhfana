import XCTest
@testable import LakhfanaCore

final class ConverterTests: XCTestCase {

    private func layouts(_ arabicID: String) throws -> (InputSourceInfo, InputSourceInfo) {
        guard let ar = InputSources.installed(id: arabicID) else {
            throw XCTSkip("layout \(arabicID) is not installed on this machine")
        }
        guard let en = InputSources.installed(id: "com.apple.keylayout.US") else {
            throw XCTSkip("US layout is not installed on this machine")
        }
        return (ar, en)
    }

    /// The Windows-compatible "Arabic - PC" layout; its id differs between macOS versions.
    private func pcLayoutID() throws -> String {
        let candidates = ["com.apple.keylayout.ArabicPC", "com.apple.keylayout.Arabic-PC"]
        for id in candidates where InputSources.installed(id: id) != nil { return id }
        if let found = InputSources.allInstalledKeyboardLayouts().first(where: {
            $0.id.lowercased().contains("arabic") && $0.id.lowercased().contains("pc")
        }) {
            return found.id
        }
        throw XCTSkip("no Arabic PC layout installed")
    }

    private func pcConverter() throws -> Converter {
        let (ar, en) = try layouts(try pcLayoutID())
        guard let c = Converter(a: ar, b: en) else {
            XCTFail("could not build converter")
            throw XCTSkip("no converter")
        }
        return c
    }

    private func conv(_ c: Converter, _ s: String) -> String {
        return c.convert(s, fallbackSourceID: nil).text
    }

    func testDumpLayouts() throws {
        // Diagnostic output for CI logs: what each layout types per key.
        let arabicIDs = InputSources.allInstalledKeyboardLayouts().map { $0.id }.filter { $0.lowercased().contains("arabic") }
        print("installed arabic layouts: \(arabicIDs)")
        for id in ["com.apple.keylayout.US"] + arabicIDs {
            guard let info = InputSources.installed(id: id), let map = LayoutMap(info) else {
                print("layout \(id): not available")
                continue
            }
            var line = "layout \(id) (latin=\(map.isLatin), max=\(map.maxLength)):"
            for code in 0...50 {
                if let o = map.render(code) { line += " \(code)=\(LayoutMap.string(o))" }
                if let o = map.render(code | LayoutMap.shiftBit) { line += " S\(code)=\(LayoutMap.string(o))" }
            }
            print(line)
        }
        print("enabled layouts: \(InputSources.enabledKeyboardLayouts().map { $0.id })")
    }

    func testEnglishKeysToArabic() throws {
        let c = try pcConverter()
        let r = c.convert("hgsghl ugd;l", fallbackSourceID: nil)
        XCTAssertEqual(r.text, "السلام عليكم")
        XCTAssertEqual(r.targetID, try pcLayoutID())
        XCTAssertEqual(conv(c, "b"), "لا")
    }

    func testArabicKeysToEnglishWithLamAlef() throws {
        let c = try pcConverter()
        let r = c.convert("شلاخعف", fallbackSourceID: nil)
        XCTAssertEqual(r.text, "about")
        XCTAssertEqual(r.targetID, "com.apple.keylayout.US")
        XCTAssertEqual(conv(c, "مهلاف"), "light")
        XCTAssertEqual(conv(c, "لامشؤن"), "black")
        XCTAssertEqual(conv(c, "ىهلاف"), "night")
        XCTAssertEqual(conv(c, "فشلامث"), "table")
        XCTAssertEqual(conv(c, "اثممخ صخقمي"), "hello world")
    }

    func testWhitespaceAndPassThrough() throws {
        let c = try pcConverter()
        XCTAssertEqual(conv(c, "  hgsghl\r\n\tugd;l  "), "  السلام\r\n\tعليكم  ")
        XCTAssertEqual(conv(c, ""), "")
        XCTAssertEqual(conv(c, "12345"), "12345")
        XCTAssertEqual(conv(c, "   "), "   ")
        XCTAssertEqual(conv(c, "hgsghl😀"), "السلام😀")
    }

    func testMixedDirections() throws {
        let c = try pcConverter()
        XCTAssertEqual(conv(c, "hgsghl عليكم"), "السلام ugd;l")
    }

    func testPairOrderDoesNotMatter() throws {
        let (ar, en) = try layouts(try pcLayoutID())
        let c = Converter(a: en, b: ar)!
        XCTAssertEqual(c.convert("hgsghl ugd;l", fallbackSourceID: nil).text, "السلام عليكم")
    }

    /// Random keystrokes typed in English must convert to exactly the same keystrokes typed in Arabic.
    private func roundTrip(_ arabicID: String) throws {
        let (ar, en) = try layouts(arabicID)
        guard let c = Converter(a: ar, b: en), let mEn = LayoutMap(en), let mAr = LayoutMap(ar) else {
            XCTFail("could not build maps for \(arabicID)")
            return
        }
        var keys: [Int] = []
        for (ks, e) in mEn.outputs {
            guard mAr.render(ks) != nil else { continue }
            if mEn.reverse[LayoutMap.string(e)] != ks { continue }
            keys.append(ks)
        }
        keys.sort()
        XCTAssertGreaterThan(keys.count, 30)

        var seed: UInt64 = 12345
        func next(_ n: Int) -> Int {
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            return Int((seed >> 33) % UInt64(n))
        }
        var bad = 0
        for _ in 0..<2000 {
            var input: [Unicode.Scalar] = []
            var expected: [Unicode.Scalar] = []
            var letters = 0
            let n = 1 + next(8)
            for _ in 0..<n {
                let ks = keys[next(keys.count)]
                let e = mEn.render(ks)!
                input.append(contentsOf: e)
                expected.append(contentsOf: mAr.render(ks)!)
                if e[0].properties.isAlphabetic { letters += 1 }
            }
            if letters * 2 <= n { continue }
            let got = c.convert(LayoutMap.string(input), fallbackSourceID: nil).text
            if got != LayoutMap.string(expected) {
                if bad < 5 { print("roundtrip \(arabicID): [\(LayoutMap.string(input))] → [\(got)] expected [\(LayoutMap.string(expected))]") }
                bad += 1
            }
        }
        XCTAssertEqual(bad, 0, "round-trip failures for \(arabicID)")
    }

    func testRoundTripArabicPC() throws {
        try roundTrip(try pcLayoutID())
    }

    func testRoundTripMacArabic() throws {
        try roundTrip("com.apple.keylayout.Arabic")
    }

    /// Mac's default "Arabic" layout (different key positions from Windows, no lam-alef key).
    func testMacArabicLayout() throws {
        let (ar, en) = try layouts("com.apple.keylayout.Arabic")
        guard let c = Converter(a: ar, b: en) else { XCTFail("no converter"); return }
        let r = c.convert("hgsghl ugd;l", fallbackSourceID: nil)
        XCTAssertEqual(r.text, "السلام عليكم")
        XCTAssertEqual(r.targetID, "com.apple.keylayout.Arabic")
        XCTAssertEqual(c.convert("السلام عليكم", fallbackSourceID: nil).text, "hgsghl ugd;l")
        XCTAssertEqual(c.convert("شزخعف", fallbackSourceID: nil).text, "about")
        XCTAssertEqual(c.convert("about", fallbackSourceID: nil).text, "شزخعف")
    }

    func testEnglishScorerPrefersRealWords() {
        func s(_ t: String) -> Double { return EnglishScorer.score(Array(t.unicodeScalars)) }
        XCTAssertGreaterThan(s("light"), s("libt"))
        XCTAssertGreaterThan(s("about"), s("aghout"))
        XCTAssertGreaterThan(s("Good"), s("gHood"))
    }

    func testPickPair() throws {
        let (ar, en) = try layouts(try pcLayoutID())
        let pair = InputSources.pickPair([en, ar], savedA: nil, savedB: nil)
        XCTAssertEqual(pair?.0.id, ar.id)
        XCTAssertEqual(pair?.1.id, en.id)
        XCTAssertNil(InputSources.pickPair([en], savedA: nil, savedB: nil))
    }
}
