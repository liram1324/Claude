# App To Mic 🎙️

<div dir="rtl">

תוכנה ל-Windows שמחברת **אפליקציות לבחירתך** (Spotify, דפדפן/YouTube, משחק, סאונדבורד...) **למיקרופון שלך** –
כך שבשיחה (Discord, Zoom, Teams, WhatsApp Desktop) או בלייב (OBS, TikTok Live) ישמעו גם אותך וגם את האפליקציות.
מפעילים ומכבים בלחיצה אחת – מהתוכנה, בקיצור מקלדת או מכפתור ב-**Stream Deck**.

## איך זה עובד

```
המיקרופון שלך ─────────────┐
Spotify / Chrome / משחק ───┼─► App To Mic ─► CABLE Input ══► CABLE Output ─► Discord / Zoom / OBS
                           │      (מיקס)        (כבל וירטואלי)      ("המיקרופון" החדש)
```

* התוכנה מקליטה **רק** את האפליקציות שסימנת (לא את כל המחשב – אז התראות, הודעות ושאר הצלילים לא יישמעו).
* היא מערבבת אותן עם המיקרופון שלך ושולחת לכבל וירטואלי.
* בתוכנת השיחה בוחרים את הכבל (`CABLE Output`) בתור המיקרופון.
* כשהמתג **כבוי** – עוברים רק הקול שלך. כשהוא **דולק** – גם האפליקציות.

## התקנה (פעם אחת)

1. **התקן כבל וירטואלי חינמי – VB-Cable:** https://vb-audio.com/Cable/
   הורד, חלץ, הרץ את `VBCABLE_Setup_x64.exe` כמנהל ← Install Driver, ואז **הפעל מחדש את המחשב**.
2. **הורד את App To Mic:** בלשונית **Actions** של הריפו ← הריצה האחרונה (Build) ← Artifacts ← `AppToMic-win-x64`
   (או מעמוד ה-Releases אם קיים). זה קובץ `AppToMic.exe` אחד – לא צריך להתקין כלום.
3. הרץ את `AppToMic.exe`.
   אם Windows מציג "Windows protected your PC" – לחץ *More info* ← *Run anyway* (הקובץ פשוט לא חתום דיגיטלית).

**דרישות:** Windows 10 גרסה 2004 ומעלה, או Windows 11 (64 ביט).

## שימוש

1. **אפליקציות** – סמן ✔ את האפליקציות שאתה רוצה שישמעו (🔊 מסמן אפליקציה שמשמיעה קול עכשיו).
   אפשר לסמן גם אפליקציה שעוד לא פתוחה – היא תתחבר אוטומטית כשתיפתח.
2. **התקנים** – *המיקרופון שלי*: המיקרופון האמיתי שלך. *שלח אל*: `CABLE Input` (נבחר אוטומטית).
3. **בתוכנת השיחה/השידור** – בהגדרות הקול בחר מיקרופון: **CABLE Output (VB-Audio Virtual Cable)**.
   * Discord: הגדרות ← Voice & Video ← Input Device.
   * Zoom: הגדרות ← Audio ← Microphone.
   * OBS: Sources ← Audio Input Capture ← CABLE Output.
4. לחץ על הכפתור הגדול (או על הקיצור / Stream Deck) – **אדום = משדר**.

עוצמות: מחוון לקול שלך ומחוון לאפליקציות (עד 200%), עם מד עוצמה ליד כל אחד.
אפשר גם לבטל את "הקול שלי" כדי לשדר רק את האפליקציות.

סגירת החלון משאירה את התוכנה פועלת במגש המערכת (ליד השעון) כדי שהקיצור וה-Stream Deck ימשיכו לעבוד.
ליציאה: קליק ימני על האייקון ← יציאה.

## חיבור ל-Stream Deck

בחר אחת מהדרכים:

| דרך | איך |
|---|---|
| **Hotkey** (הכי פשוט) | גרור פעולת *System → Hotkey* ולחץ `Ctrl + Alt + F9` (או הקיצור שבחרת בתוכנה). |
| **Website** | גרור *System → Website*, כתובת: `http://127.0.0.1:8765/toggle`, וסמן **GET request in background**. |
| **Open** | גרור *System → Open*, ובחר: `"C:\path\to\AppToMic.exe" --toggle` |

במקום `toggle` אפשר גם `on` / `off` (למשל כפתור Multi-Action שמדליק מוזיקה ומפעיל את השידור).
`http://127.0.0.1:8765/status` מחזיר `{"active":true/false}`.
השליטה פתוחה רק מהמחשב עצמו (127.0.0.1).

## טיפים ופתרון בעיות

* **אל תסמן את תוכנת השיחה עצמה** (למשל Discord) – אחרת המשתתפים ישמעו את עצמם.
  אם השיחה רצה בדפדפן (Google Meet / Discord Web) – אל תסמן את אותו דפדפן; השתמש בדפדפן אחר למוזיקה.
* **אני לא שומע את האפליקציה בעצמי?** התוכנה לא משנה את מה שאתה שומע – האפליקציה ממשיכה להתנגן ברמקולים/אוזניות שלך כרגיל.
* **רוצה לשמוע את עצמך דרך הכבל לבדיקה?** Windows ← הגדרות קול ← CABLE Output ← Properties ← Listen ← "Listen to this device".
* **לא שומעים את האפליקציה בשיחה:** ודא שהכפתור אדום, שהאפליקציה מסומנת ומופיעה תחת "מחובר ל:", ושבשיחה נבחר `CABLE Output`.
* **הקול שלי נשמע רובוטי/מקוטע:** בתוכנת השיחה כבה "Noise suppression"/"Echo cancellation" חזקים, או הנמך את עוצמת האפליקציות.
* **"הקיצור תפוס":** בחר קיצור אחר בתיבה (לחץ עליה ואז על צירוף המקשים).
* ההגדרות נשמרות ב-`%AppData%\AppToMic\settings.json` (שם אפשר גם לשנות את הפורט `ControlPort`).

</div>

## Building from source

Requires the .NET 8 SDK on Windows:

```
dotnet publish src/AppToMic/AppToMic.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

How it works: each selected app is captured with the WASAPI *process loopback* API
(`ActivateAudioInterfaceAsync` + `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`, including child processes),
mixed with the microphone using [NAudio](https://github.com/naudio/NAudio), and rendered to a virtual cable.
