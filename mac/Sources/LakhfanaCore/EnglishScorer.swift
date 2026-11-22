import Foundation

/// Letter-bigram Markov model of English (word start → letters → word end). Used only to break ties between
/// keystroke segmentations, e.g. Arabic "لا" = one key or two keys. Same table as the Windows EnglishScorer.cs.
/// The score is the MEAN log-probability per transition, so longer candidates are not penalised for length.
public enum EnglishScorer {
    private static let bigrams =
        "th3.56 he3.07 in2.43 er2.05 an1.99 re1.85 on1.76 at1.49 en1.45 nd1.35 ti1.34 es1.34 or1.28 te1.20 " +
        "of1.17 ed1.17 is1.13 it1.12 al1.09 ar1.07 st1.05 to1.04 nt1.04 ng.95 se.93 ha.93 as.87 ou.87 io.83 " +
        "le.83 ve.83 co.79 me.79 de.76 hi.76 ri.73 ro.73 ic.70 ne.69 ea.69 ra.69 ce.65 li.62 ch.60 ll.58 be.58 " +
        "ma.57 si.55 om.55 ur.54 ca.54 el.53 ta.53 la.52 ns.51 di.50 fo.49 ho.49 pe.48 ec.48 pr.47 no.47 ct.46 " +
        "us.45 ac.45 ot.44 il.43 tr.43 ly.43 nc.42 et.42 ut.42 ss.41 so.41 rs.40 un.39 lo.39 wa.38 ge.38 ie.38 " +
        "wh.38 ee.38 wi.37 em.37 ad.36 ol.36 rt.36 po.35 we.35 na.35 ul.35 ni.34 ts.34 mo.34 ow.33 pa.32 im.32 " +
        "mi.32 ai.32 sh.32 ir.31 su.31 id.30 os.30 iv.29 ia.29 am.29 fi.29 ci.28 vi.28 pl.28 ig.26 tu.26 ev.26 " +
        "ld.25 ry.25 mp.25 fe.24 bl.24 ab.23 gh.23 ty.23 op.23 wo.22 sa.22 ay.22 ex.22 ke.21 fr.21 oo.21 av.20 " +
        "ag.20 if.20 ap.20 gr.19 od.19 bo.19 sp.19 rd.19 do.18 uc.18 bu.18 ei.18 ov.17 by.17 rm.17 ep.17 tt.17 " +
        "oc.17 fa.16 ef.16 cu.16 rn.16 sc.15 gi.15 da.15 yo.15 cr.15 cl.15 du.15 ga.15 qu.15 ue.15 ff.15 ba.14 " +
        "ey.14 ls.14 va.14 um.14 pp.14 ua.14 up.14 lu.14 go.13 ht.13 ru.13 ug.13 ds.13 lt.13 pi.13 rc.13 rr.13 " +
        "eg.13 au.13 ck.13 ew.13 mu.12 br.12 bi.12 pt.12 ak.12 pu.12 ui.12 rg.12 ib.11 tl.11 ny.11 ki.11 rk.11 " +
        "ys.11 ob.11 mm.11 fu.11 ph.11 og.11 ms.11 ye.11 ud.11 mb.10 ip.10 ub.10 oi.10 rl.10 gu.10 dr.10 hr.10 " +
        "cc.10 tw.10 ft.10 wn.10 nu.10 af.09 hu.09 nn.09 eo.09 vo.09 rv.09 nf.09 xp.09 gn.09 sm.09 fl.09 iz.08 " +
        "ok.08 nl.08 my.08 gl.08 aw.08 ju.08 oa.08 eq.08 sy.08 sl.08 ps.08 jo.08 lf.08 nv.08 je.08 nk.08 kn.08 " +
        "gs.07 dy.07 hy.07 ze.07 ks.07 xt.07 bs.07 ik.06 dd.06 cy.06 rp.06 sk.06 xi.06 oe.06 oy.06 ws.06 lv.06 " +
        "dl.05 rf.05 eu.05 dg.05 wr.05 xa.05 yi.04 nm.04 eb.04 rb.04 tm.04 xc.04 eh.04 tc.04 gy.04 ja.04 hn.04 " +
        "yp.04 za.04 gg.04 ym.03 sw.03 lm.03 cs.03 ii.03 ix.03 xe.03 oh.03 lk.03 dv.03 lp.03 ax.03 ox.03 uf.03 " +
        "dm.03 iu.03 sf.03 bt.03 ka.03 yt.03 ek.03 pm.03 ya.03 gt.03"

