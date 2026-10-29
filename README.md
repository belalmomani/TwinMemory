# TwinMemory — الذاكرة المشتركة
- Android: افتح المجلد في Android Studio (JDK 17) أو ابنِ بـ Gradle 8.9. Application ID: `com.belalmomani.twinmemory`.
- التوقيع: ضع `keystore.properties` محليًا (storeFile, storePassword, keyAlias, keyPassword) أو متغيرات البيئة TM_KEYSTORE_PATH / TM_KEYSTORE_PASSWORD / TM_KEY_ALIAS / TM_KEY_PASSWORD. لا يُرفع إلى Git.
- `app/google-services.json` مستثنى من Git؛ ضعه محليًا أو عبر سر CI.
- `design/`: مصادر الأيقونة (SVG/PNG/ICO) وملف الألوان. ملف .ico جاهز لتطبيق Windows لاحقًا.

## بناء APK عبر GitHub Actions
بعد رفع المشروع: تبويب Actions ← Build APK ← Run workflow ← حمّل `TwinMemory-apk` من Artifacts.
أسرار اختيارية (Settings ← Secrets ← Actions): `TM_KEYSTORE_BASE64` و`TM_KEYSTORE_PASSWORD` و`TM_KEY_ALIAS` و`TM_KEY_PASSWORD` للتوقيع الرسمي، و`GOOGLE_SERVICES_JSON_BASE64` (غير مطلوب حاليًا). بدونها يُبنى APK تجريبي (Debug).
