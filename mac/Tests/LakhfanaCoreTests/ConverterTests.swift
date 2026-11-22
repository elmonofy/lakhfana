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

    private func pcConverter() throws -> Converter {
        let (ar, en) = try layouts("com.apple.keylayout.Arabic-PC")
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
        for id in ["com.apple.keylayout.US", "com.apple.keylayout.Arabic-PC", "com.apple.keylayout.Arabic"] {
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
        XCTAssertEqual(r.targetID, "com.apple.keylayout.Arabic-PC")
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
        let (ar, en) = try layouts("com.apple.keylayout.Arabic-PC")
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
        try roundTrip("com.apple.keylayout.Arabic-PC")
    }

    func testRoundTripMacArabic() throws {
        try roundTrip("com.apple.keylayout.Arabic")
    }

    func testEnglishScorerPrefersRealWords() {
        func s(_ t: String) -> Double { return EnglishScorer.score(Array(t.unicodeScalars)) }
        XCTAssertGreaterThan(s("light"), s("libt"))
        XCTAssertGreaterThan(s("about"), s("aghout"))
        XCTAssertGreaterThan(s("Good"), s("gHood"))
    }

    func testPickPair() throws {
        let (ar, en) = try layouts("com.apple.keylayout.Arabic-PC")
        let pair = InputSources.pickPair([en, ar], savedA: nil, savedB: nil)
        XCTAssertEqual(pair?.0.id, ar.id)
        XCTAssertEqual(pair?.1.id, en.id)
        XCTAssertNil(InputSources.pickPair([en], savedA: nil, savedB: nil))
    }
}