    private static let startFreq: [Double] = [
        11.7, 4.4, 5.2, 3.2, 2.8, 4.0, 1.6, 4.2, 6.3, 0.6, 0.6, 2.4, 4.3,
        2.3, 7.6, 4.0, 0.2, 2.8, 7.8, 16.0, 1.2, 0.8, 5.5, 0.05, 0.8, 0.05]
    private static let endFreq: [Double] = [
        2.6, 0.2, 0.9, 10.0, 19.0, 4.0, 2.7, 3.0, 0.8, 0.02, 1.5, 4.5, 1.5,
        7.0, 4.0, 0.8, 0.01, 5.0, 14.0, 9.0, 0.3, 0.05, 1.0, 0.2, 6.0, 0.05]

    private static let floorValue = 0.005
    private static let caseFlipPenalty = -4.0

    /// trans[a * 27 + b] = log P(b | a); b == 26 means word end.
    private static let model: (trans: [Double], start: [Double]) = {
        var raw = [Double](repeating: floorValue, count: 26 * 26)
        for tok in bigrams.split(separator: " ") {
            let chars = Array(tok.unicodeScalars)
            guard chars.count > 2 else { continue }
            let a = Int(chars[0].value) - 97, b = Int(chars[1].value) - 97
            var num = String(tok.dropFirst(2))
            if num.hasPrefix(".") { num = "0" + num }
            let v = Double(num) ?? floorValue
            raw[a * 26 + b] = v
        }
        let endTotal = endFreq.reduce(0, +)
        let startTotal = startFreq.reduce(0, +)
        var trans = [Double](repeating: 0, count: 26 * 27)
        var start = [Double](repeating: 0, count: 26)
        for i in 0..<26 {
            var row = 0.0
            for j in 0..<26 { row += raw[i * 26 + j] }
            let end = row * 0.25 * (endFreq[i] / (endTotal / 26.0))
            let total = row + end
            for j in 0..<26 { trans[i * 27 + j] = log(raw[i * 26 + j] / total) }
            trans[i * 27 + 26] = log(max(end, 1e-6) / total)
            start[i] = log(max(startFreq[i], 0.01) / startTotal)
        }
        return (trans, start)
    }()

    /// Mean log-probability per transition; non-letters split words. -infinity when there are no Latin letters.
    public static func score(_ scalars: [Unicode.Scalar]) -> Double {
        let m = model
        var sum = 0.0
        var terms = 0
        var prev = -1
        var prevLower = false
        var i = 0
        while i <= scalars.count {
            var cur = -1
            var upper = false
            if i < scalars.count {
                let v = scalars[i].value
                if v >= 65 && v <= 90 { cur = Int(v) - 65; upper = true }
                else if v >= 97 && v <= 122 { cur = Int(v) - 97 }
            }
            if cur >= 0 {
                sum += prev < 0 ? m.start[cur] : m.trans[prev * 27 + cur]
                if upper && prev >= 0 && prevLower { sum += caseFlipPenalty }
                terms += 1
                prevLower = !upper
            } else if prev >= 0 {
                sum += m.trans[prev * 27 + 26]
                terms += 1
            }
            prev = cur
            i += 1
        }
        return terms == 0 ? -Double.infinity : sum / Double(terms)
    }
}
