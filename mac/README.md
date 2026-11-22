# لخفنه للماك

كتبت بالإنجليزي وإنت فاكر الكيبورد عربي؟ (`hgsghl ugd;l` بدل `السلام عليكم`)
حدد الكلام ودوس الاختصار (الافتراضي <kbd>⌃</kbd> <kbd>⇧</kbd> <kbd>Space</kbd>)، ولخفنه يكتبه صح باللغة التانية.
لو مفيش كلام متحدد، بيحدد السطر لوحده.

يشتغل على macOS 13 (Ventura) أو أحدث، على أجهزة Apple Silicon وIntel.

## التثبيت

1. نزّل `Lakhfana.dmg` من صفحة الـ Releases وافتحه.
2. اسحب أيقونة **لخفنه** على فولدر **Applications**.
3. افتح لخفنه من فولدر Applications (أو من Launchpad). هيظهر كأيقونة صغيرة فوق في شريط القوائم، مش في الـ Dock.

## أول مرة تفتحه (Gatekeeper)

النسخة دي لسه مش موقّعة من Apple، فالماك هيقولك إنه مش قادر يتأكد منها. ده طبيعي، اعمل كده مرة واحدة بس:

**macOS 15 (Sequoia) أو أحدث:**
1. افتح لخفنه. هتطلع رسالة إنه "Not Opened". دوس **Done**.
2. افتح **System Settings ← Privacy & Security**، وانزل لتحت لحد جزء **Security**.
3. هتلاقي سطر بيقول إن لخفنه اتمنع. دوس **Open Anyway**، وأكّد بالباسورد أو Touch ID.
4. افتح لخفنه تاني ودوس **Open**.

> على macOS 15 طريقة كليك يمين ← Open مبقتش بتنفع. لازم من System Settings زي ما فوق.
> زرار Open Anyway بيفضل ظاهر حوالي ساعة بعد ما تحاول تفتح البرنامج. لو مش لاقيه، افتح لخفنه تاني وارجع للإعدادات.

**macOS 13 (Ventura) و 14 (Sonoma):**
- كليك يمين (أو Control + كليك) على لخفنه في Applications ← **Open** ← **Open**.
- أو زي فوق: **System Settings ← Privacy & Security ← Open Anyway**.

**لو بتعرف تستخدم Terminal** (أي نسخة):
```bash
xattr -dr com.apple.quarantine /Applications/Lakhfana.app
```

## صلاحية Accessibility

لخفنه محتاج الصلاحية دي عشان يقدر ينسخ الكلام المتحدد ويكتب مكانه. من غيرها الاختصار مش هيعمل حاجة.

1. أول ما تفتحه هيطلب الصلاحية، أو دوس **افتح الإعدادات** في شاشة الترحيب.
2. في **System Settings ← Privacy & Security ← Accessibility** شغّل المفتاح جنب **لخفنه**.
3. ارجع للبرنامج، هتلاقي الحالة بقت خضرا: "تمام، الصلاحية اتفعّلت".

> بعد ما تنزّل نسخة جديدة ممكن الماك ينسى الصلاحية. لو الاختصار وقف، شيل لخفنه من القايمة بزرار (−) وضيفه تاني بزرار (+)، أو اقفل المفتاح وافتحه.

## البناء من الكود

محتاج ماك عليه **Xcode 15** أو أحدث (افتحه مرة واحدة عشان يكمّل التثبيت). بعدين من فولدر المشروع:

```bash
bash mac/scripts/bundle.sh
```

الناتج:
- `mac/dist/Lakhfana.app` (يشتغل على Apple Silicon وIntel، وموقّع ad-hoc)
- `mac/dist/Lakhfana.dmg`

لو عندك Command Line Tools بس من غير Xcode (`xcode-select --install`)، ابني لمعمارية جهازك بس:

```bash
LAKHFANA_ARCHS=arm64 bash mac/scripts/bundle.sh     # Apple Silicon
LAKHFANA_ARCHS=x86_64 bash mac/scripts/bundle.sh    # Intel
```

التستات: `cd mac && swift test`

كل push على GitHub بيبني الـ dmg أوتوماتيك (Actions ← mac ← Artifacts)، وأي tag بيبدأ بـ `v` (زي `v1.0.0`) بيعمل Release ومعاه الـ dmg.
